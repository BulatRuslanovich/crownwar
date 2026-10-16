using System.Numerics;
using Warcrown.Sim.Buildings;
using Warcrown.Sim.Core;
using Warcrown.Sim.Units;
using Warcrown.Sim.World;

namespace Warcrown.Sim.AI;

/// <summary>The bot's economy: what to build and where, which upgrades to buy, which troops to train.</summary>
public sealed partial class BotBrain
{
    /// <summary>The profile's pacing: no mine before <see cref="BotProfile.FirstMineDelay"/>, then one per <see cref="BotProfile.MineInterval"/>.</summary>
    private bool MayStartMine() =>
        _sim.Tick >= _profile.FirstMineDelay * Simulation.TicksPerSecond
        && _sim.Tick - _lastMineTick >= _profile.MineInterval * Simulation.TicksPerSecond;

    /// <summary>Places buildings it can afford and returns gold to keep aside for a planned one.</summary>
    private int PlanConstruction(Faction me, Building capital, List<Unit> army, bool threatened)
    {
        int gold = me.Treasury.Gold;
        int reserve = 0;
        var mineType = _sim.Rules.BuildingTypes.First(t => t.IsMine);

        // 1. A mine on the expansion deposit as soon as the escort is there.
        if (_expansion is { } deposit && !threatened)
        {
            if (deposit.MineId != 0 || !IsSafe(deposit.Center))
            {
                _expansion = null;
            }
            else
            {
                var error = _sim.CanBuild(FactionId, mineType.Id, deposit.Anchor);
                if (error == BuildError.None)
                {
                    Issue(new BuildCommand(FactionId, mineType.Id, deposit.Anchor));
                    gold -= mineType.Cost;
                    _expansion = null;
                    _lastMineTick = _sim.Tick;
                }
                else
                {
                    reserve = mineType.Cost;
                }
            }
        }
        // Exhausted mines do not count: while they refill, the bot may open new ones elsewhere.
        if (OwnBuildings().Count(b => b.Type.IsMine && b.Deposit is not { IsExhausted: true }) < _profile.MaxMines && MayStartMine())
            _expansion ??= ChooseExpansion(capital);
        else
            _expansion = null;

        // 2. Barracks when the army limit is the bottleneck, or now and then with gold to spare, to open the next kind of
        //    unit (each finished barracks opens one).
        var barracksType = _sim.Rules.BuildingTypes.FirstOrDefault(t => t.ArmyCap > 0 && t.IsBuildable && !t.IsMine && t.Attack == null);
        int barracks = OwnBuildings().Count(b => b.Type.Id == barracksType?.Id);
        bool capped = me.Population >= me.ArmyCap - 1;
        bool unlockDue = barracksType is { UnlocksUnits: true } && !threatened
                         && _sim.Tick - _lastBarracksTick >= BarracksIntervalSeconds * Simulation.TicksPerSecond
                         && gold - reserve >= barracksType.Cost * 2;
        if (barracksType != null && barracks < _profile.MaxBarracks && (capped || unlockDue))
        {
            if (gold - reserve >= barracksType.Cost && TryPlaceNear(barracksType, capital.Position, 3, 7))
            {
                gold -= barracksType.Cost;
                _lastBarracksTick = _sim.Tick;
            }
            else if (capped)
            {
                reserve += barracksType.Cost;
            }
        }

        // 3. Towers next to own mines.
        var towerType = _sim.Rules.BuildingTypes.FirstOrDefault(t => t.Attack is { IsSplash: false } && t.IsBuildable);
        if (_profile.BuildsTowers && towerType != null && !threatened
            && (towerType.MaxCount == 0 || _sim.CountBuildings(FactionId, towerType.Id) < towerType.MaxCount))
        {
            foreach (var mine in OwnBuildings().Where(b => b.Type.IsMine && b.IsComplete))
            {
                // Big towers need room: search further out, and count a tower as this mine's guard that far too.
                int maxRadius = 3 + towerType.Size;
                int guards = OwnBuildings().Count(b => b.Type.Id == towerType.Id && Vector2.Distance(b.Position, mine.Position) < maxRadius + towerType.Size);
                if (guards >= TowersPerMine)
                    continue;
                if (gold - reserve < towerType.Cost)
                {
                    // A tower is a big purchase: save up for it (one at a time) instead of spending all on troops.
                    reserve += towerType.Cost;
                    break;
                }
                if (TryPlaceNear(towerType, mine.Position, 2, maxRadius))
                    gold -= towerType.Cost;
            }
        }
        return reserve;
    }

    /// <summary>
    /// Upgrades one working mine at a time (the lowest level first) when there is gold to spare after construction,
    /// and saves up for it once the army is half way to attack strength. Towers are left at their first level:
    /// bots that also raise them turtle up and four-bot matches stop ending. Returns the gold committed or set
    /// aside, so recruiting does not spend it.
    /// </summary>
    private int PlanUpgrades(Faction me, int reserve, int armyValue)
    {
        var own = OwnBuildings();
        int levelsBought = own.Sum(b => b.Type.Tier - 1);
        if (own.Any(b => b.IsUpgrading) || levelsBought >= _profile.MaxUpgrades
            || _sim.Tick < _profile.UpgradeAfter * Simulation.TicksPerSecond)
            return 0;
        var next = own
            .Where(b => _sim.CanUpgrade(FactionId, b.Id) is UpgradeError.None or UpgradeError.NotEnoughGold)
            .Where(b => b.Type.IsMine && b.Deposit is { IsExhausted: false })
            .OrderBy(b => b.Type.Tier)
            .ThenBy(b => b.Id)
            .FirstOrDefault();
        if (next == null)
            return 0;
        int cost = next.Type.UpgradeCost;
        if (me.Treasury.Gold - reserve < cost)
            return armyValue >= _profile.AttackArmyValue / 2 ? cost : 0;
        Issue(new UpgradeCommand(FactionId, next.Id));
        return cost;
    }

    /// <summary>Keeps the training queues busy with what gold allows beyond the reserve.</summary>
    private void Recruit(Faction me, Building capital, int reserve, bool urgent)
    {
        if (urgent)
            reserve = 0; // under attack: soldiers before buildings
        int gold = me.Treasury.Gold;
        int population = me.Population;
        var producers = OwnBuildings().Where(b => b.IsComplete && b.Type.Recruits.Count > 0).OrderBy(b => b.Queue.Count).ToList();

        foreach (var building in producers)
        {
            // Keep every training place busy, with one more waiting.
            if (population >= me.ArmyCap || building.Queue.Count >= _sim.TrainingSlots(building) + 1)
                continue;
            // Keep the chosen type until it is bought, so expensive units are saved for, not skipped (unless the barracks
            // that opened it fell meanwhile).
            if (_nextUnit != null && !_sim.IsUnlocked(FactionId, _nextUnit))
                _nextUnit = null;
            _nextUnit ??= ChooseUnitType(building);
            if (!building.Type.Recruits.Contains(_nextUnit.Id))
                continue;
            if (gold - _nextUnit.Cost < reserve)
                break;
            Issue(new RecruitCommand(FactionId, building.Id, _nextUnit.Id));
            gold -= _nextUnit.Cost;
            population++;
            _nextUnit = null;
        }
    }

    private UnitType ChooseUnitType(Building producer)
    {
        var options = producer.Type.Recruits.Select(_sim.Rules.Units.Get).Where(t => _sim.IsUnlocked(FactionId, t)).ToList();
        if (options.Count == 0)
            return producer.Type.Recruits.Select(_sim.Rules.Units.Get).OrderBy(t => t.BarracksRequired).First();
        // Siege engines come once there is an army to escort them: about one per six soldiers; a monk per eight.
        var army = _sim.Units.Where(u => u.FactionId == FactionId).ToList();
        bool wantRam = army.Count >= SoldiersPerRam && army.Count(u => u.Type.IsSiege) * SoldiersPerRam < army.Count;
        bool wantMonk = army.Count >= MonkEvery && army.Count(u => u.Type.IsHealer) * MonkEvery < army.Count;
        int siegeKinds = Math.Max(1, options.Count(t => t.IsSiege));
        var weights = options.Select(t => t.IsSiege ? (wantRam ? 0.2f / siegeKinds : 0f)
            : t.IsHealer ? (wantMonk ? 0.15f : 0f)
            : BaseWeight(t)).ToArray();

        if (_profile.CountersComposition)
        {
            var enemies = _sim.Units.Where(u => _sim.AreEnemies(FactionId, u.FactionId) && u.FactionId >= 0).ToList();
            if (enemies.Count > 0)
            {
                float rangedShare = enemies.Count(u => u.Type.IsRanged) / (float)enemies.Count;
                float armoredShare = enemies.Count(u => u.Type.RangedArmor > 0f) / (float)enemies.Count;
                for (int i = 0; i < options.Count; i++)
                {
                    if (options[i].IsSiege || options[i].IsHealer)
                        continue;
                    if (options[i].ArmorPiercing > 0f) weights[i] *= 1f + 2f * armoredShare; // bolts through plate
                    // Arrow armor answers archers; cheap swarms answer heavies; archers answer plain infantry.
                    if (options[i].RangedArmor > 0f) weights[i] *= 1f + 2f * rangedShare;
                    if (options[i].IsRanged) weights[i] *= 1.5f - armoredShare;
                    if (!options[i].IsRanged && options[i].RangedArmor == 0f) weights[i] *= 1f + 2f * armoredShare;
                }
            }
        }

        float roll = (float)_rng.NextDouble() * weights.Sum();
        for (int i = 0; i < options.Count; i++)
        {
            roll -= weights[i];
            if (roll <= 0f)
                return options[i];
        }
        return options[^1];

        // A fixed mix: mostly infantry, some archers and heavies.
        static float BaseWeight(UnitType t) => t.IsRanged ? 0.25f : t.RangedArmor > 0f ? 0.25f : 0.5f;
    }

    /// <summary>Nearest free deposit that is closer to our castle than to any enemy castle and not guarded.</summary>
    private GoldDeposit? ChooseExpansion(Building capital)
    {
        var enemyCapitals = _sim.Factions
            .Where(f => _sim.AreEnemies(FactionId, f.Id) && !f.IsEliminated)
            .Select(f => _sim.GetBuilding(f.CapitalId)).OfType<Building>().ToList();
        int home = _sim.Map.ComponentOf(Cell.FromPosition(SpawnSide(capital)));

        return _sim.Deposits
            .Where(d => d.MineId == 0 && !d.IsExhausted && IsSafe(d.Center))
            .Where(d => NearbyComponent(d.Center) == home)
            .Where(d => enemyCapitals.All(e => Vector2.Distance(d.Center, e.Position) > Vector2.Distance(d.Center, capital.Position) * 0.8f))
            .OrderBy(d => Vector2.Distance(d.Center, capital.Position) / d.Yield)
            .FirstOrDefault();
    }

    /// <summary>
    /// Tries anchors on rings around <paramref name="center"/> until the rules accept one; with
    /// <paramref name="sendBuilder"/> also one where nobody stands but a soldier can walk over to build.
    /// </summary>
    private bool TryPlaceNear(BuildingType type, Vector2 center, int minRadius, int maxRadius, bool sendBuilder = false)
    {
        var c = Cell.FromPosition(center);
        for (int r = minRadius; r <= maxRadius; r++)
        {
            _ring.Clear();
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) == r)
                        _ring.Add(new Cell(c.X + dx, c.Y + dy));
            // Shuffle so bases do not all grow in the same direction: a random key per cell, in ring order, and a
            // stable sort by it (ties keep ring order).
            _ringOrder.Clear();
            for (int i = 0; i < _ring.Count; i++)
                _ringOrder.Add((_rng.Next(), i));
            _ringOrder.Sort();
            foreach (var (_, index) in _ringOrder)
            {
                var anchor = _ring[index];
                var error = _sim.CanBuild(FactionId, type.Id, anchor);
                bool walkOver = sendBuilder && error == BuildError.NoUnitsNearby && _sim.FindBuilder(FactionId, type.Id, anchor) != null;
                if (error != BuildError.None && !walkOver || BlocksNeighbours(anchor, type.Size))
                    continue;
                Issue(new BuildCommand(FactionId, type.Id, anchor));
                return true;
            }
        }
        return false;
    }

    /// <summary>Keeps a free ring around new buildings so the base does not wall itself in.</summary>
    private bool BlocksNeighbours(Cell anchor, int size)
    {
        for (int y = -1; y <= size; y++)
        {
            for (int x = -1; x <= size; x++)
            {
                var cell = new Cell(anchor.X + x, anchor.Y + y);
                if (_sim.Map.IsOccupied(cell) || _sim.DepositAt(cell) != null)
                    return true;
            }
        }
        return false;
    }
}

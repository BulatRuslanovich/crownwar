using System.Numerics;
using Lordsio.Sim.Buildings;
using Lordsio.Sim.Core;
using Lordsio.Sim.Units;
using Lordsio.Sim.World;

namespace Lordsio.Sim.AI;

public enum BotMode
{
    /// <summary>Army waits by the castle.</summary>
    Gather,
    /// <summary>Army escorts the expansion to a free gold deposit.</summary>
    Expand,
    /// <summary>Enemies are near own buildings; the army goes to meet them.</summary>
    Defend,
    /// <summary>Army marches on an enemy building.</summary>
    Attack,
    /// <summary>The attack went badly; the army walks home without fighting.</summary>
    Retreat,
}

/// <summary>
/// A computer opponent. It reads the public state of the <see cref="Simulation"/> and acts only through
/// <see cref="Simulation.Issue"/>, exactly like the player's UI, so it plays by the same rules.
/// Call <see cref="Update"/> once per tick; it thinks every <see cref="BotProfile.ThinkInterval"/> seconds.
/// </summary>
public sealed class BotBrain
{
    /// <summary>Enemy units this close (tiles) to one of our buildings count as an attack on us.</summary>
    private const float ThreatRadius = 9f;
    /// <summary>Units further than this from where they were sent get the order again.</summary>
    private const float RegroupDistance = 4f;
    /// <summary>Enemy units this close to a target building are counted as its defenders.</summary>
    private const float DefenceRadius = 8f;
    /// <summary>Worth credited to a tower when judging how well a target is defended.</summary>
    private const int TowerDefenceValue = 250;
    private const int TowersPerMine = 1;

    private readonly Simulation _sim;
    private readonly BotProfile _profile;
    private readonly Random _rng;
    private readonly int _thinkTicks;
    private readonly int _thinkOffset;

    private Vector2? _armyGoal;
    private int _attackTargetId;
    private int _launchValue;
    private GoldDeposit? _expansion;
    private UnitType? _nextUnit;

    public BotBrain(Simulation sim, int factionId, BotDifficulty difficulty, int seed)
        : this(sim, factionId, BotProfile.For(difficulty), seed)
    {
    }

    public BotBrain(Simulation sim, int factionId, BotProfile profile, int seed)
    {
        _sim = sim;
        FactionId = factionId;
        _profile = profile;
        _rng = new Random(seed);
        _thinkTicks = Math.Max(1, (int)MathF.Round(profile.ThinkInterval * Simulation.TicksPerSecond));
        // Spread bots over different ticks so they do not all think at once.
        _thinkOffset = (factionId * 7) % _thinkTicks;
    }

    public int FactionId { get; }
    public BotMode Mode { get; private set; } = BotMode.Gather;

    public void Update()
    {
        if (_sim.Tick % _thinkTicks == _thinkOffset)
            Think();
    }

    /// <summary>One decision round: economy first, then what the army does.</summary>
    public void Think()
    {
        if (_sim.GetFaction(FactionId) is not { IsEliminated: false } me || _sim.GetBuilding(me.CapitalId) is not { } capital)
            return;

        var army = _sim.Units.Where(u => u.FactionId == FactionId).ToList();
        int armyValue = army.Sum(u => u.Type.Cost);
        var threats = FindThreats();

        int reserve = PlanConstruction(me, capital, army, threatened: threats.Count > 0);
        Recruit(me, capital, reserve, urgent: threats.Count > 0);
        CommandArmy(capital, army, armyValue, threats);
    }

    // ---------------------------------------------------------------- economy

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
                    _sim.Issue(new BuildCommand(FactionId, mineType.Id, deposit.Anchor));
                    gold -= mineType.Cost;
                    _expansion = null;
                }
                else
                {
                    reserve = mineType.Cost;
                }
            }
        }
        if (OwnBuildings().Count(b => b.Type.IsMine) < _profile.MaxMines)
            _expansion ??= ChooseExpansion(capital);
        else
            _expansion = null;

        // 2. Barracks when the army limit is the bottleneck.
        var barracksType = _sim.Rules.BuildingTypes.FirstOrDefault(t => t.ArmyCap > 0 && t.IsBuildable && !t.IsMine && t.Attack == null);
        int barracks = OwnBuildings().Count(b => b.Type == barracksType);
        if (barracksType != null && barracks < _profile.MaxBarracks && me.Population >= me.ArmyCap - 1)
        {
            if (gold - reserve >= barracksType.Cost && TryPlaceNear(barracksType, capital.Position, 3, 7))
                gold -= barracksType.Cost;
            else
                reserve += barracksType.Cost;
        }

        // 3. Towers next to own mines.
        var towerType = _sim.Rules.BuildingTypes.FirstOrDefault(t => t.Attack != null && t.IsBuildable);
        if (_profile.BuildsTowers && towerType != null && !threatened)
        {
            foreach (var mine in OwnBuildings().Where(b => b.Type.IsMine && b.IsComplete))
            {
                int guards = OwnBuildings().Count(b => b.Type == towerType && Vector2.Distance(b.Position, mine.Position) < 5f);
                if (guards >= TowersPerMine || gold - reserve < towerType.Cost)
                    continue;
                if (TryPlaceNear(towerType, mine.Position, 2, 4))
                    gold -= towerType.Cost;
            }
        }
        return reserve;
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
            if (population >= me.ArmyCap || building.Queue.Count >= 2)
                continue;
            // Keep the chosen type until it is bought, so expensive units are saved for, not skipped.
            _nextUnit ??= ChooseUnitType(building);
            if (!building.Type.Recruits.Contains(_nextUnit.Id))
                continue;
            if (gold - _nextUnit.Cost < reserve)
                break;
            _sim.Issue(new RecruitCommand(FactionId, building.Id, _nextUnit.Id));
            gold -= _nextUnit.Cost;
            population++;
            _nextUnit = null;
        }
    }

    private UnitType ChooseUnitType(Building producer)
    {
        var options = producer.Type.Recruits.Select(_sim.Rules.Units.Get).ToList();
        var weights = options.Select(t => BaseWeight(t)).ToArray();

        if (_profile.CountersComposition)
        {
            var enemies = _sim.Units.Where(u => u.FactionId != FactionId && u.FactionId >= 0).ToList();
            if (enemies.Count > 0)
            {
                float rangedShare = enemies.Count(u => u.Type.IsRanged) / (float)enemies.Count;
                float armoredShare = enemies.Count(u => u.Type.RangedArmor > 0f) / (float)enemies.Count;
                for (int i = 0; i < options.Count; i++)
                {
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
            .Where(f => f.Id != FactionId && !f.IsEliminated)
            .Select(f => _sim.GetBuilding(f.CapitalId)).OfType<Building>().ToList();
        int home = _sim.Map.ComponentOf(Cell.FromPosition(SpawnSide(capital)));

        return _sim.Deposits
            .Where(d => d.MineId == 0 && IsSafe(d.Center))
            .Where(d => NearbyComponent(d.Center) == home)
            .Where(d => enemyCapitals.All(e => Vector2.Distance(d.Center, e.Position) > Vector2.Distance(d.Center, capital.Position) * 0.8f))
            .OrderBy(d => Vector2.Distance(d.Center, capital.Position) / d.Yield)
            .FirstOrDefault();
    }

    /// <summary>Tries anchors on rings around <paramref name="center"/> until the rules accept one.</summary>
    private bool TryPlaceNear(BuildingType type, Vector2 center, int minRadius, int maxRadius)
    {
        var c = Cell.FromPosition(center);
        for (int r = minRadius; r <= maxRadius; r++)
        {
            var ring = new List<Cell>();
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) == r)
                        ring.Add(new Cell(c.X + dx, c.Y + dy));
            // Shuffle so bases do not all grow in the same direction.
            foreach (var anchor in ring.OrderBy(_ => _rng.Next()))
            {
                if (_sim.CanBuild(FactionId, type.Id, anchor) != BuildError.None || BlocksNeighbours(anchor, type.Size))
                    continue;
                _sim.Issue(new BuildCommand(FactionId, type.Id, anchor));
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

    // ---------------------------------------------------------------- army

    private void CommandArmy(Building capital, List<Unit> army, int armyValue, List<Unit> threats)
    {
        if (army.Count == 0)
        {
            Mode = BotMode.Gather;
            return;
        }

        if (threats.Count > 0)
        {
            Mode = BotMode.Defend;
            // Meet the intruders closest to home first.
            var nearest = threats.OrderBy(t => Vector2.Distance(t.Position, capital.Position)).First();
            Send(army, nearest.Position, MoveMode.AttackMove);
            return;
        }

        if (Mode == BotMode.Attack)
        {
            var target = _sim.GetBuilding(_attackTargetId);
            if (target == null)
            {
                Mode = BotMode.Gather; // destroyed: pick the next target below
            }
            else if (armyValue < _launchValue * _profile.RetreatFraction)
            {
                Mode = BotMode.Retreat;
                Send(army, RallyPoint(capital), MoveMode.Move, force: true);
                return;
            }
            else
            {
                Send(army, target.Position, MoveMode.AttackMove);
                return;
            }
        }

        if (Mode == BotMode.Retreat)
        {
            var rally = RallyPoint(capital);
            if (army.Average(u => Vector2.Distance(u.Position, rally)) > RegroupDistance * 2)
                return; // still walking home
            Mode = BotMode.Gather;
        }

        if (armyValue >= _profile.AttackArmyValue && ChooseAttackTarget(capital, armyValue) is { } attackTarget)
        {
            Mode = BotMode.Attack;
            _attackTargetId = attackTarget.Id;
            _launchValue = armyValue;
            Send(army, attackTarget.Position, MoveMode.AttackMove, force: true);
            return;
        }

        if (_expansion is { } deposit)
        {
            Mode = BotMode.Expand;
            // Stand next to the ore (towards home), not on it, so the mine can be placed.
            var toHome = Vector2.Normalize(capital.Position - deposit.Center);
            Send(army, deposit.Center + toHome * 2.5f, MoveMode.AttackMove);
            return;
        }

        Mode = BotMode.Gather;
        Send(army, RallyPoint(capital), MoveMode.AttackMove);
    }

    /// <summary>
    /// Cheapest prize for the effort: distance plus how well it is defended. Castles only once
    /// the army is big enough for a siege; their fall wins the war.
    /// </summary>
    private Building? ChooseAttackTarget(Building capital, int armyValue)
    {
        Building? best = null;
        float bestScore = float.MaxValue;
        foreach (var building in _sim.Buildings)
        {
            if (building.FactionId == FactionId)
                continue;
            if (building.Type.IsCapital && armyValue < _profile.CapitalAssaultValue)
                continue;
            int defence = DefenceValue(building.Position, building.FactionId);
            if (defence * _profile.AttackAdvantage > armyValue)
                continue;
            float score = Vector2.Distance(building.Position, capital.Position) + defence / 25f;
            if (building.Type.IsCapital)
                score *= 0.6f; // the decisive target
            if (score < bestScore)
            {
                bestScore = score;
                best = building;
            }
        }
        return best;
    }

    private int DefenceValue(Vector2 point, int owner)
    {
        int units = _sim.Units.Where(u => u.FactionId == owner && Vector2.Distance(u.Position, point) <= DefenceRadius).Sum(u => u.Type.Cost);
        // Shooting buildings (towers, the castle) count in proportion to their damage.
        int buildings = _sim.Buildings
            .Where(b => b.FactionId == owner && b.Type.Attack != null && b.IsComplete && Vector2.Distance(b.Position, point) <= DefenceRadius)
            .Sum(b => TowerDefenceValue * b.Type.Attack!.Damage / 10);
        return units + buildings;
    }

    /// <summary>
    /// Orders units towards <paramref name="goal"/>, skipping those already fighting or already on their
    /// way, so repeated decisions do not reset the march every second.
    /// </summary>
    private void Send(List<Unit> army, Vector2 goal, MoveMode mode, bool force = false)
    {
        bool newGoal = force || _armyGoal is not { } previous || Vector2.Distance(previous, goal) > 3f;
        _armyGoal = goal;
        var ids = army
            .Where(u => newGoal || (u.Target == null && u.Order == UnitOrder.None && Vector2.Distance(u.Position, goal) > RegroupDistance))
            .Where(u => mode == MoveMode.Move || u.Target == null || newGoal)
            .Select(u => u.Id).ToList();
        if (ids.Count > 0)
            _sim.Issue(new MoveCommand(FactionId, ids, goal, mode));
    }

    // ---------------------------------------------------------------- helpers

    private IEnumerable<Building> OwnBuildings() => _sim.Buildings.Where(b => b.FactionId == FactionId);

    private List<Unit> FindThreats()
    {
        var own = OwnBuildings().ToList();
        return _sim.Units
            .Where(u => u.FactionId != FactionId && u.IsAlive && own.Any(b => b.SurfaceDistance(u.Position) <= ThreatRadius))
            .ToList();
    }

    /// <summary>No enemy units or buildings near the point.</summary>
    private bool IsSafe(Vector2 point) =>
        !_sim.Units.Any(u => u.FactionId != FactionId && Vector2.Distance(u.Position, point) < DefenceRadius)
        && !_sim.Buildings.Any(b => b.FactionId != FactionId && Vector2.Distance(b.Position, point) < DefenceRadius);

    private Vector2 RallyPoint(Building capital) => SpawnSide(capital) + new Vector2(0f, 1.5f);

    private static Vector2 SpawnSide(Building capital) => capital.Position + new Vector2(0f, capital.Type.Size / 2f + 1f);

    /// <summary>Walkable region next to a point (deposit centers are on open ground, but may be built on).</summary>
    private int NearbyComponent(Vector2 point) =>
        _sim.Map.FindNearestInComponent(Cell.FromPosition(point), TileGrid.AnyComponent, 4) is { } cell
            ? _sim.Map.ComponentOf(cell)
            : -1;
}

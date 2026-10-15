using System.Numerics;
using Lordsio.Sim.Buildings;
using Lordsio.Sim.Units;
using Lordsio.Sim.World;

namespace Lordsio.Sim.Core;

/// <summary>
/// The Defense mode: the players' team holds a shared citadel (owned by an allied garrison) against endless waves of
/// a horde coming in from the edge of the map. Each wave is announced a while before, grows with its number and the
/// number of players, has its theme (a line of foot, a cavalry raid on the mines, a siege, a warlord) and pays every
/// player for being wiped out. A player's outpost may fall and be built again; the citadel falling ends the match.
/// </summary>
public sealed partial class Simulation
{
    private const int Raid = 4, Siege = 5, Boss = 10;

    /// <summary>The Defense mode's state; null in a versus match.</summary>
    public DefenseState? Defense { get; private set; }

    /// <summary>Seconds until the next wave comes; 0 outside the Defense mode.</summary>
    public float SecondsToNextWave => Defense is { } d ? Math.Max(0, d.NextWaveTick - Tick) / (float)TicksPerSecond : 0f;

    private void SetUpDefense(MapDefinition map, MatchSetup setup)
    {
        var type = Rules.Citadel ?? throw new ArgumentException("The rules have no citadel: the Defense mode cannot be played.");
        var anchor = map.Citadel ?? throw new ArgumentException("The map has no citadel: it was not made for the Defense mode.");
        for (int y = 0; y < type.Size; y++)
            for (int x = 0; x < type.Size; x++)
                if (!Map.IsWalkable(new Cell(anchor.X + x, anchor.Y + y)))
                    throw new ArgumentException($"The citadel does not fit at {anchor}.");
        var citadel = AddBuilding(type, setup.GarrisonFactionId, anchor, constructionTicks: 0);
        Defense = new DefenseState(setup.FactionCount, setup.GarrisonFactionId, setup.HordeFactionId, citadel.Id,
            Ticks.FromSeconds(Rules.Defense.FirstWaveSeconds), setup.Seed);
        // The ground the waves must reach: the region in front of the citadel's gate.
        var gate = Cell.FromPosition(citadel.Position + new Vector2(0f, type.Size / 2f + 0.8f));
        Defense.CitadelComponent = Map.FindNearestInComponent(gate, TileGrid.AnyComponent, 8) is { } open ? Map.ComponentOf(open) : -1;
    }

    /// <summary>A new outpost for a player whose own has fallen: the castle becomes buildable for them, one at a time.</summary>
    public bool MayBuildOutpost(int factionId, BuildingType type) =>
        Defense is { } d && type.IsCapital && d.IsPlayer(factionId)
        && CountBuildings(factionId, type.Id) + PlannedCount(factionId, type.Id) == 0;

    /// <summary>What a building costs: a castle (only ever a new outpost in Defense) at the mode's price.</summary>
    public int BuildCost(BuildingType type) => Defense != null && type.IsCapital ? Rules.Defense.OutpostCost : type.Cost;

    private float BuildSeconds(BuildingType type) => Defense != null && type.IsCapital ? Rules.Defense.OutpostBuildSeconds : type.BuildTime;

    private void UpdateDefense()
    {
        if (Defense is not { } d)
            return;
        if (d.Incoming.Count == 0 && Tick >= d.NextWaveTick - Ticks.FromSeconds(Rules.Defense.WarningSeconds))
            AnnounceWave(d);
        if (Tick >= d.NextWaveTick)
        {
            LaunchWave(d);
            d.NextWaveTick += Ticks.FromSeconds(Rules.Defense.WaveIntervalSeconds);
        }
        if (Tick % TicksPerSecond == 0)
            SendIdleHordeOn(d);
    }

    /// <summary>The next wave's sides are chosen and told to everyone (more sides as the waves go on).</summary>
    private void AnnounceWave(DefenseState d)
    {
        int wave = d.WavesLaunched + 1;
        var rules = Rules.Defense;
        int count = wave >= rules.ThreeSidesFrom ? 3 : wave >= rules.TwoSidesFrom ? 2 : 1;
        var sides = new List<int> { 0, 1, 2, 3 };
        for (int i = sides.Count - 1; i > 0; i--)
        {
            int j = d.Rng.Next(i + 1);
            (sides[i], sides[j]) = (sides[j], sides[i]);
        }
        foreach (int side in sides)
        {
            if (d.Incoming.Count == count)
                break;
            if (WaveEntry(d, side) is { } point)
                d.Incoming.Add(point);
        }
        _events.Add(new WaveIncoming(wave, d.Incoming.ToArray(), SecondsToNextWave));
    }

    /// <summary>A point on open ground a few tiles in from the middle part of one edge (0 north, 1 east, 2 south, 3 west), from which the citadel can be reached.</summary>
    private Vector2? WaveEntry(DefenseState d, int side)
    {
        const int inset = 3;
        int w = Map.Width, h = Map.Height;
        int along = (int)((d.Rng.NextDouble() - 0.5) * 0.6 * (side % 2 == 0 ? w : h));
        var cell = side switch
        {
            0 => new Cell(w / 2 + along, inset),
            1 => new Cell(w - 1 - inset, h / 2 + along),
            2 => new Cell(w / 2 + along, h - 1 - inset),
            _ => new Cell(inset, h / 2 + along),
        };
        return Map.FindNearestInComponent(cell, d.CitadelComponent, 24)?.Center;
    }

    private void LaunchWave(DefenseState d)
    {
        if (d.Incoming.Count == 0)
            AnnounceWave(d); // the warning was missed (a very short interval): announce now
        int wave = ++d.WavesLaunched;
        var rules = Rules.Defense;
        int budget = (int)(rules.BaseBudget * MathF.Pow(rules.Growth, wave - 1) * (1f + rules.ExtraPlayerShare * (d.Players - 1)));
        var troops = ComposeWave(d, wave, budget);
        var groups = d.Incoming.Select(_ => new List<Unit>()).ToList();
        if (groups.Count > 0)
        {
            var sideTypes = groups.Select(_ => new List<UnitType>()).ToList();
            for (int i = 0; i < troops.Count; i++)
                sideTypes[i % groups.Count].Add(troops[i]);
            for (int s = 0; s < groups.Count; s++)
            {
                if (sideTypes[s].Count == 0)
                    continue;
                var slots = Formation.ComputeSlots(Map, d.Incoming[s], sideTypes[s].Count, sideTypes[s].Max(t => t.Radius));
                for (int i = 0; i < sideTypes[s].Count; i++)
                {
                    var unit = SpawnUnit(sideTypes[s][i].Id, d.HordeFactionId, slots[i]);
                    d.WaveOfUnit[unit.Id] = wave;
                    groups[s].Add(unit);
                }
                var target = wave % Raid == 0 && wave % Siege != 0 ? NearestPlayerMine(d, d.Incoming[s]) ?? CitadelPosition(d) : CitadelPosition(d);
                OrderGroupMove(groups[s], d.CitadelComponent, target, UnitOrder.AttackMove);
            }
        }
        int spawned = groups.Sum(g => g.Count);
        if (spawned > 0)
            d.AliveInWave[wave] = spawned;
        _events.Add(new WaveStarted(wave, spawned, d.Incoming.ToArray()));
        d.Incoming.Clear();
    }

    /// <summary>
    /// The troops of a wave worth <paramref name="budget"/> gold: a warlord every tenth wave, rams (and later trebuchets)
    /// taking a third of every fifth, riders making up most of every fourth (a raid on the mines), and the rest a line
    /// of foot that grows tougher with the waves: spearmen, then archers, men-at-arms, crossbowmen and riders.
    /// </summary>
    private List<UnitType> ComposeWave(DefenseState d, int wave, int budget)
    {
        var units = Rules.Units;
        var troops = new List<UnitType>();
        int left = budget;
        int cap = (int)(Rules.Defense.MaxUnitsPerWave * (1f + Rules.Defense.ExtraPlayerShare * (d.Players - 1)));
        bool Take(string id)
        {
            if (!units.Contains(id) || troops.Count >= cap)
                return false;
            var type = units.Get(id);
            troops.Add(type);
            left -= type.Cost;
            return true;
        }

        if (wave % Boss == 0)
            Take(Rules.Defense.BossUnit);
        if (wave % Siege == 0)
        {
            int siege = budget / 3;
            while (siege > 0 && left > 0)
            {
                string engine = wave >= 15 && d.Rng.Next(3) == 0 ? "trebuchet" : "ram";
                if (!Take(engine))
                    break;
                siege -= units.Get(engine).Cost;
            }
        }

        var mix = new List<(string Id, float Weight)>();
        if (wave % Raid == 0 && wave % Siege != 0)
        {
            mix.Add(("cavalry", 0.8f));
            mix.Add(("infantry", 0.2f));
        }
        else
        {
            mix.Add(("infantry", 1f));
            if (wave >= 3) mix.Add(("archer", 0.5f));
            if (wave >= 6) mix.Add(("heavy", 0.35f));
            if (wave >= 8) mix.Add(("crossbowman", 0.3f));
            if (wave >= 10) mix.Add(("cavalry", 0.25f));
        }
        mix.RemoveAll(m => !units.Contains(m.Id));
        while (troops.Count < cap)
        {
            var affordable = mix.Where(m => units.Get(m.Id).Cost <= left).ToList();
            if (affordable.Count == 0)
                break;
            float roll = (float)d.Rng.NextDouble() * affordable.Sum(m => m.Weight);
            var pick = affordable[^1].Id;
            foreach (var (id, weight) in affordable)
            {
                roll -= weight;
                if (roll <= 0f)
                {
                    pick = id;
                    break;
                }
            }
            Take(pick);
        }
        if (troops.Count == 0)
            Take(mix.Count > 0 ? mix[0].Id : units.All.First().Id);
        UpgradeTroops(troops, left);
        return troops;
    }

    /// <summary>What a horde soldier becomes when a wave at its cap has gold to spare: spearmen into men-at-arms, bowmen into crossbowmen.</summary>
    private static readonly (string From, string To)[] Upgrades = { ("infantry", "heavy"), ("archer", "crossbowman") };

    /// <summary>
    /// A wave that hit the cap on its numbers spends what is left on better troops instead: the cheap soldiers are
    /// turned into tougher ones, front to back, while the gold lasts.
    /// </summary>
    private void UpgradeTroops(List<UnitType> troops, int left)
    {
        var units = Rules.Units;
        foreach (var (from, to) in Upgrades)
        {
            if (!units.Contains(from) || !units.Contains(to))
                continue;
            int extra = units.Get(to).Cost - units.Get(from).Cost;
            if (extra <= 0)
                continue;
            for (int i = 0; i < troops.Count && left >= extra; i++)
            {
                if (troops[i].Id != from)
                    continue;
                troops[i] = units.Get(to);
                left -= extra;
            }
        }
    }

    private Vector2 CitadelPosition(DefenseState d) => GetBuilding(d.CitadelId)?.Position ?? new Vector2(Map.Width / 2f, Map.Height / 2f);

    private Vector2? NearestPlayerMine(DefenseState d, Vector2 from)
    {
        Vector2? best = null;
        float bestDistance = float.MaxValue;
        foreach (var b in _buildings)
        {
            if (!b.Type.IsMine || !d.IsPlayer(b.FactionId))
                continue;
            float distance = Vector2.DistanceSquared(b.Position, from);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = b.Position;
            }
        }
        return best;
    }

    /// <summary>Horde troops left with nothing to do (a raid done, a target gone) march on the citadel again.</summary>
    private void SendIdleHordeOn(DefenseState d)
    {
        List<Unit>? idle = null;
        foreach (var unit in _units)
        {
            if (unit.FactionId == d.HordeFactionId && unit.IsAlive && unit.Order == UnitOrder.None && unit.Target == null)
                (idle ??= new List<Unit>()).Add(unit);
        }
        if (idle == null)
            return;
        var target = CitadelPosition(d);
        foreach (var group in idle.GroupBy(u => Map.ComponentOf(Cell.FromPosition(u.Position))).OrderBy(g => g.Key))
            OrderGroupMove(group.ToList(), group.Key, target, UnitOrder.AttackMove);
    }

    /// <summary>A horde unit fell: when its wave is wiped out, every player is paid for it.</summary>
    private void OnHordeUnitGone(Unit unit)
    {
        if (Defense is not { } d || !d.WaveOfUnit.Remove(unit.Id, out int wave))
            return;
        if (--d.AliveInWave[wave] > 0)
            return;
        d.AliveInWave.Remove(wave);
        d.WavesCleared++;
        int reward = Rules.Defense.RewardBase + Rules.Defense.RewardPerWave * wave;
        foreach (var faction in _factions)
        {
            if (d.IsPlayer(faction.Id))
                faction.Treasury.Add(reward);
        }
        _events.Add(new WaveCleared(wave, reward));
    }

    /// <summary>Defense ends when the citadel falls; nobody wins (the score is the waves held).</summary>
    private void CheckCitadel(DefenseState d)
    {
        if (IsOver || GetBuilding(d.CitadelId) != null)
            return;
        IsOver = true;
        WinningTeam = -1;
        _events.Add(new MatchEnded(WinningTeam));
    }

    private void HashDefense(ref Fnv hash)
    {
        if (Defense is not { } d)
            return;
        hash.Add(d.WavesLaunched);
        hash.Add(d.WavesCleared);
        hash.Add(d.NextWaveTick);
        foreach (var p in d.Incoming)
        {
            hash.Add(BitConverter.SingleToInt32Bits(p.X));
            hash.Add(BitConverter.SingleToInt32Bits(p.Y));
        }
        foreach (var (wave, alive) in d.AliveInWave)
        {
            hash.Add(wave);
            hash.Add(alive);
        }
    }
}

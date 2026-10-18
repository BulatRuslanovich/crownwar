using System.Numerics;
using Warcrown.Sim.Buildings;
using Warcrown.Sim.Core;
using Warcrown.Sim.Units;
using Warcrown.Sim.World;

namespace Warcrown.Sim.AI;

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
public sealed partial class BotBrain
{
    /// <summary>Enemy units this close (tiles) to one of our buildings count as an attack on us.</summary>
    private const float ThreatRadius = 9f;
    /// <summary>Units further than this from where they were sent get the order again.</summary>
    private const float RegroupDistance = 4f;
    /// <summary>Enemy units this close to a target building are counted as its defenders.</summary>
    private const float DefenceRadius = 8f;
    /// <summary>Worth credited to a tower when judging how well a target is defended.</summary>
    private const int TowerDefenceValue = 250;
    private const int SoldiersPerRam = 5;
    /// <summary>No siege engines for a smaller army: they are useless in the field and only needed to break walls.</summary>
    private const int SiegeFromArmy = 10;
    private const int MonkEvery = 8;
    private const int TowersPerMine = 1;
    /// <summary>An army worth less than this share of the strongest enemy one counts as outmatched.</summary>
    private const float BehindShare = 0.8f;

    private readonly Simulation _sim;
    private readonly BotProfile _profile;
    private readonly Random _rng;
    private readonly int _thinkTicks;
    private readonly int _thinkOffset;

    private Vector2? _armyGoal;
    private int _attackTargetId;
    private int _launchValue;
    private GoldDeposit? _expansion;
    private int _lastMineTick = int.MinValue / 2;
    private int _lastBarracksTick = int.MinValue / 2;
    /// <summary>Beyond what the army limit needs, a new barracks (to open the next kind of unit) at most this often.</summary>
    private const float BarracksIntervalSeconds = 60f;
    private UnitType? _nextUnit;
    /// <summary>The bot's own buildings, taken once per decision (orders only take effect when the simulation steps).</summary>
    private readonly List<Building> _own = new();
    /// <summary>Scratch lists for <see cref="TryPlaceNear"/>.</summary>
    private readonly List<Cell> _ring = new();
    private readonly List<(int Key, int Index)> _ringOrder = new();

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
        _thinkTicks = Ticks.AtLeastOne(profile.ThinkInterval);
        // Spread bots over different ticks so they do not all think at once.
        _thinkOffset = (factionId * 7) % _thinkTicks;
    }

    public int FactionId { get; }

    /// <summary>Where commands go: the simulation unless set (e.g. a networked bot sends them through lockstep).</summary>
    public Action<Command>? CommandSink { get; set; }
    public BotMode Mode { get; private set; } = BotMode.Gather;

    private void Issue(Command command)
    {
        if (CommandSink != null)
            CommandSink(command);
        else
            _sim.Issue(command);
    }

    public void Update()
    {
        if (_sim.Tick % _thinkTicks == _thinkOffset)
            Think();
    }

    /// <summary>One decision round: economy first, then what the army does.</summary>
    public void Think()
    {
        if (_sim.GetFaction(FactionId) is not { IsEliminated: false } me)
            return;
        if (_sim.GetBuilding(me.CapitalId) is not { } capital)
        {
            PlayWithoutOutpost(me); // Defense: the outpost fell and may be built again
            return;
        }
        _home = capital.Position;

        // Bots do not do diplomacy: any offer of peace is turned down at once.
        foreach (var faction in _sim.Factions)
            if (_sim.HasPeaceOffer(faction.Id, FactionId))
                Issue(new AnswerPeaceCommand(FactionId, faction.Id, Accept: false));

        _own.Clear();
        foreach (var building in _sim.Buildings)
        {
            if (building.FactionId == FactionId)
                _own.Add(building);
        }
        var army = _sim.Units.Where(u => u.FactionId == FactionId).ToList();
        int armyValue = army.Sum(u => u.Type.Cost);
        var threats = FindThreats();
        AddCitadelThreats(threats);

        // Outmatched: troops first; towers and upgrades wait until the army has caught up.
        bool behind = armyValue < StrongestEnemyArmy() * BehindShare;
        ChooseDoctrine(me);
        int reserve = PlanConstruction(me, capital, army, threatened: threats.Count > 0, behind);
        if (threats.Count == 0 && !behind)
            reserve += PlanUpgrades(me, reserve, armyValue);
        Recruit(me, capital, reserve, urgent: threats.Count > 0);
        CommandArmy(capital, army, armyValue, threats);
    }

    private List<Building> OwnBuildings() => _own;

    /// <summary>Worth of the largest enemy army (bots see the whole map, as the player could by scouting).</summary>
    private int StrongestEnemyArmy()
    {
        var worth = new Dictionary<int, int>();
        foreach (var u in _sim.Units)
        {
            if (u.FactionId >= 0 && _sim.AreEnemies(FactionId, u.FactionId))
                worth[u.FactionId] = worth.GetValueOrDefault(u.FactionId) + u.Type.Cost;
        }
        return worth.Count == 0 ? 0 : worth.Values.Max();
    }

    private List<Unit> FindThreats()
    {
        var threats = new List<Unit>();
        foreach (var u in _sim.Units)
        {
            if (!_sim.AreEnemies(FactionId, u.FactionId) || !u.IsAlive)
                continue;
            foreach (var b in _own)
            {
                if (b.SurfaceDistance(u.Position) <= ThreatRadius)
                {
                    threats.Add(u);
                    break;
                }
            }
        }
        return threats;
    }

    /// <summary>No enemy units or buildings near the point.</summary>
    private bool IsSafe(Vector2 point)
    {
        foreach (var u in _sim.Units)
        {
            if (_sim.AreEnemies(FactionId, u.FactionId) && Vector2.Distance(u.Position, point) < DefenceRadius)
                return false;
        }
        foreach (var b in _sim.Buildings)
        {
            if (_sim.AreEnemies(FactionId, b.FactionId) && Vector2.Distance(b.Position, point) < DefenceRadius)
                return false;
        }
        return true;
    }

    private Vector2 RallyPoint(Building capital) => SpawnSide(capital) + new Vector2(0f, 1.5f);

    private static Vector2 SpawnSide(Building capital) => capital.Position + new Vector2(0f, capital.Type.Size / 2f + 1f);

    /// <summary>Walkable region next to a point (deposit centers are on open ground, but may be built on).</summary>
    private int NearbyComponent(Vector2 point) =>
        _sim.Map.FindNearestInComponent(Cell.FromPosition(point), TileGrid.AnyComponent, 4) is { } cell
            ? _sim.Map.ComponentOf(cell)
            : -1;
}

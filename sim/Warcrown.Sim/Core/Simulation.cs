using System.Numerics;
using Warcrown.Sim.Buildings;
using Warcrown.Sim.Combat;
using Warcrown.Sim.Units;
using Warcrown.Sim.World;

namespace Warcrown.Sim.Core;

/// <summary>
/// Authoritative game state advanced in fixed ticks. Knows nothing about rendering or input.
/// Players and bots change it only through <see cref="Issue"/>.
/// The class is split by topic: this file holds the state, the tick loop and command dispatch;
/// Simulation.Movement (marching and stuck handling), .DayNight, .Buildings (placement, construction, upgrades),
/// .Training, .Economy, .Destruction (loot, demolition, elimination, victory), .Plans (distant construction),
/// .Diplomacy and .Checksum hold the rest.
/// </summary>
public sealed partial class Simulation
{
    public const int TicksPerSecond = 20;
    public const float TickDuration = 1f / TicksPerSecond;

    private readonly List<Unit> _units = new();
    private readonly List<Building> _buildings = new();
    private readonly Dictionary<int, Entity> _entitiesById = new();
    private readonly List<Faction> _factions = new();
    private readonly List<GoldDeposit> _deposits = new();
    private readonly List<Projectile> _projectiles = new();
    private readonly Queue<Command> _pending = new();
    private readonly Pathfinder _pathfinder;
    private readonly CombatSystem _combat;
    private List<SimEvent> _events = new();
    private int _nextEntityId = 1;
    /// <summary>Team per faction id, for the hot enemy checks in combat.</summary>
    private readonly int[] _teamOf;

    public Simulation(MapDefinition map, GameRules rules, MatchSetup setup)
    {
        Map = map.Grid;
        Rules = rules;
        _pathfinder = new Pathfinder(Map);
        _combat = new CombatSystem(Map, _pathfinder, _projectiles, e => _events.Add(e), AreEnemies);

        int regenTicks = Ticks.FromSeconds(rules.DepositRegenSeconds);
        foreach (var deposit in map.Deposits)
        {
            int capacity = (int)MathF.Round(deposit.Yield * rules.DepositReserveSeconds);
            _deposits.Add(new GoldDeposit(_deposits.Count + 1, deposit.Anchor, deposit.Yield, capacity, regenTicks));
            Map.ClearWoods(deposit.Anchor, GoldDeposit.Size); // the ore lies in the open, not under trees
        }

        var factionIds = Enumerable.Range(0, setup.FactionCount)
            .Concat(setup.Mode == MatchMode.Defense ? new[] { setup.GarrisonFactionId, setup.HordeFactionId } : Array.Empty<int>())
            .Concat(map.UnitSpawns.Select(u => u.FactionId))
            .Where(id => id >= 0).Distinct().Order().ToList();
        _teamOf = new int[factionIds.Count == 0 ? 0 : factionIds[^1] + 1];
        foreach (int id in factionIds)
        {
            int team = setup.TeamOf(id);
            _factions.Add(new Faction(id, team, rules));
            _teamOf[id] = team;
        }
        InitDiplomacy(_teamOf.Length);

        PlaceCapitals(map.StartPositions, setup);
        if (setup.Mode == MatchMode.Defense)
            SetUpDefense(map, setup);
        PlaceForts(map.FortAnchors);

        foreach (var spawn in map.UnitSpawns)
        {
            var type = Rules.Units.Get(spawn.TypeId);
            foreach (var slot in Formation.ComputeSlots(Map, spawn.Cell.Center, spawn.Count, type.Radius))
                SpawnUnit(spawn.TypeId, spawn.FactionId, slot);
        }
        UpdateEconomy(payIncome: false);
    }

    public TileGrid Map { get; }
    public GameRules Rules { get; }
    public int Tick { get; private set; }
    public IReadOnlyList<Unit> Units => _units;
    public IReadOnlyList<Building> Buildings => _buildings;
    public IReadOnlyList<Faction> Factions => _factions;
    public IReadOnlyList<GoldDeposit> Deposits => _deposits;
    public IReadOnlyList<Projectile> Projectiles => _projectiles;

    /// <summary>Set once a single contender (or nobody) is left; the world no longer changes.</summary>
    public bool IsOver { get; private set; }

    /// <summary>The winning team, or -1 when the match ended without survivors or is still running.</summary>
    public int WinningTeam { get; private set; } = -1;

    /// <summary>
    /// Whether units of the two factions fight each other: different factions on different teams, not at peace
    /// (see <see cref="StanceBetween"/>). Neutral (-1) is hostile to everyone.
    /// </summary>
    public bool AreEnemies(int factionA, int factionB) => StanceBetween(factionA, factionB) == Stance.War;

    public Entity? GetEntity(int id) => _entitiesById.GetValueOrDefault(id);
    public Unit? GetUnit(int id) => GetEntity(id) as Unit;
    public Building? GetBuilding(int id) => GetEntity(id) as Building;
    public Faction? GetFaction(int id)
    {
        foreach (var faction in _factions)
        {
            if (faction.Id == id)
                return faction;
        }
        return null;
    }

    public void Issue(Command command) => _pending.Enqueue(command);

    /// <summary>Events produced since the last call, oldest first.</summary>
    public IReadOnlyList<SimEvent> TakeEvents()
    {
        if (_events.Count == 0)
            return Array.Empty<SimEvent>();
        var taken = _events;
        _events = new List<SimEvent>();
        return taken;
    }

    public void Step()
    {
        if (IsOver)
            return;
        Tick++;
        foreach (var unit in _units)
            unit.PreviousPosition = unit.Position;

        while (_pending.TryDequeue(out var command))
            Apply(command);
        UpdateDiplomacy();

        UpdateDeposits();
        UpdateEconomy(payIncome: true);
        AdvanceConstructionAndTraining();
        UpdateDefense();

        foreach (var unit in _units)
            _combat.UpdateBehaviour(unit, _units, _buildings, Tick);
        _combat.ResumeMarches();
        foreach (var building in _buildings)
            _combat.UpdateTower(building, _units, Tick, AttackRangeOf(building));
        foreach (var unit in _units)
            unit.Move(TickDuration);
        _combat.AdvanceProjectiles(TickDuration, _units);

        UnitCollisions.Resolve(_units);
        foreach (var unit in _units)
        {
            KeepOnWalkableGround(unit);
            CheckProgress(unit);
            CompleteFinishedOrder(unit);
        }
        UpdatePlans();

        RemoveDeadUnits();
        RemoveDestroyedBuildings();
        CheckEliminations();
    }

    /// <summary>A unit of the kind as its faction trains it (its doctrine applied).</summary>
    public Unit SpawnUnit(string typeId, int factionId, Vector2 position) => SpawnUnit(UnitTypeFor(factionId, typeId), factionId, position);

    private Unit SpawnUnit(UnitType type, int factionId, Vector2 position)
    {
        if (!Map.IsWalkable(Cell.FromPosition(position)))
            throw new ArgumentException($"Cannot spawn a unit on a blocked tile at {position}.");
        var unit = new Unit(_nextEntityId++, type, factionId, position);
        _units.Add(unit);
        _entitiesById.Add(unit.Id, unit);
        return unit;
    }

    private void Apply(Command command)
    {
        if (GetFaction(command.FactionId) is { IsEliminated: true })
            return; // the eliminated no longer give orders
        if (Defense != null && command is ProposePeaceCommand or AnswerPeaceCommand or DeclareWarCommand)
        {
            // One team against the horde: there is nobody to make peace or war with.
            int other = command switch
            {
                ProposePeaceCommand o => o.OtherFactionId,
                AnswerPeaceCommand a => a.OtherFactionId,
                DeclareWarCommand w => w.OtherFactionId,
                _ => -1,
            };
            _events.Add(new DiplomacyRejected(command.FactionId, other,
                AreEnemies(command.FactionId, other) ? DiplomacyError.UnknownFaction : DiplomacyError.Allies));
            return;
        }
        switch (command)
        {
            case MoveCommand move:
                ApplyMove(move);
                break;
            case AttackCommand attack:
                ApplyAttack(attack);
                break;
            case RecruitCommand recruit:
                ApplyRecruit(recruit);
                break;
            case BuildCommand build:
                ApplyBuild(build);
                break;
            case UpgradeCommand upgrade:
                ApplyUpgrade(upgrade);
                break;
            case DemolishCommand demolish:
                ApplyDemolish(demolish);
                break;
            case SetRallyCommand rally:
                ApplyRally(rally);
                break;
            case ProposePeaceCommand offer:
                ApplyProposePeace(offer);
                break;
            case AnswerPeaceCommand answer:
                ApplyAnswerPeace(answer);
                break;
            case DeclareWarCommand war:
                ApplyDeclareWar(war);
                break;
            case ChooseDoctrineCommand doctrine:
                ApplyChooseDoctrine(doctrine);
                break;
            default:
                throw new NotSupportedException($"Unhandled command type {command.GetType().Name}.");
        }
    }

    private void RemoveDeadUnits()
    {
        for (int i = _units.Count - 1; i >= 0; i--)
        {
            var unit = _units[i];
            if (unit.IsAlive)
                continue;
            _units.RemoveAt(i);
            _entitiesById.Remove(unit.Id);
            _events.Add(new UnitDied(unit.FactionId, unit.Id, unit.KilledByFactionId, unit.Position, unit.Type.Id));
            RewardKiller(unit);
            OnHordeUnitGone(unit);
        }
    }

    /// <summary>The unit that struck <paramref name="fallen"/> down, if it still stands, gains experience worth what the fallen cost.</summary>
    private void RewardKiller(Unit fallen)
    {
        if (fallen.KilledById < 0 || GetUnit(fallen.KilledById) is not { IsAlive: true } killer
            || !AreEnemies(killer.FactionId, fallen.FactionId) || Rules.Veterancy.Ranks.Count == 0)
            return;
        if (killer.GainExperience(fallen.Type.Cost, Rules.Veterancy))
            _events.Add(new UnitPromoted(killer.FactionId, killer.Id, killer.Rank, killer.Position));
    }
}

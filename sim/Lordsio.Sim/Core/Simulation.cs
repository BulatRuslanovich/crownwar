using System.Numerics;
using Lordsio.Sim.Buildings;
using Lordsio.Sim.Combat;
using Lordsio.Sim.Units;
using Lordsio.Sim.World;

namespace Lordsio.Sim.Core;

/// <summary>
/// Authoritative game state advanced in fixed ticks. Knows nothing about rendering or input.
/// Players and bots change it only through <see cref="Issue"/>.
/// This file holds the tick loop, commands and unit movement; buildings and the economy live in
/// Simulation.Buildings.cs.
/// </summary>
public sealed partial class Simulation
{
    public const int TicksPerSecond = 20;
    public const float TickDuration = 1f / TicksPerSecond;

    /// <summary>How far from a blocked click we look for a reachable replacement point.</summary>
    private const int MaxGoalSearchRadius = 64;

    // Stuck handling: every second a marching unit must cover a fraction of its speed.
    private const int ProgressCheckTicks = TicksPerSecond;
    private const float MinProgressFraction = 0.25f;
    private const int RepathAfterChecks = 2;
    private const int GiveUpAfterChecks = 5;
    /// <summary>A unit stuck this close to its slot (crowded by neighbours) simply counts as arrived.</summary>
    private const float ArrivalSlack = 1.5f;

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

    public Simulation(MapDefinition map, GameRules rules, MatchSetup setup)
    {
        Map = map.Grid;
        Rules = rules;
        _pathfinder = new Pathfinder(Map);
        _combat = new CombatSystem(Map, _pathfinder, _projectiles, e => _events.Add(e));

        foreach (var deposit in map.Deposits)
            _deposits.Add(new GoldDeposit(_deposits.Count + 1, deposit.Anchor, deposit.Yield));

        var factionIds = Enumerable.Range(0, setup.FactionCount)
            .Concat(map.UnitSpawns.Select(u => u.FactionId))
            .Where(id => id >= 0).Distinct().Order();
        foreach (int id in factionIds)
            _factions.Add(new Faction(id, rules.StartingGold));

        PlaceCapitals(map.StartPositions, setup);

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

    /// <summary>The last faction standing, or -1 when the match ended without survivors or is still running.</summary>
    public int WinnerId { get; private set; } = -1;

    public Entity? GetEntity(int id) => _entitiesById.GetValueOrDefault(id);
    public Unit? GetUnit(int id) => GetEntity(id) as Unit;
    public Building? GetBuilding(int id) => GetEntity(id) as Building;
    public Faction? GetFaction(int id) => _factions.Find(f => f.Id == id);

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

        UpdateEconomy(payIncome: true);
        AdvanceConstructionAndTraining();

        foreach (var unit in _units)
            _combat.UpdateBehaviour(unit, _units, _buildings, Tick);
        foreach (var building in _buildings)
            _combat.UpdateTower(building, _units, Tick);
        foreach (var unit in _units)
            unit.Move(TickDuration);
        _combat.AdvanceProjectiles(TickDuration);

        UnitCollisions.Resolve(_units);
        foreach (var unit in _units)
        {
            KeepOnWalkableGround(unit);
            CheckProgress(unit);
            CompleteFinishedOrder(unit);
        }

        RemoveDeadUnits();
        RemoveDestroyedBuildings();
        CheckEliminations();
    }

    public Unit SpawnUnit(string typeId, int factionId, Vector2 position)
    {
        if (!Map.IsWalkable(Cell.FromPosition(position)))
            throw new ArgumentException($"Cannot spawn a unit on a blocked tile at {position}.");
        var unit = new Unit(_nextEntityId++, Rules.Units.Get(typeId), factionId, position);
        _units.Add(unit);
        _entitiesById.Add(unit.Id, unit);
        return unit;
    }

    private void Apply(Command command)
    {
        if (GetFaction(command.FactionId) is { IsEliminated: true })
            return; // the eliminated no longer give orders
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
            default:
                throw new NotSupportedException($"Unhandled command type {command.GetType().Name}.");
        }
    }

    /// <summary>
    /// Units that died or belong to someone else are silently skipped: the issuer's view of the world
    /// may be a tick out of date, and foreign units are not theirs to command.
    /// </summary>
    private IEnumerable<Unit> OwnLivingUnits(int factionId, IReadOnlyList<int> unitIds)
    {
        foreach (int id in unitIds.Distinct())
        {
            if (GetUnit(id) is { IsAlive: true } unit && unit.FactionId == factionId)
                yield return unit;
        }
    }

    private void ApplyMove(MoveCommand move)
    {
        // Units are grouped by walkable region, since each region resolves a blocked target differently.
        var groups = new SortedDictionary<int, List<Unit>>();
        foreach (var unit in OwnLivingUnits(move.FactionId, move.UnitIds))
        {
            int component = Map.ComponentOf(Cell.FromPosition(unit.Position));
            if (!groups.TryGetValue(component, out var list))
                groups[component] = list = new List<Unit>();
            list.Add(unit);
        }

        var order = move.Mode == MoveMode.AttackMove ? UnitOrder.AttackMove : UnitOrder.Move;
        foreach (var (component, group) in groups)
            OrderGroupMove(group, component, move.Target, order);
    }

    private void ApplyAttack(AttackCommand attack)
    {
        if (GetEntity(attack.TargetId) is not { IsAlive: true } target || target.FactionId == attack.FactionId)
            return;
        foreach (var unit in OwnLivingUnits(attack.FactionId, attack.UnitIds))
        {
            unit.StartAttack(target);
            unit.LastOrder = MoveOrderResult.Accepted;
        }
    }

    private void OrderGroupMove(List<Unit> group, int component, Vector2 target, UnitOrder order)
    {
        Cell requested = Cell.FromPosition(target);
        Cell? goal = Map.FindNearestInComponent(requested, component, MaxGoalSearchRadius);
        if (goal == null)
        {
            foreach (var unit in group)
            {
                unit.CompleteOrder();
                unit.LastOrder = MoveOrderResult.Unreachable;
            }
            return;
        }

        bool adjusted = goal.Value != requested;
        Vector2 center = adjusted ? goal.Value.Center : target;
        float clearance = group.Max(u => u.Type.Radius);
        var slots = Formation.ComputeSlots(Map, center, group.Count, clearance);

        // Closest units take the central slots; the rest fill the outer rings.
        group.Sort((a, b) =>
        {
            int byDistance = Vector2.DistanceSquared(a.Position, center).CompareTo(Vector2.DistanceSquared(b.Position, center));
            return byDistance != 0 ? byDistance : a.Id.CompareTo(b.Id);
        });

        for (int i = 0; i < group.Count; i++)
        {
            var unit = group[i];
            var path = _pathfinder.FindPath(unit.Position, slots[i], unit.Type.Radius);
            if (path == null)
            {
                unit.CompleteOrder();
                unit.LastOrder = MoveOrderResult.Unreachable;
                continue;
            }
            unit.StartMove(order, slots[i], path);
            unit.LastOrder = adjusted ? MoveOrderResult.Adjusted : MoveOrderResult.Accepted;
        }
    }

    /// <summary>Undoes pushes into walls, buildings, the map edge or another walkable region.</summary>
    private void KeepOnWalkableGround(Unit unit)
    {
        Vector2 pos = Map.PushOutOfWalls(unit.Position, unit.Type.Radius);
        var cell = Cell.FromPosition(pos);
        var previousCell = Cell.FromPosition(unit.PreviousPosition);
        if (!Map.IsWalkable(cell) || Map.ComponentOf(cell) != Map.ComponentOf(previousCell))
            pos = unit.PreviousPosition;
        unit.Position = pos;
    }

    /// <summary>A march whose path has run out (and that is not busy fighting) is done.</summary>
    private static void CompleteFinishedOrder(Unit unit)
    {
        if (unit.State != UnitState.Idle || unit.Target != null)
            return;
        if (unit.Order == UnitOrder.Move)
            unit.CompleteOrder();
        else if (unit.Order == UnitOrder.AttackMove && unit.Destination is { } d
                 && Vector2.Distance(unit.Position, d) <= ArrivalSlack)
            unit.CompleteOrder();
        // An attack-move that ended a chase away from its slot resumes next tick.
    }

    private void CheckProgress(Unit unit)
    {
        if (unit.State != UnitState.Moving || unit.Target != null || unit.Destination is not { } destination)
            return;
        if (++unit.TicksSinceProgressCheck < ProgressCheckTicks)
            return;

        float moved = Vector2.Distance(unit.Position, unit.ProgressAnchor);
        float expected = unit.Type.Speed * ProgressCheckTicks * TickDuration;
        unit.StuckChecks = moved < expected * MinProgressFraction ? unit.StuckChecks + 1 : 0;
        unit.TicksSinceProgressCheck = 0;
        unit.ProgressAnchor = unit.Position;
        if (unit.StuckChecks == 0)
            return;

        bool nearSlot = Vector2.Distance(unit.Position, destination) <= ArrivalSlack;
        if (unit.StuckChecks >= RepathAfterChecks && nearSlot)
        {
            unit.CompleteOrder(); // close enough: the slot is crowded, stand where we are
            return;
        }
        if (unit.StuckChecks >= GiveUpAfterChecks)
        {
            unit.CompleteOrder();
            unit.LastOrder = MoveOrderResult.Blocked;
            return;
        }
        if (unit.StuckChecks == RepathAfterChecks)
            Repath(unit, destination);
    }

    private void Repath(Unit unit, Vector2 destination)
    {
        // The slot itself may now be under a new building: aim for the nearest free spot instead.
        int component = Map.ComponentOf(Cell.FromPosition(unit.Position));
        if (Map.FindNearestInComponent(Cell.FromPosition(destination), component, MaxGoalSearchRadius) is not { } goal)
        {
            unit.CompleteOrder();
            unit.LastOrder = MoveOrderResult.Blocked;
            return;
        }
        var target = goal == Cell.FromPosition(destination) ? destination : goal.Center;
        var path = _pathfinder.FindPath(unit.Position, target, unit.Type.Radius);
        if (path == null)
        {
            unit.CompleteOrder();
            unit.LastOrder = MoveOrderResult.Blocked;
            return;
        }
        unit.ReplacePath(path);
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
            _events.Add(new UnitDied(unit.FactionId, unit.Id, unit.KilledByFactionId, unit.Position));
        }
    }
}

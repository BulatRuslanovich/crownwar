using System.Numerics;
using Lordsio.Sim.Units;
using Lordsio.Sim.World;

namespace Lordsio.Sim.Core;

/// <summary>
/// Authoritative game state advanced in fixed ticks. Knows nothing about rendering or input.
/// </summary>
public sealed class Simulation
{
    public const int TicksPerSecond = 20;
    public const float TickDuration = 1f / TicksPerSecond;

    /// <summary>How far from a blocked click we look for a reachable replacement point.</summary>
    private const int MaxGoalSearchRadius = 64;

    private readonly List<Unit> _units = new();
    private readonly Dictionary<int, Unit> _unitsById = new();
    private readonly Queue<Command> _pending = new();
    private readonly Pathfinder _pathfinder;
    private int _nextUnitId = 1;

    public Simulation(MapDefinition map, UnitCatalog catalog)
    {
        Map = map.Grid;
        Catalog = catalog;
        _pathfinder = new Pathfinder(Map);
        foreach (var spawn in map.UnitSpawns)
            SpawnUnit(spawn.TypeId, spawn.FactionId, spawn.Cell.Center);
    }

    public TileGrid Map { get; }
    public UnitCatalog Catalog { get; }
    public int Tick { get; private set; }
    public IReadOnlyList<Unit> Units => _units;

    public Unit? GetUnit(int id) => _unitsById.GetValueOrDefault(id);

    public void Issue(Command command) => _pending.Enqueue(command);

    public void Step()
    {
        Tick++;
        foreach (var unit in _units)
            unit.PreviousPosition = unit.Position;

        while (_pending.TryDequeue(out var command))
            Apply(command);

        foreach (var unit in _units)
            unit.Move(TickDuration);
    }

    public Unit SpawnUnit(string typeId, int factionId, Vector2 position)
    {
        if (!Map.IsWalkable(Cell.FromPosition(position)))
            throw new ArgumentException($"Cannot spawn a unit on a blocked tile at {position}.");
        var unit = new Unit(_nextUnitId++, Catalog.Get(typeId), factionId, position);
        _units.Add(unit);
        _unitsById.Add(unit.Id, unit);
        return unit;
    }

    private void Apply(Command command)
    {
        switch (command)
        {
            case MoveCommand move:
                ApplyMove(move);
                break;
            default:
                throw new NotSupportedException($"Unhandled command type {command.GetType().Name}.");
        }
    }

    private void ApplyMove(MoveCommand move)
    {
        foreach (int id in move.UnitIds)
        {
            // Units that died or belong to someone else are silently skipped: the issuer's
            // view of the world may be a tick out of date, and foreign units are not theirs to command.
            if (GetUnit(id) is not { } unit || unit.FactionId != move.FactionId)
                continue;
            OrderMove(unit, move.Target);
        }
    }

    private void OrderMove(Unit unit, Vector2 target)
    {
        Cell from = Cell.FromPosition(unit.Position);
        Cell requested = Cell.FromPosition(target);
        int component = Map.ComponentOf(from);
        Cell? goal = Map.FindNearestInComponent(requested, component, MaxGoalSearchRadius);
        if (goal == null)
        {
            unit.ClearPath();
            unit.LastOrder = MoveOrderResult.Unreachable;
            return;
        }

        bool adjusted = goal.Value != requested;
        Vector2 destination = adjusted ? goal.Value.Center : target;
        var path = _pathfinder.FindPath(unit.Position, destination, unit.Type.Radius);
        if (path == null)
        {
            unit.ClearPath();
            unit.LastOrder = MoveOrderResult.Unreachable;
            return;
        }

        unit.SetPath(path);
        unit.LastOrder = adjusted ? MoveOrderResult.Adjusted : MoveOrderResult.Accepted;
    }
}

using System.Numerics;
using Lordsio.Sim.World;
using Lordsio.Sim.Core;

namespace Lordsio.Sim.Units;

public enum UnitState
{
    Idle,
    Moving,
    Attacking,
}

/// <summary>What the owner told the unit to do. Without an order a unit guards its post.</summary>
public enum UnitOrder
{
    /// <summary>Guarding: fights enemies that come into sight, but does not chase far from its post.</summary>
    None,
    /// <summary>Walk to the destination ignoring enemies (e.g. a retreat).</summary>
    Move,
    /// <summary>Walk to the destination, fighting any enemies met on the way.</summary>
    AttackMove,
    /// <summary>Hunt one specific enemy unit until it dies.</summary>
    AttackUnit,
}

/// <summary>Outcome of the most recent order, kept for UI feedback.</summary>
public enum MoveOrderResult
{
    None,
    Accepted,
    /// <summary>The requested point was blocked or unreachable; the unit goes to the nearest reachable point.</summary>
    Adjusted,
    Unreachable,
    /// <summary>The unit made no progress for too long (e.g. a jammed crossing) and stopped short of its slot.</summary>
    Blocked,
}

public sealed class Unit : Entity
{
    /// <summary>Intermediate waypoints count as reached within this distance, so a unit jostled
    /// by its neighbours does not circle back to hit a corner point exactly.</summary>
    private const float WaypointTolerance = 0.35f;

    private readonly List<Vector2> _path = new();

    internal Unit(int id, UnitType type, int factionId, Vector2 position)
        : base(id, factionId, position, type.MaxHealth)
    {
        Type = type;
        PreviousPosition = position;
        GuardPoint = position;
    }

    /// <summary>What kind of unit this is (its stats come from the rules).</summary>
    public UnitType Type { get; }
    public override int MaxHealth => Type.MaxHealth;
    public override float RangedArmor => Type.RangedArmor;

    public override float SurfaceDistance(Vector2 point) =>
        MathF.Max(0f, Vector2.Distance(Position, point) - Type.Radius);

    /// <summary>Position at the start of the last tick; views interpolate between the two.</summary>
    public Vector2 PreviousPosition { get; internal set; }

    public UnitOrder Order { get; internal set; }
    public MoveOrderResult LastOrder { get; internal set; }

    /// <summary>Enemy unit or building being fought or chased.</summary>
    public Entity? Target { get; internal set; }

    /// <summary>Set by the simulation each tick while the unit stands and swings at its target.</summary>
    internal bool IsAttacking { get; set; }

    public UnitState State => IsAttacking ? UnitState.Attacking
        : PathIndex < _path.Count ? UnitState.Moving
        : UnitState.Idle;

    /// <summary>Final point of the current move order (the unit's formation slot).</summary>
    public Vector2? Destination { get; private set; }

    /// <summary>Where a guarding unit stands; it returns here after chasing an intruder.</summary>
    public Vector2 GuardPoint { get; private set; }

    /// <summary>Remaining waypoints, starting from <see cref="PathIndex"/>.</summary>
    public IReadOnlyList<Vector2> Path => _path;
    public int PathIndex { get; private set; }

    /// <summary>Ticks until the unit may strike again (views time the wind-up of a blow by it).</summary>
    public int AttackCooldownTicks { get; internal set; }
    internal int ChaseRepathTicks { get; set; }

    /// <summary>The target cell of the group order the unit marches under, whose route field it may reuse; null alone.</summary>
    internal Cell? MarchGoal { get; set; }

    /// <summary>Consecutive progress checks in which the unit barely moved.</summary>
    internal int StuckChecks { get; set; }
    internal int TicksSinceProgressCheck { get; set; }
    internal Vector2 ProgressAnchor { get; set; }

    public float RemainingPathLength
    {
        get
        {
            float total = 0f;
            Vector2 p = Position;
            for (int i = PathIndex; i < _path.Count; i++)
            {
                total += Vector2.Distance(p, _path[i]);
                p = _path[i];
            }
            return total;
        }
    }

    /// <summary>Starts a new order towards <paramref name="destination"/>.</summary>
    internal void StartMove(UnitOrder order, Vector2 destination, List<Vector2> waypoints)
    {
        Order = order;
        Target = null;
        Destination = destination;
        StuckChecks = 0;
        ReplacePath(waypoints);
    }

    /// <summary>Starts hunting one enemy; the route is planned by the chase logic.</summary>
    internal void StartAttack(Entity target)
    {
        Order = UnitOrder.AttackUnit;
        Target = target;
        Destination = null;
        ChaseRepathTicks = 0;
        ClearPath();
    }

    /// <summary>Swaps the current route (re-pathing or chasing), keeping the order and its stuck history.</summary>
    internal void ReplacePath(List<Vector2> waypoints)
    {
        _path.Clear();
        _path.AddRange(waypoints);
        PathIndex = 0;
        TicksSinceProgressCheck = 0;
        ProgressAnchor = Position;
    }

    /// <summary>Heads straight for a point (open-ground chase) without allocating a new path.</summary>
    internal void SetSingleWaypoint(Vector2 point)
    {
        _path.Clear();
        _path.Add(point);
        PathIndex = 0;
    }

    internal void ClearPath()
    {
        _path.Clear();
        PathIndex = 0;
    }

    /// <summary>Ends the current order: the unit stands still and guards where it is.</summary>
    internal void CompleteOrder()
    {
        ClearPath();
        Order = UnitOrder.None;
        Target = null;
        Destination = null;
        GuardPoint = Position;
    }

    /// <summary>Advances along the path by speed × dt.</summary>
    internal void Move(float dt)
    {
        if (PathIndex >= _path.Count)
            return;

        float budget = Type.Speed * dt;
        Vector2 pos = Position;
        while (budget > 0f && PathIndex < _path.Count)
        {
            Vector2 target = _path[PathIndex];
            float dist = Vector2.Distance(pos, target);
            bool last = PathIndex == _path.Count - 1;
            if (!last && dist <= WaypointTolerance)
            {
                PathIndex++;
                continue;
            }
            if (dist <= budget)
            {
                pos = target;
                budget -= dist;
                PathIndex++;
            }
            else
            {
                pos += (target - pos) / dist * budget;
                budget = 0f;
            }
        }
        Position = pos;
        if (PathIndex >= _path.Count)
            ClearPath();
    }
}

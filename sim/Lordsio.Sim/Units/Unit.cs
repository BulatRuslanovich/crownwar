using System.Numerics;

namespace Lordsio.Sim.Units;

public enum UnitState
{
    Idle,
    Moving,
}

/// <summary>Outcome of the most recent move order, kept for UI feedback.</summary>
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

public sealed class Unit
{
    /// <summary>Intermediate waypoints count as reached within this distance, so a unit jostled
    /// by its neighbours does not circle back to hit a corner point exactly.</summary>
    private const float WaypointTolerance = 0.35f;

    private readonly List<Vector2> _path = new();

    internal Unit(int id, UnitType type, int factionId, Vector2 position)
    {
        Id = id;
        Type = type;
        FactionId = factionId;
        Position = position;
        PreviousPosition = position;
    }

    public int Id { get; }
    public UnitType Type { get; }
    public int FactionId { get; }

    /// <summary>Position in tile units at the end of the last tick.</summary>
    public Vector2 Position { get; internal set; }

    /// <summary>Position at the start of the last tick; views interpolate between the two.</summary>
    public Vector2 PreviousPosition { get; internal set; }

    public UnitState State { get; private set; }
    public MoveOrderResult LastOrder { get; internal set; }

    /// <summary>Final point of the current move order (the unit's formation slot).</summary>
    public Vector2? Destination { get; private set; }

    /// <summary>Remaining waypoints, starting from <see cref="PathIndex"/>.</summary>
    public IReadOnlyList<Vector2> Path => _path;
    public int PathIndex { get; private set; }

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

    /// <summary>Starts a new move order.</summary>
    internal void StartMove(Vector2 destination, List<Vector2> waypoints)
    {
        Destination = destination;
        StuckChecks = 0;
        ReplacePath(waypoints);
    }

    /// <summary>Swaps the route of the current order (re-pathing), keeping its stuck history.</summary>
    internal void ReplacePath(List<Vector2> waypoints)
    {
        _path.Clear();
        _path.AddRange(waypoints);
        PathIndex = 0;
        TicksSinceProgressCheck = 0;
        ProgressAnchor = Position;
        State = _path.Count > 0 ? UnitState.Moving : UnitState.Idle;
        if (State == UnitState.Idle)
            Destination = null;
    }

    internal void Stop()
    {
        _path.Clear();
        PathIndex = 0;
        Destination = null;
        State = UnitState.Idle;
    }

    /// <summary>Advances along the path by speed × dt.</summary>
    internal void Move(float dt)
    {
        if (State != UnitState.Moving)
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
            Stop();
    }
}

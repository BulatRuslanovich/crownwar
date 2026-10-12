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
}

public sealed class Unit
{
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

    public UnitState State { get; internal set; }
    public MoveOrderResult LastOrder { get; internal set; }

    /// <summary>Remaining waypoints, starting from <see cref="PathIndex"/>.</summary>
    public IReadOnlyList<Vector2> Path => _path;
    public int PathIndex { get; private set; }

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

    internal void SetPath(List<Vector2> waypoints)
    {
        _path.Clear();
        _path.AddRange(waypoints);
        PathIndex = 0;
        State = _path.Count > 0 ? UnitState.Moving : UnitState.Idle;
    }

    internal void ClearPath()
    {
        _path.Clear();
        PathIndex = 0;
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

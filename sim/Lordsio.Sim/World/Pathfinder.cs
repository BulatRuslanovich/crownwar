using System.Numerics;

namespace Lordsio.Sim.World;

/// <summary>
/// A* over the tile grid: 8 directions, diagonal moves only when both orthogonal neighbours are
/// walkable (no squeezing between two blocked corners). Search buffers are reused between calls,
/// so one instance must not be used from several threads.
/// </summary>
public sealed class Pathfinder
{
    private const float DiagonalCost = 1.41421356f;

    private static readonly (int dx, int dy)[] Directions =
    {
        (1, 0), (-1, 0), (0, 1), (0, -1),
        (1, 1), (1, -1), (-1, 1), (-1, -1),
    };

    private readonly TileGrid _map;
    private readonly float[] _g;
    private readonly int[] _parent;
    private readonly int[] _seenStamp;
    private readonly int[] _closedStamp;
    private readonly PriorityQueue<int, float> _open = new();
    private int _stamp;

    public Pathfinder(TileGrid map)
    {
        _map = map;
        int n = map.Width * map.Height;
        _g = new float[n];
        _parent = new int[n];
        _seenStamp = new int[n];
        _closedStamp = new int[n];
    }

    /// <summary>Cell path from start to goal inclusive, or null when the goal cannot be reached.</summary>
    public List<Cell>? FindCellPath(Cell start, Cell goal)
    {
        if (!_map.AreConnected(start, goal))
            return null;
        if (start == goal)
            return new List<Cell> { start };

        NextStamp();
        _open.Clear();
        int startIndex = _map.Index(start.X, start.Y);
        int goalIndex = _map.Index(goal.X, goal.Y);
        _g[startIndex] = 0f;
        _parent[startIndex] = -1;
        _seenStamp[startIndex] = _stamp;
        _open.Enqueue(startIndex, Heuristic(start, goal));

        while (_open.TryDequeue(out int current, out _))
        {
            if (_closedStamp[current] == _stamp)
                continue; // stale duplicate entry
            if (current == goalIndex)
                return Reconstruct(goalIndex);
            _closedStamp[current] = _stamp;

            int cx = current % _map.Width, cy = current / _map.Width;
            foreach (var (dx, dy) in Directions)
            {
                int nx = cx + dx, ny = cy + dy;
                if (!_map.IsWalkable(nx, ny))
                    continue;
                bool diagonal = dx != 0 && dy != 0;
                if (diagonal && (!_map.IsWalkable(cx + dx, cy) || !_map.IsWalkable(cx, cy + dy)))
                    continue;

                int next = _map.Index(nx, ny);
                if (_closedStamp[next] == _stamp)
                    continue;
                float g = _g[current] + (diagonal ? DiagonalCost : 1f);
                if (_seenStamp[next] == _stamp && g >= _g[next])
                    continue;

                _seenStamp[next] = _stamp;
                _g[next] = g;
                _parent[next] = current;
                _open.Enqueue(next, g + Heuristic(new Cell(nx, ny), goal));
            }
        }
        return null; // unreachable despite matching components would be a bug, but fail safely
    }

    /// <summary>
    /// Waypoints from <paramref name="from"/> to <paramref name="to"/> (excluding the start point),
    /// with redundant intermediate cells removed where a body of <paramref name="radius"/> can walk straight.
    /// </summary>
    public List<Vector2>? FindPath(Vector2 from, Vector2 to, float radius)
    {
        var cells = FindCellPath(Cell.FromPosition(from), Cell.FromPosition(to));
        if (cells == null)
            return null;

        var waypoints = new List<Vector2>();
        Vector2 anchor = from;
        int i = 1;
        while (i < cells.Count)
        {
            int j = i;
            while (j + 1 < cells.Count && _map.IsCorridorClear(anchor, PointOf(j + 1), radius))
                j++;
            anchor = PointOf(j);
            waypoints.Add(anchor);
            i = j + 1;
        }

        // Same-cell move: the loop adds nothing, but we still want to reach the exact point.
        if (waypoints.Count == 0 && Vector2.DistanceSquared(from, to) > 1e-6f)
            waypoints.Add(to);
        return waypoints;

        // The last cell targets the exact requested point; intermediate cells target their centers.
        Vector2 PointOf(int index) => index == cells.Count - 1 ? to : cells[index].Center;
    }

    private static float Heuristic(Cell a, Cell b)
    {
        int dx = Math.Abs(a.X - b.X), dy = Math.Abs(a.Y - b.Y);
        return Math.Max(dx, dy) + (DiagonalCost - 1f) * Math.Min(dx, dy);
    }

    private List<Cell> Reconstruct(int goalIndex)
    {
        var path = new List<Cell>();
        for (int i = goalIndex; i != -1; i = _parent[i])
            path.Add(new Cell(i % _map.Width, i / _map.Width));
        path.Reverse();
        return path;
    }

    private void NextStamp()
    {
        if (++_stamp == int.MaxValue)
        {
            Array.Clear(_seenStamp);
            Array.Clear(_closedStamp);
            _stamp = 1;
        }
    }
}

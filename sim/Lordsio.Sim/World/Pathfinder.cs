using System.Numerics;

namespace Lordsio.Sim.World;

/// <summary>
/// A* over the tile grid: 8 directions, diagonal moves only when both orthogonal neighbours are
/// walkable (no squeezing between two blocked corners). Search buffers (and the waypoint list
/// <see cref="FindPath"/> returns) are reused between calls, so one instance must not be used from several threads.
/// </summary>
public sealed class Pathfinder
{
    internal const float DiagonalCost = 1.41421356f;

    internal static readonly (int dx, int dy)[] Directions =
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
    private readonly List<Cell> _cells = new();
    private readonly List<Cell> _tail = new();
    /// <summary>Route fields of recent group orders, most recently used first (see <see cref="FindPathViaField"/>).</summary>
    private readonly List<RouteField> _fields = new();
    private const int KeptFields = 4;
    private readonly List<Vector2> _waypoints = new();

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

    /// <summary>
    /// Waypoints like <see cref="FindPath"/> for a unit of a group sent to <paramref name="goal"/>: the shortest way
    /// towards it along the goal's <see cref="RouteField"/> (shared by the whole group, and kept for when they march on
    /// after a fight), then the few steps to <paramref name="to"/>, the unit's slot around the goal. Falls back to a search
    /// of its own when the field cannot lead there.
    /// </summary>
    public List<Vector2>? FindPathViaField(Cell goal, Vector2 from, Vector2 to, float radius)
    {
        var start = Cell.FromPosition(from);
        var slot = Cell.FromPosition(to);
        if (!_map.AreConnected(start, goal))
            return FindPath(from, to, radius);
        var field = FieldTowards(goal);
        int s = _map.Index(start.X, start.Y);
        if (!field.Reaches(s))
            return FindPath(from, to, radius);

        var cells = _cells;
        cells.Clear();
        for (int i = s; i != -1; i = field.Next(i))
        {
            var cell = new Cell(i % _map.Width, i / _map.Width);
            cells.Add(cell);
            if (cell == slot)
                return Smooth(cells, from, to, radius); // the slot lies on the way: stop there
        }
        if (slot != goal)
        {
            if (!SearchCells(goal, slot, _tail))
                return FindPath(from, to, radius);
            for (int i = 1; i < _tail.Count; i++)
                cells.Add(_tail[i]);
        }
        return Smooth(cells, from, to, radius);
    }

    /// <summary>The field towards <paramref name="goal"/>: a kept one if it is still valid, else the least recently used, started anew.</summary>
    private RouteField FieldTowards(Cell goal)
    {
        int found = _fields.FindIndex(f => f.Goal == goal && f.MapVersion == _map.Version);
        RouteField field;
        if (found >= 0)
        {
            field = _fields[found];
            _fields.RemoveAt(found);
        }
        else if (_fields.Count < KeptFields)
        {
            field = new RouteField(_map);
            field.Reset(goal);
        }
        else
        {
            field = _fields[^1];
            _fields.RemoveAt(_fields.Count - 1);
            field.Reset(goal);
        }
        _fields.Insert(0, field); // most recently used first
        return field;
    }

    /// <summary>Cell path from start to goal inclusive, or null when the goal cannot be reached.</summary>
    public List<Cell>? FindCellPath(Cell start, Cell goal)
    {
        var path = new List<Cell>();
        return SearchCells(start, goal, path) ? path : null;
    }

    /// <summary>Fills <paramref name="path"/> with the cells from start to goal inclusive; false when unreachable.</summary>
    private bool SearchCells(Cell start, Cell goal, List<Cell> path)
    {
        path.Clear();
        if (!_map.AreConnected(start, goal))
            return false;
        if (start == goal)
        {
            path.Add(start);
            return true;
        }

        NextStamp();
        _open.Clear();
        int startIndex = _map.Index(start.X, start.Y);
        int goalIndex = _map.Index(goal.X, goal.Y);
        _g[startIndex] = 0f;
        _parent[startIndex] = -1;
        _seenStamp[startIndex] = _stamp;
        _open.Enqueue(startIndex, Heuristic(start.X, start.Y, goal.X, goal.Y));
        int width = _map.Width, height = _map.Height;

        while (_open.TryDequeue(out int current, out _))
        {
            if (_closedStamp[current] == _stamp)
                continue; // stale duplicate entry
            if (current == goalIndex)
            {
                Reconstruct(goalIndex, path);
                return true;
            }
            _closedStamp[current] = _stamp;

            int cx = current % width, cy = current / width;
            float gCurrent = _g[current];
            foreach (var (dx, dy) in Directions)
            {
                int nx = cx + dx, ny = cy + dy;
                if ((uint)nx >= (uint)width || (uint)ny >= (uint)height)
                    continue;
                int next = current + dy * width + dx;
                if (!_map.IsWalkableAt(next))
                    continue;
                bool diagonal = dx != 0 && dy != 0;
                // Diagonal neighbours are in bounds here, so both side cells are too.
                if (diagonal && (!_map.IsWalkableAt(current + dx) || !_map.IsWalkableAt(current + dy * width)))
                    continue;

                if (_closedStamp[next] == _stamp)
                    continue;
                float g = gCurrent + (diagonal ? DiagonalCost : 1f);
                if (_seenStamp[next] == _stamp && g >= _g[next])
                    continue;

                _seenStamp[next] = _stamp;
                _g[next] = g;
                _parent[next] = current;
                _open.Enqueue(next, g + Heuristic(nx, ny, goal.X, goal.Y));
            }
        }
        return false; // unreachable despite matching components would be a bug, but fail safely
    }

    /// <summary>
    /// Waypoints from <paramref name="from"/> to <paramref name="to"/> (excluding the start point),
    /// with redundant intermediate cells removed where a body of <paramref name="radius"/> can walk straight.
    /// The list is reused by the next call: copy it (as units do) rather than keep it.
    /// </summary>
    public List<Vector2>? FindPath(Vector2 from, Vector2 to, float radius) =>
        SearchCells(Cell.FromPosition(from), Cell.FromPosition(to), _cells) ? Smooth(_cells, from, to, radius) : null;

    /// <summary>Turns a cell path into waypoints, skipping cells a body of <paramref name="radius"/> can walk straight past.</summary>
    private List<Vector2> Smooth(List<Cell> cells, Vector2 from, Vector2 to, float radius)
    {
        var waypoints = _waypoints;
        waypoints.Clear();
        Vector2 anchor = from;
        int i = 1;
        int last = cells.Count - 1;
        while (i <= last)
        {
            // The farthest cell ahead the body can walk to in a straight line: gallop out (1, 2, 4, ... cells) while the
            // corridor is clear, then narrow down between the last clear and the first blocked look. Every waypoint
            // taken has had its corridor checked, so the straight walk to it is sound even past a blocked look between.
            int clear = i, blocked = -1;
            for (int step = 1; clear < last; step *= 2)
            {
                int probe = Math.Min(last, i + step);
                if (!_map.IsCorridorClear(anchor, PointOf(probe), radius))
                {
                    blocked = probe;
                    break;
                }
                clear = probe;
            }
            while (blocked > clear + 1)
            {
                int mid = (clear + blocked) / 2;
                if (_map.IsCorridorClear(anchor, PointOf(mid), radius))
                    clear = mid;
                else
                    blocked = mid;
            }
            anchor = PointOf(clear);
            waypoints.Add(anchor);
            i = clear + 1;
        }

        // Same-cell move: the loop adds nothing, but we still want to reach the exact point.
        if (waypoints.Count == 0 && Vector2.DistanceSquared(from, to) > 1e-6f)
            waypoints.Add(to);
        return waypoints;

        // The last cell targets the exact requested point; intermediate cells target their centers.
        Vector2 PointOf(int index) => index == cells.Count - 1 ? to : cells[index].Center;
    }

    /// <summary>Octile distance: the exact cost of the way over open ground.</summary>
    private static float Heuristic(int ax, int ay, int bx, int by)
    {
        int dx = Math.Abs(ax - bx), dy = Math.Abs(ay - by);
        return Math.Max(dx, dy) + (DiagonalCost - 1f) * Math.Min(dx, dy);
    }

    private void Reconstruct(int goalIndex, List<Cell> path)
    {
        for (int i = goalIndex; i != -1; i = _parent[i])
            path.Add(new Cell(i % _map.Width, i / _map.Width));
        path.Reverse();
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

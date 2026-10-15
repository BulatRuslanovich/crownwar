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
    private readonly List<Cell> _cells = new();
    private readonly List<Cell> _tail = new();
    private readonly List<Vector2> _waypoints = new();

    // The route field of a group order (see BuildField): distance to its goal and the next cell towards it.
    private readonly float[] _fieldDistance;
    private readonly int[] _fieldNext;
    private readonly int[] _fieldSeenStamp;
    private readonly int[] _fieldSettledStamp;
    private readonly int[] _fieldStartStamp;
    private readonly PriorityQueue<int, float> _fieldOpen = new();
    private int _fieldStamp;
    private Cell _fieldGoal;
    private int _stamp;

    public Pathfinder(TileGrid map)
    {
        _map = map;
        int n = map.Width * map.Height;
        _g = new float[n];
        _parent = new int[n];
        _seenStamp = new int[n];
        _closedStamp = new int[n];
        _fieldDistance = new float[n];
        _fieldNext = new int[n];
        _fieldSeenStamp = new int[n];
        _fieldSettledStamp = new int[n];
        _fieldStartStamp = new int[n];
    }

    /// <summary>
    /// Prepares the routes of a group order: one search outwards from <paramref name="goal"/> (the same moves and
    /// costs as <see cref="FindCellPath"/>) until every cell in <paramref name="starts"/> is reached. Every unit then
    /// follows the shortest way to the goal from where it stands (<see cref="FindPathViaField"/>), for the price of a
    /// single search instead of one per unit.
    /// </summary>
    public void BuildField(Cell goal, IEnumerable<Cell> starts)
    {
        if (++_fieldStamp == int.MaxValue)
        {
            Array.Clear(_fieldSeenStamp);
            Array.Clear(_fieldSettledStamp);
            Array.Clear(_fieldStartStamp);
            _fieldStamp = 1;
        }
        _fieldGoal = goal;
        _fieldOpen.Clear();
        int width = _map.Width, height = _map.Height;
        int waiting = 0;
        foreach (var start in starts)
        {
            if (!_map.AreConnected(start, goal))
                continue;
            int s = _map.Index(start.X, start.Y);
            if (_fieldStartStamp[s] == _fieldStamp)
                continue;
            _fieldStartStamp[s] = _fieldStamp;
            waiting++;
        }

        int goalIndex = _map.Index(goal.X, goal.Y);
        _fieldDistance[goalIndex] = 0f;
        _fieldNext[goalIndex] = -1;
        _fieldSeenStamp[goalIndex] = _fieldStamp;
        _fieldOpen.Enqueue(goalIndex, 0f);
        while (waiting > 0 && _fieldOpen.TryDequeue(out int current, out _))
        {
            if (_fieldSettledStamp[current] == _fieldStamp)
                continue;
            _fieldSettledStamp[current] = _fieldStamp;
            if (_fieldStartStamp[current] == _fieldStamp)
                waiting--;

            int cx = current % width, cy = current / width;
            float dCurrent = _fieldDistance[current];
            foreach (var (dx, dy) in Directions)
            {
                int nx = cx + dx, ny = cy + dy;
                if ((uint)nx >= (uint)width || (uint)ny >= (uint)height)
                    continue;
                int next = current + dy * width + dx;
                if (!_map.IsWalkableAt(next) || _fieldSettledStamp[next] == _fieldStamp)
                    continue;
                bool diagonal = dx != 0 && dy != 0;
                if (diagonal && (!_map.IsWalkableAt(current + dx) || !_map.IsWalkableAt(current + dy * width)))
                    continue;
                float d = dCurrent + (diagonal ? DiagonalCost : 1f);
                if (_fieldSeenStamp[next] == _fieldStamp && d >= _fieldDistance[next])
                    continue;
                _fieldSeenStamp[next] = _fieldStamp;
                _fieldDistance[next] = d;
                _fieldNext[next] = current;
                _fieldOpen.Enqueue(next, d);
            }
        }
    }

    /// <summary>
    /// Waypoints like <see cref="FindPath"/>, along the field of the last <see cref="BuildField"/>: the shortest way
    /// towards its goal, then the few steps to <paramref name="to"/> (a slot of the formation around the goal).
    /// Falls back to a search of its own when the start was not part of the field.
    /// </summary>
    public List<Vector2>? FindPathViaField(Vector2 from, Vector2 to, float radius)
    {
        var start = Cell.FromPosition(from);
        var slot = Cell.FromPosition(to);
        int s = _map.InBounds(start) ? _map.Index(start.X, start.Y) : -1;
        if (s < 0 || _fieldSettledStamp[s] != _fieldStamp)
            return FindPath(from, to, radius);

        var cells = _cells;
        cells.Clear();
        for (int i = s; i != -1; i = _fieldNext[i])
        {
            var cell = new Cell(i % _map.Width, i / _map.Width);
            cells.Add(cell);
            if (cell == slot)
                return Smooth(cells, from, to, radius); // the slot lies on the way: stop there
        }
        if (slot != _fieldGoal)
        {
            if (!SearchCells(_fieldGoal, slot, _tail))
                return FindPath(from, to, radius);
            for (int i = 1; i < _tail.Count; i++)
                cells.Add(_tail[i]);
        }
        return Smooth(cells, from, to, radius);
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

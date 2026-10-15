namespace Lordsio.Sim.World;

/// <summary>
/// The shortest ways to one goal cell from everywhere: a search outwards from the goal (the moves and costs of
/// <see cref="Pathfinder"/>) that runs only as far as it has been asked to, and picks up where it stopped when a cell
/// further out is wanted. A group order builds one for its target; units of the group marching on after a fight reuse
/// it instead of searching again. Valid until the map changes (a building placed or removed).
/// </summary>
internal sealed class RouteField
{
    private readonly TileGrid _map;
    private readonly float[] _distance;
    private readonly int[] _next;
    private readonly int[] _seenStamp;
    private readonly int[] _settledStamp;
    private readonly PriorityQueue<int, float> _open = new();
    private int _stamp;

    public RouteField(TileGrid map)
    {
        _map = map;
        int n = map.Width * map.Height;
        _distance = new float[n];
        _next = new int[n];
        _seenStamp = new int[n];
        _settledStamp = new int[n];
    }

    public Cell Goal { get; private set; }

    /// <summary>The map's <see cref="TileGrid.Version"/> the field was started at; a different one makes it stale.</summary>
    public int MapVersion { get; private set; } = -1;

    /// <summary>Starts a new field towards <paramref name="goal"/> (a walkable cell).</summary>
    public void Reset(Cell goal)
    {
        if (++_stamp == int.MaxValue)
        {
            Array.Clear(_seenStamp);
            Array.Clear(_settledStamp);
            _stamp = 1;
        }
        Goal = goal;
        MapVersion = _map.Version;
        _open.Clear();
        int g = _map.Index(goal.X, goal.Y);
        _distance[g] = 0f;
        _next[g] = -1;
        _seenStamp[g] = _stamp;
        _open.Enqueue(g, 0f);
    }

    /// <summary>The next cell (flat index) on the way to the goal, or -1 at the goal.</summary>
    public int Next(int index) => _next[index];

    /// <summary>Whether the way from this cell (flat index) is known, searching on as far as needed; false if unreachable.</summary>
    public bool Reaches(int index)
    {
        int width = _map.Width, height = _map.Height;
        while (_settledStamp[index] != _stamp && _open.TryDequeue(out int current, out _))
        {
            if (_settledStamp[current] == _stamp)
                continue;
            _settledStamp[current] = _stamp;
            int cx = current % width, cy = current / width;
            float dCurrent = _distance[current];
            for (int k = 0; k < Pathfinder.Directions.Length; k++)
            {
                var (dx, dy) = Pathfinder.Directions[k];
                int nx = cx + dx, ny = cy + dy;
                if ((uint)nx >= (uint)width || (uint)ny >= (uint)height)
                    continue;
                int next = current + dy * width + dx;
                if (!_map.IsWalkableAt(next) || _settledStamp[next] == _stamp)
                    continue;
                bool diagonal = dx != 0 && dy != 0;
                if (diagonal && (!_map.IsWalkableAt(current + dx) || !_map.IsWalkableAt(current + dy * width)))
                    continue;
                float d = dCurrent + (diagonal ? Pathfinder.DiagonalCost : 1f);
                if (_seenStamp[next] == _stamp && d >= _distance[next])
                    continue;
                _seenStamp[next] = _stamp;
                _distance[next] = d;
                _next[next] = current;
                _open.Enqueue(next, d);
            }
        }
        return _settledStamp[index] == _stamp;
    }
}

using Lordsio.Sim.World;

namespace Lordsio.Sim.Territory;

/// <summary>
/// Splits the map between settlements: every walkable cell belongs to the settlement that is closest
/// on foot, so a region never jumps across a river. Blocked cells (water, mountains) take the region of
/// the nearest walkable cell, which only matters for drawing borders. Computed once per match.
/// </summary>
public sealed class Regions
{
    public const int None = 0;

    private const float DiagonalCost = 1.41421356f;

    private readonly TileGrid _map;
    private readonly int[] _settlementIdByCell;

    public Regions(TileGrid map, IReadOnlyList<Settlement> settlements)
    {
        _map = map;
        _settlementIdByCell = new int[map.Width * map.Height];
        AssignWalkable(settlements);
        AssignBlocked();
    }

    /// <summary>Id of the settlement owning the cell, or <see cref="None"/>.</summary>
    public int SettlementAt(Cell cell) =>
        _map.InBounds(cell) ? _settlementIdByCell[_map.Index(cell.X, cell.Y)] : None;

    private void AssignWalkable(IReadOnlyList<Settlement> settlements)
    {
        var dist = new float[_settlementIdByCell.Length];
        Array.Fill(dist, float.MaxValue);
        var open = new PriorityQueue<(int Index, int SettlementId), (float Dist, int SettlementId)>();
        foreach (var s in settlements)
        {
            int i = _map.Index(s.Cell.X, s.Cell.Y);
            dist[i] = 0f;
            open.Enqueue((i, s.Id), (0f, s.Id));
        }

        // Ties go to the lower settlement id, so the split does not depend on queue internals.
        while (open.TryDequeue(out var item, out var priority))
        {
            int i = item.Index;
            if (_settlementIdByCell[i] != None || priority.Dist > dist[i])
                continue;
            _settlementIdByCell[i] = item.SettlementId;

            int cx = i % _map.Width, cy = i / _map.Width;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0)
                        continue;
                    int nx = cx + dx, ny = cy + dy;
                    if (!_map.IsWalkable(nx, ny))
                        continue;
                    bool diagonal = dx != 0 && dy != 0;
                    if (diagonal && (!_map.IsWalkable(cx + dx, cy) || !_map.IsWalkable(cx, cy + dy)))
                        continue;
                    int n = _map.Index(nx, ny);
                    float d = priority.Dist + (diagonal ? DiagonalCost : 1f);
                    if (_settlementIdByCell[n] != None || d >= dist[n])
                        continue;
                    dist[n] = d;
                    open.Enqueue((n, item.SettlementId), (d, item.SettlementId));
                }
            }
        }
    }

    private void AssignBlocked()
    {
        var frontier = new Queue<int>();
        for (int i = 0; i < _settlementIdByCell.Length; i++)
        {
            if (_settlementIdByCell[i] != None)
                frontier.Enqueue(i);
        }
        while (frontier.TryDequeue(out int i))
        {
            int x = i % _map.Width, y = i / _map.Width;
            Spread(x + 1, y);
            Spread(x - 1, y);
            Spread(x, y + 1);
            Spread(x, y - 1);

            void Spread(int nx, int ny)
            {
                if (!_map.InBounds(nx, ny) || _map.IsWalkable(nx, ny))
                    return; // walkable cells without a region are cut off from every settlement
                int n = _map.Index(nx, ny);
                if (_settlementIdByCell[n] != None)
                    return;
                _settlementIdByCell[n] = _settlementIdByCell[i];
                frontier.Enqueue(n);
            }
        }
    }
}

using System.Numerics;
using Warcrown.Sim.World;

namespace Warcrown.Sim.Units;

/// <summary>
/// Spreads a group around a point: slots lie on a lattice centered on the point and are taken
/// in order of walking distance from it, so a group never gets slots across a river or wall.
/// </summary>
public static class Formation
{
    /// <summary>Distance between neighbouring slots, in tiles.</summary>
    public const float Spacing = 0.75f;

    /// <summary>
    /// Returns <paramref name="count"/> points near <paramref name="target"/>, closest first.
    /// The target must be on a walkable cell. Points keep <paramref name="clearance"/> from blocked cells
    /// when possible; in a space too cramped for everyone, slots are reused.
    /// </summary>
    public static List<Vector2> ComputeSlots(TileGrid map, Vector2 target, int count, float clearance)
    {
        var slots = new List<Vector2>(count);
        if (count <= 0)
            return slots;
        var goal = Cell.FromPosition(target);
        if (!map.IsWalkable(goal))
            throw new ArgumentException($"Formation target {target} is not walkable.", nameof(target));

        var cells = NearestCells(map, goal, cellsWanted: (int)MathF.Ceiling(count * Spacing * Spacing * 2f) + 9);

        var candidates = Scratch.Candidates;
        candidates.Clear();
        foreach (var (cell, walkDistance) in cells)
        {
            int i0 = (int)MathF.Ceiling((cell.X - target.X) / Spacing);
            int i1 = (int)MathF.Floor((cell.X + 1 - target.X) / Spacing - 1e-4f);
            int j0 = (int)MathF.Ceiling((cell.Y - target.Y) / Spacing);
            int j1 = (int)MathF.Floor((cell.Y + 1 - target.Y) / Spacing - 1e-4f);
            for (int j = j0; j <= j1; j++)
            {
                for (int i = i0; i <= i1; i++)
                {
                    var p = new Vector2(target.X + i * Spacing, target.Y + j * Spacing);
                    if (Cell.FromPosition(p) != cell || !map.HasClearance(p, clearance))
                        continue;
                    // Walking distance decides the ring; straight distance orders points inside one cell.
                    candidates.Add((p, walkDistance + 0.01f * Vector2.Distance(p, target)));
                }
            }
        }

        if (candidates.Count == 0)
        {
            // Every lattice point hugs a wall: fall back to cell centers, which are always walkable.
            foreach (var (cell, walkDistance) in cells)
                candidates.Add((cell.Center, walkDistance));
        }

        candidates.Sort((a, b) => a.Key.CompareTo(b.Key));
        for (int k = 0; k < count; k++)
            slots.Add(candidates[k % candidates.Count].Point);
        return slots;
    }

    /// <summary>
    /// Slots for flyers: the lattice around <paramref name="target"/>, closest first, regardless of the ground below
    /// (kept inside the map, so a group at an edge bunches up rather than leaving it). <paramref name="spacing"/>
    /// is at least <see cref="Spacing"/>, more for big bodies.
    /// </summary>
    public static List<Vector2> ComputeAirSlots(TileGrid map, Vector2 target, int count, float spacing)
    {
        spacing = MathF.Max(Spacing, spacing);
        var slots = new List<Vector2>(count);
        int reach = (int)MathF.Ceiling(MathF.Sqrt(count)) + 1;
        var candidates = Scratch.Candidates;
        candidates.Clear();
        for (int j = -reach; j <= reach; j++)
        {
            for (int i = -reach; i <= reach; i++)
            {
                var p = Flight.Clamp(map, new Vector2(target.X + i * spacing, target.Y + j * spacing));
                candidates.Add((p, Vector2.Distance(p, target) + 1e-4f * (j * (2 * reach + 1) + i)));
            }
        }
        candidates.Sort((a, b) => a.Key.CompareTo(b.Key));
        for (int k = 0; k < count; k++)
            slots.Add(candidates[k % candidates.Count].Point);
        return slots;
    }

    /// <summary>
    /// Dijkstra from <paramref name="goal"/> (8 directions, no corner cutting) until enough cells are settled.
    /// The list returned is scratch space, valid until the next call.
    /// </summary>
    private static List<(Cell Cell, float Distance)> NearestCells(TileGrid map, Cell goal, int cellsWanted)
    {
        var scratch = Scratch;
        var result = scratch.Result;
        var best = scratch.Best;
        var settled = scratch.Settled;
        var open = scratch.Open;
        result.Clear();
        best.Clear();
        settled.Clear();
        open.Clear();
        best[goal] = 0f;
        open.Enqueue(goal, 0f);

        while (result.Count < cellsWanted && open.TryDequeue(out var cell, out float dist))
        {
            if (!settled.Add(cell))
                continue;
            result.Add((cell, dist));

            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0)
                        continue;
                    var next = new Cell(cell.X + dx, cell.Y + dy);
                    if (!map.IsWalkable(next) || settled.Contains(next))
                        continue;
                    bool diagonal = dx != 0 && dy != 0;
                    if (diagonal && (!map.IsWalkable(cell.X + dx, cell.Y) || !map.IsWalkable(cell.X, cell.Y + dy)))
                        continue;
                    float d = dist + (diagonal ? Pathfinder.DiagonalCost : 1f);
                    if (best.TryGetValue(next, out float known) && known <= d)
                        continue;
                    best[next] = d;
                    open.Enqueue(next, d);
                }
            }
        }
        return result;
    }

    /// <summary>Search buffers reused between calls (every recruit and every move order lays out slots).</summary>
    private sealed class Buffers
    {
        public readonly List<(Cell, float)> Result = new();
        public readonly Dictionary<Cell, float> Best = new();
        public readonly HashSet<Cell> Settled = new();
        public readonly PriorityQueue<Cell, float> Open = new();
        public readonly List<(Vector2 Point, float Key)> Candidates = new();
    }

    [ThreadStatic] private static Buffers? _scratch;

    private static Buffers Scratch => _scratch ??= new Buffers();
}

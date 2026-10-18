using System.Numerics;

namespace Warcrown.Sim.World;

/// <summary>
/// Terrain grid plus cells occupied by buildings, and the connectivity derived from both.
/// Buildings are solid: placing or removing one updates walkability and the regions.
/// </summary>
public sealed class TileGrid
{
    /// <summary>Pass as the component to <see cref="FindNearestInComponent"/> to accept any walkable cell.</summary>
    public const int AnyComponent = int.MinValue;

    private readonly Terrain[] _tiles;
    private readonly bool[] _occupied;
    /// <summary>Walkable terrain without a building on it: the cell test of every path search, kept up to date.</summary>
    private readonly bool[] _walkable;
    private readonly int[] _component;

    /// <summary>Goes up whenever a building footprint changes walkability, so kept routes know they are stale.</summary>
    public int Version { get; private set; }

    public int Width { get; }
    public int Height { get; }

    public TileGrid(int width, int height, Terrain[] tiles)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Map size must be positive.");
        if (tiles.Length != width * height)
            throw new ArgumentException($"Expected {width * height} tiles, got {tiles.Length}.", nameof(tiles));

        Width = width;
        Height = height;
        _tiles = tiles;
        _occupied = new bool[tiles.Length];
        _walkable = new bool[tiles.Length];
        for (int i = 0; i < tiles.Length; i++)
            _walkable[i] = TerrainInfo.IsWalkable(tiles[i]);
        _component = new int[tiles.Length];
        LabelComponents();
    }

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;
    public bool InBounds(Cell cell) => InBounds(cell.X, cell.Y);

    public Terrain GetTerrain(Cell cell) => _tiles[Index(cell.X, cell.Y)];

    public bool IsWalkable(int x, int y) => InBounds(x, y) && _walkable[Index(x, y)];

    /// <summary>Walkability by flat index (<see cref="Index"/>), for tight loops that have checked the bounds.</summary>
    internal bool IsWalkableAt(int index) => _walkable[index];

    public bool IsWalkable(Cell cell) => IsWalkable(cell.X, cell.Y);

    /// <summary>A building stands on the cell.</summary>
    public bool IsOccupied(Cell cell) => InBounds(cell) && _occupied[Index(cell.X, cell.Y)];

    /// <summary>
    /// Fells the woods on the <paramref name="size"/>×<paramref name="size"/> square at <paramref name="anchor"/>: its
    /// forest cells become open ground. Woods and open ground are alike to the rules (both walkable, both buildable), so
    /// paths and regions stay as they are; the picture shows a clearing under what stands there.
    /// </summary>
    /// <returns>Whether any trees were felled.</returns>
    public bool ClearWoods(Cell anchor, int size)
    {
        bool felled = false;
        for (int y = anchor.Y; y < anchor.Y + size; y++)
            for (int x = anchor.X; x < anchor.X + size; x++)
            {
                if (!InBounds(x, y) || _tiles[Index(x, y)] != Terrain.Forest)
                    continue;
                _tiles[Index(x, y)] = Terrain.Grass;
                felled = true;
            }
        return felled;
    }

    /// <summary>
    /// Marks a building footprint (the <paramref name="size"/>×<paramref name="size"/> square at <paramref name="anchor"/>)
    /// as solid, or frees it, and recomputes connectivity unless <paramref name="relabel"/> is false — then the caller
    /// changes several footprints in a row and calls <see cref="Relabel"/> once at the end.
    /// </summary>
    public void SetOccupied(Cell anchor, int size, bool occupied, bool relabel = true)
    {
        for (int y = anchor.Y; y < anchor.Y + size; y++)
        {
            for (int x = anchor.X; x < anchor.X + size; x++)
            {
                if (!InBounds(x, y))
                    throw new ArgumentOutOfRangeException(nameof(anchor), $"Cell ({x}, {y}) is outside the map.");
                int i = Index(x, y);
                _occupied[i] = occupied;
                _walkable[i] = !occupied && TerrainInfo.IsWalkable(_tiles[i]);
            }
        }
        Version++;
        if (relabel)
            LabelComponents();
    }

    /// <summary>Recomputes connectivity after footprints were changed with <c>relabel: false</c>.</summary>
    public void Relabel() => LabelComponents();

    /// <summary>
    /// Id of the walkable region the cell belongs to, or -1 for blocked / out-of-bounds cells.
    /// Two cells are mutually reachable exactly when their component ids are equal and not -1.
    /// </summary>
    public int ComponentOf(Cell cell) => InBounds(cell) ? _component[Index(cell.X, cell.Y)] : -1;

    public bool AreConnected(Cell a, Cell b)
    {
        int ca = ComponentOf(a);
        return ca >= 0 && ca == ComponentOf(b);
    }

    /// <summary>
    /// Walkable cell of the given component closest (euclidean, between centers) to <paramref name="target"/>.
    /// Returns null when the component has no cell within <paramref name="maxRadius"/>.
    /// </summary>
    public Cell? FindNearestInComponent(Cell target, int component, int maxRadius)
    {
        if (component < 0 && component != AnyComponent)
            return null;
        if (Matches(target))
            return target;

        Cell? best = null;
        float bestDistSq = float.MaxValue;
        for (int r = 1; r <= maxRadius; r++)
        {
            // Cells on ring r are at least r away, so once a candidate is closer than that we are done.
            if (best != null && bestDistSq <= (float)r * r)
                break;

            for (int dy = -r; dy <= r; dy++)
            {
                bool edgeRow = dy == -r || dy == r;
                for (int dx = -r; dx <= r; dx += edgeRow ? 1 : 2 * r)
                {
                    var cell = new Cell(target.X + dx, target.Y + dy);
                    if (!Matches(cell))
                        continue;
                    float distSq = dx * dx + dy * dy;
                    if (distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        best = cell;
                    }
                }
            }
        }
        return best;

        bool Matches(Cell cell)
        {
            int c = ComponentOf(cell);
            return component == AnyComponent ? c >= 0 : c == component;
        }
    }

    /// <summary>
    /// True when every cell touched by the segment is walkable. Passing exactly through a cell corner
    /// requires both side cells to be walkable, matching the pathfinder's no-corner-cutting rule.
    /// </summary>
    public bool IsSegmentClear(Vector2 from, Vector2 to)
    {
        var cell = Cell.FromPosition(from);
        var end = Cell.FromPosition(to);
        if (!IsWalkable(cell))
            return false;

        Vector2 d = to - from;
        int stepX = Math.Sign(d.X);
        int stepY = Math.Sign(d.Y);
        float tDeltaX = stepX != 0 ? 1f / MathF.Abs(d.X) : float.PositiveInfinity;
        float tDeltaY = stepY != 0 ? 1f / MathF.Abs(d.Y) : float.PositiveInfinity;
        float tMaxX = stepX > 0 ? (cell.X + 1 - from.X) * tDeltaX
            : stepX < 0 ? (from.X - cell.X) * tDeltaX
            : float.PositiveInfinity;
        float tMaxY = stepY > 0 ? (cell.Y + 1 - from.Y) * tDeltaY
            : stepY < 0 ? (from.Y - cell.Y) * tDeltaY
            : float.PositiveInfinity;

        int x = cell.X, y = cell.Y;
        int guard = Math.Abs(end.X - x) + Math.Abs(end.Y - y) + 2;
        while (x != end.X || y != end.Y)
        {
            if (--guard < 0)
                return false; // float drift; be conservative
            if (MathF.Abs(tMaxX - tMaxY) < 1e-5f)
            {
                if (!IsWalkable(x + stepX, y) || !IsWalkable(x, y + stepY))
                    return false;
                x += stepX;
                y += stepY;
                tMaxX += tDeltaX;
                tMaxY += tDeltaY;
            }
            else if (tMaxX < tMaxY)
            {
                x += stepX;
                tMaxX += tDeltaX;
            }
            else
            {
                y += stepY;
                tMaxY += tDeltaY;
            }
            if (!IsWalkable(x, y))
                return false;
        }
        return true;
    }

    /// <summary>Segment check for a body of the given radius: the center line plus both side lines.</summary>
    public bool IsCorridorClear(Vector2 from, Vector2 to, float radius)
    {
        if (!IsSegmentClear(from, to))
            return false;
        Vector2 d = to - from;
        float len = d.Length();
        if (radius <= 0f || len < 1e-5f)
            return true;
        Vector2 side = new Vector2(-d.Y, d.X) / len * radius;
        return IsSegmentClear(from + side, to + side) && IsSegmentClear(from - side, to - side);
    }

    /// <summary>True when a circle of <paramref name="radius"/> (&lt; 1) at <paramref name="p"/> touches no blocked cell.</summary>
    public bool HasClearance(Vector2 p, float radius)
    {
        var center = Cell.FromPosition(p);
        if (!IsWalkable(center))
            return false;
        for (int y = center.Y - 1; y <= center.Y + 1; y++)
        {
            for (int x = center.X - 1; x <= center.X + 1; x++)
            {
                if (!IsWalkable(x, y) && DistanceSqToCell(p, x, y) < radius * radius)
                    return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Moves a circle centered in a walkable cell out of the neighbouring blocked cells (and the map edge),
    /// so bodies slide along walls instead of overlapping them. A center inside a blocked cell is returned unchanged.
    /// </summary>
    public Vector2 PushOutOfWalls(Vector2 p, float radius)
    {
        var center = Cell.FromPosition(p);
        if (!IsWalkable(center))
            return p;
        for (int y = center.Y - 1; y <= center.Y + 1; y++)
        {
            for (int x = center.X - 1; x <= center.X + 1; x++)
            {
                if (IsWalkable(x, y))
                    continue;
                var closest = new Vector2(Math.Clamp(p.X, x, x + 1), Math.Clamp(p.Y, y, y + 1));
                Vector2 away = p - closest;
                float dist = away.Length();
                if (dist > 1e-6f && dist < radius)
                    p += away / dist * (radius - dist);
            }
        }
        return p;
    }

    private static float DistanceSqToCell(Vector2 p, int x, int y)
    {
        var closest = new Vector2(Math.Clamp(p.X, x, x + 1), Math.Clamp(p.Y, y, y + 1));
        return Vector2.DistanceSquared(p, closest);
    }

    internal int Index(int x, int y) => y * Width + x;

    private readonly Stack<int> _labelStack = new();

    private void LabelComponents()
    {
        Array.Fill(_component, -1);
        var stack = _labelStack;
        int next = 0;
        for (int start = 0; start < _tiles.Length; start++)
        {
            if (_component[start] != -1 || !_walkable[start])
                continue;

            // 4-connectivity is sufficient: a diagonal step is only legal when both orthogonal
            // neighbours are walkable, so it never connects regions that are otherwise separate.
            _component[start] = next;
            stack.Push(start);
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                int x = i % Width, y = i / Width;
                TryVisit(x + 1, y);
                TryVisit(x - 1, y);
                TryVisit(x, y + 1);
                TryVisit(x, y - 1);
            }
            next++;
        }

        void TryVisit(int x, int y)
        {
            if (!IsWalkable(x, y))
                return;
            int i = Index(x, y);
            if (_component[i] != -1)
                return;
            _component[i] = next;
            stack.Push(i);
        }
    }
}

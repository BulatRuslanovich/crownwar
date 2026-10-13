using System.Numerics;

namespace Lordsio.Sim.World;

/// <summary>
/// Makes a random four-player map from a seed: the same seed always gives the same map (so lockstep peers
/// only exchange the seed). One quadrant is generated and mirrored both ways, so every start has exactly
/// the same surroundings — fair for any player count, since factions are dealt to the corners at random.
/// <list type="bullet">
/// <item>Terrain: forest clumps, mountain ridges, maybe a lake; maybe a river along one or both mirror axes.</item>
/// <item>A ring road links the four castles, bridging any water on the way, so every castle reaches every other.</item>
/// <item>Gold: a safe and an exposed deposit per player, contested ones on the borders between neighbours,
/// and a rich one in the middle (or four around it when a river runs through the middle).</item>
/// </list>
/// Deposits are only placed on open ground the castles can reach.
/// </summary>
public static class MapGenerator
{
    public const int Width = 96;
    public const int Height = 64;
    private const int QuarterWidth = Width / 2;
    private const int QuarterHeight = Height / 2;
    private const int CastleSize = 3;
    private const int RichYield = 5;
    private const int Attempts = 60;

    private enum Rivers { None, Vertical, Horizontal, Both }

    public static MapDefinition Generate(int seed)
    {
        var rng = new Random(seed);
        var quarter = new Terrain[QuarterWidth * QuarterHeight];

        // Castle (top-left cell) and the corner of the ring road, which passes outside the castle.
        var start = new Cell(rng.Next(7, 13), rng.Next(6, 11));
        int roadX = start.X - 4, roadY = start.Y - 4;
        var castleCenter = new Vector2(start.X + CastleSize / 2f, start.Y + CastleSize / 2f);

        for (int i = rng.Next(7, 12); i > 0; i--)
            Blob(quarter, rng, Terrain.Forest, RandomPoint(rng), 1.5f + 2f * (float)rng.NextDouble());
        for (int i = rng.Next(1, 4); i > 0; i--)
            Ridge(quarter, rng);
        if (rng.NextDouble() < 0.45)
            Blob(quarter, rng, Terrain.Water, RandomPoint(rng), 2f + 1.5f * (float)rng.NextDouble());

        var rivers = PickRivers(rng);
        if (rivers is Rivers.Vertical or Rivers.Both)
            River(quarter, rng, vertical: true);
        if (rivers is Rivers.Horizontal or Rivers.Both)
            River(quarter, rng, vertical: false);

        // Open ground around the castle for the castle itself, the starting army and the first buildings.
        for (int y = 0; y < QuarterHeight; y++)
            for (int x = 0; x < QuarterWidth; x++)
                if (Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), castleCenter) < 6.5f)
                    Set(quarter, x, y, Terrain.Grass);

        // Ring road: across the top to the vertical axis, down the side to the horizontal axis.
        for (int x = roadX; x < QuarterWidth; x++)
            Set(quarter, x, roadY, Terrain.Road);
        for (int y = roadY; y < QuarterHeight; y++)
            Set(quarter, roadX, y, Terrain.Road);
        // A river along an axis gets a second crossing nearer the middle, so it is not only crossed at the edges.
        if (rivers is Rivers.Vertical or Rivers.Both)
        {
            int y = rng.Next(roadY + 8, QuarterHeight - 6);
            for (int x = QuarterWidth - 7; x < QuarterWidth; x++)
                Set(quarter, x, y, Terrain.Road);
        }
        if (rivers is Rivers.Horizontal or Rivers.Both)
        {
            int x = rng.Next(roadX + 10, QuarterWidth - 8);
            for (int y = QuarterHeight - 7; y < QuarterHeight; y++)
                Set(quarter, x, y, Terrain.Road);
        }

        var grid = new TileGrid(Width, Height, Mirror(quarter));
        var starts = MirrorAnchors(start, CastleSize);
        int home = grid.ComponentOf(new Cell(start.X + 1, start.Y + CastleSize));

        var taken = new List<(Cell Anchor, int Size)> { (new Cell(start.X - 1, start.Y - 1), CastleSize + 3) };
        var deposits = new List<DepositSpawn>();
        void Add(Cell? anchor, int yield)
        {
            if (anchor is not { } a)
                return;
            taken.Add((new Cell(a.X - 1, a.Y - 1), GoldDeposit.Size + 2));
            foreach (var mirrored in MirrorAnchors(a, GoldDeposit.Size))
                if (!deposits.Exists(d => d.Anchor == mirrored))
                    deposits.Add(new DepositSpawn(mirrored, yield));
        }

        // Safe: close to home. Exposed: further out, towards the middle.
        Add(Place(grid, rng, taken, home, () => Around(rng, castleCenter, 5.5f, 8f)), MapLoader.DefaultDepositYield);
        Add(Place(grid, rng, taken, home, () => Around(rng, castleCenter, 12f, 17f)), MapLoader.DefaultDepositYield);
        // Contested: straddling a mirror axis, shared by two neighbours.
        Add(Place(grid, rng, taken, home, () => new Cell(QuarterWidth - 1, rng.Next(roadY + 4, QuarterHeight - 6))), MapLoader.DefaultDepositYield);
        Add(Place(grid, rng, taken, home, () => new Cell(rng.Next(roadX + 4, QuarterWidth - 6), QuarterHeight - 1)), MapLoader.DefaultDepositYield);
        // Rich: the very middle, or four around it if the middle is underwater.
        var middle = new Cell(QuarterWidth - 1, QuarterHeight - 1);
        if (Fits(grid, taken, home, middle))
            Add(middle, RichYield);
        else
            Add(Place(grid, rng, taken, home, () => new Cell(QuarterWidth - rng.Next(4, 14), QuarterHeight - rng.Next(4, 12)))
                ?? Place(grid, rng, taken, home, () => Around(rng, castleCenter, 16f, 26f)), RichYield);

        return new MapDefinition(grid, Array.Empty<UnitSpawn>(), starts, deposits);
    }

    private static Rivers PickRivers(Random rng)
    {
        double roll = rng.NextDouble();
        return roll < 0.3 ? Rivers.None : roll < 0.6 ? Rivers.Vertical : roll < 0.85 ? Rivers.Horizontal : Rivers.Both;
    }

    private static Vector2 RandomPoint(Random rng) =>
        new(rng.Next(0, QuarterWidth), rng.Next(0, QuarterHeight));

    private static void Set(Terrain[] quarter, int x, int y, Terrain terrain)
    {
        if (x >= 0 && y >= 0 && x < QuarterWidth && y < QuarterHeight)
            quarter[y * QuarterWidth + x] = terrain;
    }

    /// <summary>A rough disc: cells within the radius, with a ragged edge.</summary>
    private static void Blob(Terrain[] quarter, Random rng, Terrain terrain, Vector2 center, float radius)
    {
        int r = (int)MathF.Ceiling(radius) + 1;
        for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
                if (dx * dx + dy * dy <= radius * radius + rng.Next(-1, 2) * radius)
                    Set(quarter, (int)center.X + dx, (int)center.Y + dy, terrain);
    }

    /// <summary>A mountain ridge: a wandering line, one or two tiles thick.</summary>
    private static void Ridge(Terrain[] quarter, Random rng)
    {
        var p = RandomPoint(rng);
        int length = rng.Next(6, 15);
        bool thick = rng.NextDouble() < 0.5;
        var direction = rng.NextDouble() < 0.5 ? new Vector2(1, 0) : new Vector2(0, 1);
        for (int i = 0; i < length; i++)
        {
            Set(quarter, (int)p.X, (int)p.Y, Terrain.Mountain);
            if (thick)
                Set(quarter, (int)p.X + (int)direction.Y, (int)p.Y + (int)direction.X, Terrain.Mountain);
            if (rng.NextDouble() < 0.2)
                direction = new Vector2(direction.Y, direction.X); // turn
            p += direction;
        }
    }

    /// <summary>
    /// A river along the quarter's edge on a mirror axis: one or two tiles here, so two to four once mirrored,
    /// with the width changing gently along its course.
    /// </summary>
    private static void River(Terrain[] quarter, Random rng, bool vertical)
    {
        int length = vertical ? QuarterHeight : QuarterWidth;
        int width = rng.Next(1, 3);
        for (int i = 0; i < length; i++)
        {
            if (rng.NextDouble() < 0.15)
                width = Math.Clamp(width + (rng.NextDouble() < 0.5 ? -1 : 1), 1, 2);
            for (int w = 0; w < width; w++)
            {
                if (vertical)
                    Set(quarter, QuarterWidth - 1 - w, i, Terrain.Water);
                else
                    Set(quarter, i, QuarterHeight - 1 - w, Terrain.Water);
            }
        }
    }

    private static Terrain[] Mirror(Terrain[] quarter)
    {
        var tiles = new Terrain[Width * Height];
        for (int y = 0; y < QuarterHeight; y++)
        {
            for (int x = 0; x < QuarterWidth; x++)
            {
                var t = quarter[y * QuarterWidth + x];
                tiles[y * Width + x] = t;
                tiles[y * Width + (Width - 1 - x)] = t;
                tiles[(Height - 1 - y) * Width + x] = t;
                tiles[(Height - 1 - y) * Width + (Width - 1 - x)] = t;
            }
        }
        return tiles;
    }

    /// <summary>The four mirror images of a square footprint's top-left cell, top-left quarter first.</summary>
    private static Cell[] MirrorAnchors(Cell anchor, int size)
    {
        int mx = Width - size - anchor.X, my = Height - size - anchor.Y;
        return new[] { anchor, new Cell(mx, anchor.Y), new Cell(anchor.X, my), new Cell(mx, my) };
    }

    private static Cell Around(Random rng, Vector2 center, float minDistance, float maxDistance)
    {
        double angle = rng.NextDouble() * Math.PI * 2;
        float distance = minDistance + (maxDistance - minDistance) * (float)rng.NextDouble();
        return new Cell((int)(center.X + MathF.Cos((float)angle) * distance) - 1, (int)(center.Y + MathF.Sin((float)angle) * distance) - 1);
    }

    private static Cell? Place(TileGrid grid, Random rng, List<(Cell Anchor, int Size)> taken, int home, Func<Cell> candidate)
    {
        for (int i = 0; i < Attempts; i++)
        {
            var anchor = candidate();
            if (Fits(grid, taken, home, anchor))
                return anchor;
        }
        return null; // this seed has no room for it; the map is still fair, just with one deposit less per player
    }

    /// <summary>
    /// A deposit fits on open ground (not road), inside the top-left quarter or straddling its mirror edge,
    /// clear of other things, with a walkable neighbour the castles can reach.
    /// </summary>
    private static bool Fits(TileGrid grid, List<(Cell Anchor, int Size)> taken, int home, Cell anchor)
    {
        const int size = GoldDeposit.Size;
        if (anchor.X < 1 || anchor.Y < 1 || anchor.X > QuarterWidth - 1 || anchor.Y > QuarterHeight - 1)
            return false;
        // Either fully inside the quarter or exactly on the axis (so its mirror image is itself).
        if (anchor.X > QuarterWidth - size && anchor.X != QuarterWidth - 1)
            return false;
        if (anchor.Y > QuarterHeight - size && anchor.Y != QuarterHeight - 1)
            return false;

        for (int y = anchor.Y; y < anchor.Y + size; y++)
            for (int x = anchor.X; x < anchor.X + size; x++)
                if (!TerrainInfo.IsBuildable(grid.GetTerrain(new Cell(x, y))))
                    return false;

        foreach (var (other, otherSize) in taken)
        {
            bool apart = anchor.X + size <= other.X || other.X + otherSize <= anchor.X
                      || anchor.Y + size <= other.Y || other.Y + otherSize <= anchor.Y;
            if (!apart)
                return false;
        }

        for (int y = anchor.Y - 1; y <= anchor.Y + size; y++)
            for (int x = anchor.X - 1; x <= anchor.X + size; x++)
            {
                var cell = new Cell(x, y);
                if (grid.InBounds(cell) && grid.IsWalkable(cell) && grid.ComponentOf(cell) == home)
                    return true;
            }
        return false;
    }
}

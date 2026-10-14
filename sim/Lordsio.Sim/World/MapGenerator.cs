using System.Numerics;

namespace Lordsio.Sim.World;

/// <summary>How a part of the map looks and what grows there: the generator shapes each biome's land differently.</summary>
public enum Biome : byte
{
    Meadow,
    Winter,
    Desert,
}

/// <summary>
/// Makes a random four-player map from a seed, the whole map at once from Perlin noise, like a Minecraft world: no
/// mirroring, so one player may get a river at the door and gold close by, another a mountain range and a long way
/// to the nearest deposit. The same seed always gives the same map (so lockstep peers only exchange the seed).
/// <list type="bullet">
/// <item>Climate: temperature and humidity noise place the biomes — cold winter, hot and dry desert, temperate
/// meadow — in large natural regions.</item>
/// <item>Land: elevation and moisture noise give mountains on the heights, lakes in the hollows and woods where it
/// is wet, each biome in its own way; rivers wind along the ridges of another noise.</item>
/// <item>Castles: one in each corner region, at a random spot with enough dry land around it (no hopeless starts),
/// cleared for the castle and the first buildings.</item>
/// <item>Roads: laid by a path search over the land between neighbouring castles and to the middle, avoiding
/// mountains and water where they can, bridging rivers and crossing ridges by passes where they must — so every
/// castle reaches every other.</item>
/// <item>Gold: one deposit near each castle is certain; the rest is scattered over the map as it falls, with rich
/// ones far from everybody.</item>
/// </list>
/// </summary>
public static class MapGenerator
{
    public const int Width = 192;
    public const int Height = 128;
    private const int CastleSize = 3;
    private const int RichYield = 5;
    private const int Attempts = 80;
    /// <summary>Open ground cleared around each castle (tiles from its centre).</summary>
    private const float ClearRadius = 7.5f;

    public static MapDefinition Generate(int seed)
    {
        var rng = new Random(seed);
        var elevation = new Perlin(rng.Next());
        var moisture = new Perlin(rng.Next());
        var temperature = new Perlin(rng.Next());
        var humidity = new Perlin(rng.Next());
        var rivers = new Perlin(rng.Next());
        var tiles = new Terrain[Width * Height];
        var biomes = new Biome[Width * Height];

        // 1. Climate and land.
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                // Four octaves: the large climate zones, with borders that wind instead of running in long steps.
                float t = temperature.Fractal(x * 0.014f, y * 0.014f, 4);
                float h = humidity.Fractal(x * 0.018f, y * 0.018f, 4);
                var biome = t < 0.42f ? Biome.Winter : t > 0.57f && h < 0.55f ? Biome.Desert : Biome.Meadow;
                biomes[y * Width + x] = biome;

                float e = elevation.Fractal(x * 0.05f, y * 0.05f, 4);
                float m = moisture.Fractal(x * 0.11f, y * 0.11f, 3);
                var land = Grow(biome, e, m);
                // Rivers: where the river noise crosses its middle, on land that is neither mountain nor lake.
                float r = MathF.Abs(rivers.Fractal(x * 0.022f, y * 0.022f, 2) - 0.5f);
                float riverWidth = biome == Biome.Desert ? 0.008f : 0.014f;
                if (land is Terrain.Grass or Terrain.Forest && r < riverWidth)
                    land = Terrain.Water;
                tiles[y * Width + x] = land;
            }
        }

        // Woods or ranges of two biomes never touch: a narrow clearing (or a pass) runs between them, which the
        // smooth blend of the ground then covers, instead of pines and oaks, grey and red rocks meeting on a straight line.
        var cleared = new List<int>();
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                var t = tiles[y * Width + x];
                if (t is not (Terrain.Forest or Terrain.Mountain))
                    continue;
                bool foreign = false;
                for (int dy = -1; dy <= 1 && !foreign; dy++)
                    for (int dx = -1; dx <= 1 && !foreign; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        foreign = nx >= 0 && ny >= 0 && nx < Width && ny < Height && tiles[ny * Width + nx] == t
                                  && biomes[ny * Width + nx] != biomes[y * Width + x];
                    }
                if (foreign)
                    cleared.Add(y * Width + x);
            }
        foreach (int i in cleared)
            tiles[i] = Terrain.Grass;

        // Water that only touches diagonally (a river stepping across the grid) is joined up, so rivers run as one
        // channel instead of a chain of ponds.
        for (int y = 0; y < Height - 1; y++)
            for (int x = 0; x < Width - 1; x++)
            {
                bool a = tiles[y * Width + x] == Terrain.Water, b = tiles[y * Width + x + 1] == Terrain.Water;
                bool c = tiles[(y + 1) * Width + x] == Terrain.Water, d = tiles[(y + 1) * Width + x + 1] == Terrain.Water;
                if (a && d && !b && !c)
                    tiles[y * Width + x + 1] = Terrain.Water;
                else if (b && c && !a && !d)
                    tiles[y * Width + x] = Terrain.Water;
            }

        // 2. Castles: a good spot in each corner region, then cleared.
        var starts = new List<Cell>();
        var regions = new[] { (Left: true, Top: true), (Left: false, Top: true), (Left: true, Top: false), (Left: false, Top: false) };
        foreach (var (left, top) in regions)
            starts.Add(PickStart(tiles, rng, left, top));
        foreach (var start in starts)
            Clear(tiles, CenterOf(start, CastleSize), ClearRadius);

        // 3. Roads between neighbouring castles (around the map) and from each castle to the middle.
        var middle = new Cell(Width / 2, Height / 2);
        var doors = starts.Select(s => new Cell(s.X + 1, s.Y + CastleSize + 1)).ToList();
        int[] ring = { 0, 1, 3, 2 }; // top-left, top-right, bottom-right, bottom-left
        for (int i = 0; i < ring.Length; i++)
            Road(tiles, doors[ring[i]], doors[ring[(i + 1) % ring.Length]]);
        foreach (var door in doors)
            Road(tiles, door, middle);

        var grid = new TileGrid(Width, Height, tiles);
        int home = grid.ComponentOf(doors[0]);

        // 4. Gold.
        var taken = starts.Select(s => (Anchor: new Cell(s.X - 1, s.Y - 1), Size: CastleSize + 3)).ToList();
        var deposits = new List<DepositSpawn>();
        bool Add(Cell? anchor, int yield)
        {
            if (anchor is not { } a)
                return false;
            taken.Add((new Cell(a.X - 2, a.Y - 2), GoldDeposit.Size + 4));
            deposits.Add(new DepositSpawn(a, yield));
            return true;
        }

        // One certain deposit by each castle; whatever else lies near it is luck.
        foreach (var start in starts)
        {
            var center = CenterOf(start, CastleSize);
            Add(Place(grid, rng, taken, home, () => Around(rng, center, 6f, 10f))
                ?? Place(grid, rng, taken, home, () => Around(rng, center, 10f, 15f)), MapLoader.DefaultDepositYield);
        }
        var castles = starts.Select(s => CenterOf(s, CastleSize)).ToList();
        float Nearest(Cell c) => castles.Min(k => Vector2.Distance(k, new Vector2(c.X + 1, c.Y + 1)));
        // Scattered deposits, anywhere a few tiles from the castles.
        int scattered = rng.Next(32, 44);
        for (int i = 0; i < scattered; i++)
            Add(Place(grid, rng, taken, home, () => RandomCell(rng), c => Nearest(c) > 9f), MapLoader.DefaultDepositYield);
        // Rich ones, far from everybody.
        int rich = rng.Next(6, 10);
        for (int i = 0; i < rich; i++)
            Add(Place(grid, rng, taken, home, () => RandomCell(rng), c => Nearest(c) > 30f), RichYield);

        return new MapDefinition(grid, Array.Empty<UnitSpawn>(), starts, deposits, biomes);
    }

    // ---------------------------------------------------------------- land

    /// <summary>What a biome grows at a given elevation and moisture (both 0..1, most values near 0.5).</summary>
    private static Terrain Grow(Biome biome, float elevation, float moisture) => biome switch
    {
        Biome.Winter => elevation > 0.65f ? Terrain.Mountain : elevation < 0.30f ? Terrain.Water : moisture > 0.55f ? Terrain.Forest : Terrain.Grass,
        Biome.Desert => elevation > 0.69f ? Terrain.Mountain : elevation < 0.26f ? Terrain.Water
            : moisture > 0.66f || moisture > 0.58f && elevation < 0.37f ? Terrain.Forest : Terrain.Grass, // palms by the oases
        _ => elevation > 0.68f ? Terrain.Mountain : elevation < 0.30f ? Terrain.Water : moisture > 0.57f ? Terrain.Forest : Terrain.Grass,
    };

    /// <summary>
    /// Classic Perlin gradient noise: a shuffled permutation of 0..255 picks one of eight gradients at each lattice
    /// point, the gradients' dot products are blended with the quintic fade, and octaves are summed — scaled to 0..1.
    /// </summary>
    private sealed class Perlin
    {
        private static readonly (float X, float Y)[] Gradients =
        {
            (1, 0), (-1, 0), (0, 1), (0, -1), (0.7071f, 0.7071f), (-0.7071f, 0.7071f), (0.7071f, -0.7071f), (-0.7071f, -0.7071f),
        };

        private readonly int[] _perm = new int[512];

        public Perlin(int seed)
        {
            var rng = new Random(seed);
            var p = Enumerable.Range(0, 256).ToArray();
            for (int i = 255; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (p[i], p[j]) = (p[j], p[i]);
            }
            for (int i = 0; i < 512; i++)
                _perm[i] = p[i & 255];
        }

        /// <summary>Sum of <paramref name="octaves"/> octaves, each twice the frequency and half the weight, in 0..1.</summary>
        public float Fractal(float x, float y, int octaves)
        {
            float sum = 0f, amplitude = 1f, total = 0f, frequency = 1f;
            for (int o = 0; o < octaves; o++)
            {
                sum += Noise(x * frequency + o * 17.3f, y * frequency + o * 31.7f) * amplitude;
                total += amplitude;
                amplitude *= 0.5f;
                frequency *= 2f;
            }
            return Math.Clamp(sum / total * 0.75f + 0.5f, 0f, 1f);
        }

        /// <summary>One octave, roughly −1..1.</summary>
        private float Noise(float x, float y)
        {
            int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
            float fx = x - x0, fy = y - y0;
            int xi = x0 & 255, yi = y0 & 255;
            float n00 = Dot(_perm[_perm[xi] + yi], fx, fy);
            float n10 = Dot(_perm[_perm[xi + 1] + yi], fx - 1, fy);
            float n01 = Dot(_perm[_perm[xi] + yi + 1], fx, fy - 1);
            float n11 = Dot(_perm[_perm[xi + 1] + yi + 1], fx - 1, fy - 1);
            float u = Fade(fx), v = Fade(fy);
            float a = n00 + (n10 - n00) * u, b = n01 + (n11 - n01) * u;
            return (a + (b - a) * v) * 1.4142f;
        }

        private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

        private static float Dot(int hash, float x, float y)
        {
            var g = Gradients[hash & 7];
            return g.X * x + g.Y * y;
        }
    }

    // ---------------------------------------------------------------- castles and roads

    /// <summary>
    /// The castle's top-left cell somewhere in its corner region: of a few dozen random spots, the one with the most dry
    /// land around it (lakes and mountains are not cleared away into a meadow), with a little luck in the choice.
    /// </summary>
    private static Cell PickStart(Terrain[] tiles, Random rng, bool left, bool top)
    {
        // Corner regions small enough that neighbours never start closer than about 70 tiles.
        const int spanX = 42, spanY = 18;
        int minX = left ? 8 : Width - 8 - CastleSize - spanX, maxX = left ? 8 + spanX : Width - 8 - CastleSize;
        int minY = top ? 7 : Height - 9 - CastleSize - spanY, maxY = top ? 7 + spanY : Height - 9 - CastleSize;
        Cell best = new(minX, minY);
        float bestScore = float.MinValue;
        for (int i = 0; i < 40; i++)
        {
            var c = new Cell(rng.Next(minX, maxX + 1), rng.Next(minY, maxY + 1));
            var center = CenterOf(c, CastleSize);
            float score = 0f;
            for (int dy = -8; dy <= 8; dy++)
                for (int dx = -8; dx <= 8; dx++)
                {
                    int x = (int)center.X + dx, y = (int)center.Y + dy;
                    if (x < 0 || y < 0 || x >= Width || y >= Height || dx * dx + dy * dy > 64)
                        continue;
                    score += tiles[y * Width + x] switch { Terrain.Grass => 1f, Terrain.Forest => 0.6f, _ => -1f };
                }
            score += (float)rng.NextDouble() * 20f; // luck: not always the very best spot
            if (score > bestScore)
            {
                bestScore = score;
                best = c;
            }
        }
        return best;
    }

    private static void Clear(Terrain[] tiles, Vector2 center, float radius)
    {
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                if (Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center) < radius)
                    tiles[y * Width + x] = Terrain.Grass;
    }

    /// <summary>
    /// Lays a road along the cheapest way between two cells (four directions, so bridges run straight): open ground
    /// is cheap, existing roads cheaper, woods dearer, and water (a bridge) and mountains (a pass) dearest of all.
    /// </summary>
    private static void Road(Terrain[] tiles, Cell from, Cell to)
    {
        static float Cost(Terrain t) => t switch
        {
            Terrain.Road => 0.4f,
            Terrain.Grass => 1f,
            Terrain.Forest => 2.5f,
            Terrain.Water => 9f,
            _ => 14f,
        };

        var distance = new float[Width * Height];
        var previous = new int[Width * Height];
        Array.Fill(distance, float.MaxValue);
        Array.Fill(previous, -1);
        var queue = new PriorityQueue<int, float>();
        int start = from.Y * Width + from.X, goal = to.Y * Width + to.X;
        distance[start] = 0f;
        queue.Enqueue(start, 0f);
        int[] dx = { 1, -1, 0, 0 }, dy = { 0, 0, 1, -1 };
        while (queue.TryDequeue(out int current, out float d))
        {
            if (current == goal)
                break;
            if (d > distance[current])
                continue;
            int cx = current % Width, cy = current / Width;
            for (int k = 0; k < 4; k++)
            {
                int nx = cx + dx[k], ny = cy + dy[k];
                if (nx < 1 || ny < 1 || nx >= Width - 1 || ny >= Height - 1)
                    continue;
                int next = ny * Width + nx;
                float nd = d + Cost(tiles[next]);
                if (nd >= distance[next])
                    continue;
                distance[next] = nd;
                previous[next] = current;
                queue.Enqueue(next, nd);
            }
        }
        for (int cell = goal; cell != -1; cell = previous[cell])
            tiles[cell] = Terrain.Road;
    }

    // ---------------------------------------------------------------- gold

    private static Vector2 CenterOf(Cell anchor, int size) => new(anchor.X + size / 2f, anchor.Y + size / 2f);

    private static Cell RandomCell(Random rng) => new(rng.Next(2, Width - 3), rng.Next(2, Height - 3));

    private static Cell Around(Random rng, Vector2 center, float minDistance, float maxDistance)
    {
        double angle = rng.NextDouble() * Math.PI * 2;
        float distance = minDistance + (maxDistance - minDistance) * (float)rng.NextDouble();
        return new Cell((int)(center.X + MathF.Cos((float)angle) * distance) - 1, (int)(center.Y + MathF.Sin((float)angle) * distance) - 1);
    }

    private static Cell? Place(TileGrid grid, Random rng, List<(Cell Anchor, int Size)> taken, int home, Func<Cell> candidate,
        Func<Cell, bool>? where = null)
    {
        for (int i = 0; i < Attempts; i++)
        {
            var anchor = candidate();
            if ((where == null || where(anchor)) && Fits(grid, taken, home, anchor))
                return anchor;
        }
        return null; // no room this time: one deposit fewer
    }

    /// <summary>A deposit fits on open ground (not road), clear of other things, with a walkable neighbour the castles can reach.</summary>
    private static bool Fits(TileGrid grid, List<(Cell Anchor, int Size)> taken, int home, Cell anchor)
    {
        const int size = GoldDeposit.Size;
        if (anchor.X < 1 || anchor.Y < 1 || anchor.X + size >= Width - 1 || anchor.Y + size >= Height - 1)
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

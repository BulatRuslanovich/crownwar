using System.Numerics;
using Lordsio.Sim.Core;

namespace Lordsio.Sim.World;

/// <summary>How a part of the map looks and what grows there: the generator shapes each biome's land differently.</summary>
public enum Biome : byte
{
    Meadow,
    Winter,
    Desert,
}

/// <summary>
/// Makes a random map for 2 to 4 players from a seed, the land from Perlin noise like a Minecraft world: the terrain
/// is not mirrored, so one player may get a river at the door, another a mountain ridge. The start is fair all the
/// same, whatever the number of players. The same seed and player count always give the same map (so lockstep peers
/// only exchange those).
/// <list type="bullet">
/// <item>Size: a square, larger for more players (<see cref="SizeFor"/>).</item>
/// <item>Climate: temperature and humidity noise place the biomes — cold winter, hot and dry desert, temperate
/// meadow — in large natural regions.</item>
/// <item>Land: elevation and moisture noise give mountains on the heights, lakes in the hollows and woods where it
/// is wet, each biome in its own way; rivers wind along the ridges of another noise.</item>
/// <item>Castles: on a circle round the middle, at equal angles, so every castle is as far from its neighbours and
/// from the middle as every other; the layout is turned one of the ways the square looks the same, and each castle
/// takes the driest spot near its place, cleared for the first buildings.</item>
/// <item>Roads: laid by a path search over the land between neighbouring castles and to the middle, avoiding
/// mountains and water where they can, bridging rivers and crossing ridges by passes where they must — so every
/// castle reaches every other.</item>
/// <item>Gold: two deposits by every castle at the same distances; rich ones in the gaps between neighbours (and the
/// middle), as far from one castle as from the next; the rest scattered as it falls, all of it inside the circle the
/// castles stand on, so the corners of the square are no hidden treasure for whoever starts next to them.</item>
/// </list>
/// For the Defense mode the layout turns inside out: the shared citadel in the middle, the players' outposts in a
/// close ring round it, roads in from the middle of every edge (where the waves come from), and the gold beyond the
/// outposts out towards the edges, so growing rich means going out where the horde comes.
/// </summary>
public static class MapGenerator
{
    public const int MinPlayers = 2, MaxPlayers = 4;
    private const int CastleSize = 3;
    private const int RichYield = 5;
    private const int Attempts = 80;
    /// <summary>Open ground cleared around each castle (tiles from its centre).</summary>
    private const float ClearRadius = 7.5f;
    /// <summary>How far castles stand in from the edge of the map (tiles, to their centre).</summary>
    private const int CastleMargin = 14;
    /// <summary>Defense: the citadel's side, and how far the outposts stand from its centre.</summary>
    private const int CitadelSize = 4;
    private const float OutpostRing = 20f;

    /// <summary>The side of the square map for a match of <paramref name="players"/> (more players, more room).</summary>
    public static int SizeFor(int players) => Math.Clamp(players, MinPlayers, MaxPlayers) switch
    {
        2 => 128,
        3 => 144,
        _ => 160,
    };

    /// <param name="players">Sides at the start of the match (players and bots): one castle each.</param>
    /// <param name="mode">Defense lays out a citadel in the middle with the players' outposts round it.</param>
    public static MapDefinition Generate(int seed, int players = MaxPlayers, MatchMode mode = MatchMode.Versus)
    {
        // One random stream for everything, drawn in a fixed order: the same seed gives the same map on every peer.
        var build = new Builder(SizeFor(players), new Random(seed));
        int sides = Math.Clamp(players, MinPlayers, MaxPlayers);
        build.GrowLand();
        build.SeparateBiomes();
        build.JoinDiagonalWater();
        if (mode == MatchMode.Defense)
        {
            var (outposts, citadel) = build.PlaceCitadelAndOutposts(sides);
            var gate = build.LayDefenseRoads(outposts, citadel);
            var defenseGrid = new TileGrid(build.Width, build.Height, build.Tiles);
            var gold = build.PlaceDefenseGold(defenseGrid, outposts, citadel, home: defenseGrid.ComponentOf(gate));
            return new MapDefinition(defenseGrid, Array.Empty<UnitSpawn>(), outposts, gold, build.Biomes, citadel);
        }
        var starts = build.PlaceCastles(sides);
        var doors = build.LayRoads(starts);
        var grid = new TileGrid(build.Width, build.Height, build.Tiles);
        var deposits = build.PlaceGold(grid, starts, home: grid.ComponentOf(doors[0]));
        return new MapDefinition(grid, Array.Empty<UnitSpawn>(), starts, deposits, build.Biomes);
    }

    /// <summary>What a biome grows at a given elevation and moisture (both 0..1, most values near 0.5).</summary>
    private static Terrain Grow(Biome biome, float elevation, float moisture) => biome switch
    {
        Biome.Winter => elevation > 0.65f ? Terrain.Mountain : elevation < 0.30f ? Terrain.Water : moisture > 0.55f ? Terrain.Forest : Terrain.Grass,
        Biome.Desert => elevation > 0.69f ? Terrain.Mountain : elevation < 0.26f ? Terrain.Water
            : moisture > 0.66f || moisture > 0.58f && elevation < 0.37f ? Terrain.Forest : Terrain.Grass, // palms by the oases
        _ => elevation > 0.68f ? Terrain.Mountain : elevation < 0.30f ? Terrain.Water : moisture > 0.57f ? Terrain.Forest : Terrain.Grass,
    };

    /// <summary>One map being made: its size, its random stream, and the land as it takes shape.</summary>
    private sealed class Builder
    {
        private readonly Random _rng;

        public Builder(int size, Random rng)
        {
            Width = Height = size;
            _rng = rng;
            Tiles = new Terrain[Width * Height];
            Biomes = new Biome[Width * Height];
        }

        public int Width { get; }
        public int Height { get; }
        public Terrain[] Tiles { get; }
        public Biome[] Biomes { get; }

        private Vector2 Middle => new(Width / 2f, Height / 2f);

        /// <summary>The circle the castles stand on, round the middle.</summary>
        private float CastleRadius => Width / 2f - CastleMargin;

        // ---------------------------------------------------------------- land

        /// <summary>Climate (biomes) and land: mountains, lakes, woods and rivers, from five noise fields.</summary>
        public void GrowLand()
        {
            var elevation = new Perlin(_rng.Next());
            var moisture = new Perlin(_rng.Next());
            var temperature = new Perlin(_rng.Next());
            var humidity = new Perlin(_rng.Next());
            var rivers = new Perlin(_rng.Next());
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    // Four octaves: the large climate zones, with borders that wind instead of running in long steps.
                    float t = temperature.Fractal(x * 0.014f, y * 0.014f, 4);
                    float h = humidity.Fractal(x * 0.018f, y * 0.018f, 4);
                    var biome = t < 0.42f ? Biome.Winter : t > 0.57f && h < 0.55f ? Biome.Desert : Biome.Meadow;
                    Biomes[y * Width + x] = biome;

                    float e = elevation.Fractal(x * 0.05f, y * 0.05f, 4);
                    float m = moisture.Fractal(x * 0.11f, y * 0.11f, 3);
                    var land = Grow(biome, e, m);
                    // Rivers: where the river noise crosses its middle, on land that is neither mountain nor lake.
                    float r = MathF.Abs(rivers.Fractal(x * 0.022f, y * 0.022f, 2) - 0.5f);
                    float riverWidth = biome == Biome.Desert ? 0.008f : 0.014f;
                    if (land is Terrain.Grass or Terrain.Forest && r < riverWidth)
                        land = Terrain.Water;
                    Tiles[y * Width + x] = land;
                }
            }
        }

        /// <summary>
        /// Woods or ranges of two biomes never touch: a narrow clearing (or a pass) runs between them, which the smooth
        /// blend of the ground then covers, instead of pines and oaks, grey and red rocks meeting on a straight line.
        /// </summary>
        public void SeparateBiomes()
        {
            var cleared = new List<int>();
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    var t = Tiles[y * Width + x];
                    if (t is not (Terrain.Forest or Terrain.Mountain))
                        continue;
                    bool foreign = false;
                    for (int dy = -1; dy <= 1 && !foreign; dy++)
                        for (int dx = -1; dx <= 1 && !foreign; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            foreign = nx >= 0 && ny >= 0 && nx < Width && ny < Height && Tiles[ny * Width + nx] == t
                                      && Biomes[ny * Width + nx] != Biomes[y * Width + x];
                        }
                    if (foreign)
                        cleared.Add(y * Width + x);
                }
            foreach (int i in cleared)
                Tiles[i] = Terrain.Grass;
        }

        /// <summary>
        /// Water that only touches diagonally (a river stepping across the grid) is joined up, so rivers run as one
        /// channel instead of a chain of ponds.
        /// </summary>
        public void JoinDiagonalWater()
        {
            for (int y = 0; y < Height - 1; y++)
                for (int x = 0; x < Width - 1; x++)
                {
                    bool a = Tiles[y * Width + x] == Terrain.Water, b = Tiles[y * Width + x + 1] == Terrain.Water;
                    bool c = Tiles[(y + 1) * Width + x] == Terrain.Water, d = Tiles[(y + 1) * Width + x + 1] == Terrain.Water;
                    if (a && d && !b && !c)
                        Tiles[y * Width + x + 1] = Terrain.Water;
                    else if (b && c && !a && !d)
                        Tiles[y * Width + x] = Terrain.Water;
                }
        }

        // ---------------------------------------------------------------- castles and roads

        /// <summary>
        /// The castles' places, in order round the circle: equal angles from a start turned one of the ways that leave
        /// the square unchanged (two players face each other across a diagonal, three stand as a triangle pointing at a
        /// side, four in the corners), each castle at the driest spot near its place. Then cleared.
        /// </summary>
        public List<Cell> PlaceCastles(int players)
        {
            var starts = new List<Cell>();
            foreach (var place in RingPlaces(players, CastleRadius))
                starts.Add(PickStart(place, jitter: 4));
            foreach (var start in starts)
                Clear(start.CenterOfSquare(CastleSize), ClearRadius);
            return starts;
        }

        /// <summary>
        /// Points on a circle of <paramref name="radius"/> round the middle at equal angles, turned one of the ways that
        /// leave the square unchanged: two face each other across a diagonal, three stand as a triangle pointing at a
        /// side, four in the corners.
        /// </summary>
        private List<Vector2> RingPlaces(int count, float radius)
        {
            float quarterTurns = _rng.Next(4) * MathF.PI / 2f;
            float first = count switch
            {
                3 => -MathF.PI / 2f, // a corner of the triangle points straight at a side
                _ => MathF.PI / 4f, // along the diagonals
            } + quarterTurns;
            var places = new List<Vector2>();
            for (int i = 0; i < count; i++)
            {
                float angle = first + i * MathF.Tau / count;
                places.Add(Middle + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius);
            }
            return places;
        }

        /// <summary>Defense: the citadel in the very middle, the outposts in a close ring round it; all cleared.</summary>
        public (List<Cell> Outposts, Cell Citadel) PlaceCitadelAndOutposts(int players)
        {
            var citadel = new Cell(Width / 2 - CitadelSize / 2, Height / 2 - CitadelSize / 2);
            Clear(Middle, ClearRadius + 3f);
            var outposts = new List<Cell>();
            foreach (var place in RingPlaces(players, OutpostRing))
                outposts.Add(PickStart(place, jitter: 1));
            foreach (var outpost in outposts)
                Clear(outpost.CenterOfSquare(CastleSize), ClearRadius);
            return (outposts, citadel);
        }

        /// <summary>
        /// Defense roads: from every outpost to the citadel's gate, and from the middle of every edge to it, so the
        /// waves always find a way in (over bridges and passes where they must). Returns the gate.
        /// </summary>
        public Cell LayDefenseRoads(List<Cell> outposts, Cell citadel)
        {
            var gate = new Cell(citadel.X + CitadelSize / 2, citadel.Y + CitadelSize + 1);
            foreach (var outpost in outposts)
                Road(new Cell(outpost.X + 1, outpost.Y + CastleSize + 1), gate);
            const int inset = 2;
            foreach (var edge in new[]
                     {
                         new Cell(Width / 2, inset), new Cell(Width - 1 - inset, Height / 2),
                         new Cell(Width / 2, Height - 1 - inset), new Cell(inset, Height / 2),
                     })
                Road(edge, gate);
            return gate;
        }

        /// <summary>
        /// Defense gold: two deposits by every outpost at the same distances (the safe ones); rich ones out on a ring
        /// in the gaps between the outposts' directions; and more scattered between the outposts' ring and the edge,
        /// so the riches lie out where the waves come in.
        /// </summary>
        public List<DepositSpawn> PlaceDefenseGold(TileGrid grid, List<Cell> outposts, Cell citadel, int home)
        {
            var taken = outposts.Select(s => (Anchor: new Cell(s.X - 1, s.Y - 1), Size: CastleSize + 3)).ToList();
            taken.Add((new Cell(citadel.X - 2, citadel.Y - 2), CitadelSize + 4));
            var deposits = new List<DepositSpawn>();
            var castles = outposts.Select(s => s.CenterOfSquare(CastleSize)).ToList();
            float edgeRadius = Width / 2f - 4f;
            float FromMiddle(Cell c) => Vector2.Distance(new Vector2(c.X + 1, c.Y + 1), Middle);
            bool OutOfTheRing(Cell c) => FromMiddle(c) >= OutpostRing + 10f && FromMiddle(c) <= edgeRadius;
            float Nearest(Cell c) => castles.Min(k => Vector2.Distance(k, new Vector2(c.X + 1, c.Y + 1)));

            void Add(Cell? anchor, int yield)
            {
                if (anchor is not { } a)
                    return;
                taken.Add((new Cell(a.X - 2, a.Y - 2), GoldDeposit.Size + 4));
                deposits.Add(new DepositSpawn(a, yield));
            }

            Cell? Near(Vector2 center, float min, float max, Func<Cell, bool>? where = null) =>
                Place(grid, taken, home, () => Around(center, min, max), where);

            // Every outpost's own gold, on its outer side (away from the citadel): the same for everyone.
            foreach (var center in castles)
            {
                var outward = Vector2.Normalize(center - Middle);
                Add(Near(center + outward * 8f, 0f, 4f) ?? Near(center, 6f, 12f), MapLoader.DefaultDepositYield);
                Add(Near(center + outward * 15f, 0f, 4f) ?? Near(center, 12f, 18f), MapLoader.DefaultDepositYield);
            }

            // Rich deposits out on a ring, in the gaps between the outposts' directions.
            float angle0 = MathF.Atan2(castles[0].Y - Middle.Y, castles[0].X - Middle.X);
            float richRadius = OutpostRing + (edgeRadius - OutpostRing) * 0.55f;
            int gaps = castles.Count == 2 ? 2 : castles.Count;
            for (int i = 0; i < gaps; i++)
            {
                float angle = angle0 + (i + 0.5f) * MathF.Tau / castles.Count;
                var point = Middle + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * richRadius;
                Add(Near(point, 0f, 5f) ?? Near(point, 4f, 9f), RichYield);
            }

            // The rest beyond the outposts, out towards the edge.
            int scattered = castles.Count * _rng.Next(5, 8);
            for (int i = 0; i < scattered; i++)
                Add(Place(grid, taken, home, RandomCell, c => OutOfTheRing(c) && Nearest(c) > 12f), MapLoader.DefaultDepositYield);
            return deposits;
        }

        /// <summary>
        /// The castle's top-left cell close to <paramref name="place"/>: of a few dozen spots around it, the one with the
        /// most dry land around (lakes and mountains are not cleared away into a meadow), with a little luck in the choice.
        /// </summary>
        private Cell PickStart(Vector2 place, int jitter)
        {
            var ideal = new Cell((int)(place.X - CastleSize / 2f), (int)(place.Y - CastleSize / 2f));
            Cell best = ideal;
            float bestScore = float.MinValue;
            for (int i = 0; i < 40; i++)
            {
                var c = new Cell(ideal.X + _rng.Next(-jitter, jitter + 1), ideal.Y + _rng.Next(-jitter, jitter + 1));
                var center = c.CenterOfSquare(CastleSize);
                float score = 0f;
                for (int dy = -8; dy <= 8; dy++)
                    for (int dx = -8; dx <= 8; dx++)
                    {
                        int x = (int)center.X + dx, y = (int)center.Y + dy;
                        if (x < 0 || y < 0 || x >= Width || y >= Height || dx * dx + dy * dy > 64)
                            continue;
                        score += Tiles[y * Width + x] switch { Terrain.Grass => 1f, Terrain.Forest => 0.6f, _ => -1f };
                    }
                score += (float)_rng.NextDouble() * 8f; // luck: not always the very best spot
                if (score > bestScore)
                {
                    bestScore = score;
                    best = c;
                }
            }
            return best;
        }

        private void Clear(Vector2 center, float radius)
        {
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                    if (Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center) < radius)
                        Tiles[y * Width + x] = Terrain.Grass;
        }

        /// <summary>Roads between neighbouring castles (round the circle) and from each castle to the middle; returns the castle doors.</summary>
        public List<Cell> LayRoads(List<Cell> starts)
        {
            var middle = new Cell(Width / 2, Height / 2);
            var doors = starts.Select(s => new Cell(s.X + 1, s.Y + CastleSize + 1)).ToList();
            // Two castles are each other's only neighbour: one road between them is enough.
            int links = doors.Count == 2 ? 1 : doors.Count;
            for (int i = 0; i < links; i++)
                Road(doors[i], doors[(i + 1) % doors.Count]);
            foreach (var door in doors)
                Road(door, middle);
            return doors;
        }

        /// <summary>
        /// Lays a road along the cheapest way between two cells (four directions, so bridges run straight): open ground
        /// is cheap, existing roads cheaper, woods dearer, and water (a bridge) and mountains (a pass) dearest of all.
        /// </summary>
        private void Road(Cell from, Cell to)
        {
            static float Cost(Terrain t) => t switch
            {
                Terrain.Road or Terrain.Bridge => 0.4f,
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
                    float nd = d + Cost(Tiles[next]);
                    if (nd >= distance[next])
                        continue;
                    distance[next] = nd;
                    previous[next] = current;
                    queue.Enqueue(next, nd);
                }
            }
            // Across water the road is a bridge.
            for (int cell = goal; cell != -1; cell = previous[cell])
                Tiles[cell] = Tiles[cell] is Terrain.Water or Terrain.Bridge ? Terrain.Bridge : Terrain.Road;
        }

        // ---------------------------------------------------------------- gold

        /// <summary>
        /// Two deposits by every castle at the same distances (one close, one a little further); a rich one in every gap
        /// between neighbouring castles and, for three or four players, in the middle — as far from each of them as from
        /// the others; and some more per player scattered as they fall. All of it inside the castles' circle.
        /// </summary>
        public List<DepositSpawn> PlaceGold(TileGrid grid, List<Cell> starts, int home)
        {
            var taken = starts.Select(s => (Anchor: new Cell(s.X - 1, s.Y - 1), Size: CastleSize + 3)).ToList();
            var deposits = new List<DepositSpawn>();
            var castles = starts.Select(s => s.CenterOfSquare(CastleSize)).ToList();
            float fairRadius = Width / 2f - 3f;
            bool InCircle(Cell c) => Vector2.Distance(new Vector2(c.X + 1, c.Y + 1), Middle) <= fairRadius;
            float Nearest(Cell c) => castles.Min(k => Vector2.Distance(k, new Vector2(c.X + 1, c.Y + 1)));

            void Add(Cell? anchor, int yield)
            {
                if (anchor is not { } a)
                    return;
                taken.Add((new Cell(a.X - 2, a.Y - 2), GoldDeposit.Size + 4));
                deposits.Add(new DepositSpawn(a, yield));
            }

            Cell? Near(Vector2 center, float min, float max, Func<Cell, bool>? where = null) =>
                Place(grid, taken, home, () => Around(center, min, max), where);

            // Every castle's own gold, near and a little further: the same for everyone.
            foreach (var center in castles)
            {
                Add(Near(center, 6f, 10f, InCircle) ?? Near(center, 10f, 14f, InCircle) ?? Near(center, 6f, 14f), MapLoader.DefaultDepositYield);
                Add(Near(center, 12f, 18f, InCircle) ?? Near(center, 16f, 22f, InCircle), MapLoader.DefaultDepositYield);
            }

            // Rich deposits worth fighting over: midway along each gap between neighbours, and in the middle.
            int gaps = castles.Count == 2 ? 2 : castles.Count;
            float angle0 = MathF.Atan2(castles[0].Y - Middle.Y, castles[0].X - Middle.X);
            for (int i = 0; i < gaps; i++)
            {
                float angle = angle0 + (i + 0.5f) * MathF.Tau / castles.Count;
                var point = Middle + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * CastleRadius * 0.6f;
                Add(Near(point, 0f, 5f) ?? Near(point, 4f, 9f), RichYield);
            }
            if (castles.Count >= 3)
                Add(Near(Middle, 0f, 4f) ?? Near(Middle, 4f, 9f), RichYield);

            // The rest as it falls, a few per player, away from the castles.
            int scattered = castles.Count * _rng.Next(5, 8);
            for (int i = 0; i < scattered; i++)
                Add(Place(grid, taken, home, RandomCell, c => InCircle(c) && Nearest(c) > 12f), MapLoader.DefaultDepositYield);
            return deposits;
        }

        private Cell RandomCell() => new(_rng.Next(2, Width - 3), _rng.Next(2, Height - 3));

        private Cell Around(Vector2 center, float minDistance, float maxDistance)
        {
            double angle = _rng.NextDouble() * Math.PI * 2;
            float distance = minDistance + (maxDistance - minDistance) * (float)_rng.NextDouble();
            return new Cell((int)(center.X + MathF.Cos((float)angle) * distance) - 1, (int)(center.Y + MathF.Sin((float)angle) * distance) - 1);
        }

        private Cell? Place(TileGrid grid, List<(Cell Anchor, int Size)> taken, int home, Func<Cell> candidate, Func<Cell, bool>? where = null)
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
        private bool Fits(TileGrid grid, List<(Cell Anchor, int Size)> taken, int home, Cell anchor)
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
}

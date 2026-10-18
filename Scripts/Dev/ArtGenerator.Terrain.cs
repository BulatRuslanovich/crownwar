using System;
using Godot;
using Warcrown.World;

namespace Warcrown.Dev;

/// <summary>The terrain sheet: grass, water, shores, roads and bridges, woods and ranges, biome blends, decor.</summary>
public static partial class ArtGenerator
{
    /// <summary>
    /// The materials of one biome's terrain: open ground, the canopy of its woods, bark, rock and what caps the peaks,
    /// road dirt, the sand of its shores; and the colour of grass tufts. Each biome's sheet is drawn with its own. The
    /// colours are muted, earthy and a little dark, to sit with the grim figures and stone on them, yet light and even
    /// enough that soldiers stand out against them.
    /// </summary>
    private sealed record TerrainPalette(string Name, Ramp Ground, Ramp Leaf, Ramp Bark, Ramp Rock, Ramp Cap, Ramp Dirt, Ramp Sand,
        Color Tuft, bool Conifers = false, bool Palms = false)
    {
        public bool Snow => Conifers;
    }

    private static readonly TerrainPalette MeadowLand = new("meadow", Ramp.Of(new Color("5b7e3e"), 0.7f), Ramp.Of(new Color("355f35")),
        Ramp.Of(new Color("553722")), Ramp.Of(new Color("76716b")), Ramp.Of(new Color("e8edf0"), 0.5f), Ramp.Of(new Color("8b7150"), 0.75f),
        Ramp.Of(new Color("bba878"), 0.7f), new Color("94b463"));

    private static readonly TerrainPalette WinterLand = new("winter", Ramp.Of(new Color("d3dce4"), 0.45f), Ramp.Of(new Color("28503f")),
        Ramp.Of(new Color("443226")), Ramp.Of(new Color("7e8086")), Ramp.Of(new Color("f2f6f8"), 0.4f), Ramp.Of(new Color("8a8178"), 0.7f),
        Ramp.Of(new Color("b6c4ce"), 0.5f), new Color("eef3f6"), Conifers: true);

    private static readonly TerrainPalette DesertLand = new("desert", Ramp.Of(new Color("cba672"), 0.55f), Ramp.Of(new Color("56783a")),
        Ramp.Of(new Color("6e4a2c")), Ramp.Of(new Color("9b6a45")), Ramp.Of(new Color("e3c497"), 0.5f), Ramp.Of(new Color("a5824f"), 0.7f),
        Ramp.Of(new Color("dfc790"), 0.55f), new Color("dec089"), Palms: true);

    /// <summary>Water: the same in every biome, so a lake or a river across a biome border is one sheet of water.</summary>
    private static readonly Ramp WaterR = Ramp.Of(new Color("2b5a84"), 0.75f);
    private static readonly Color FoamColor = new("d9e7ea");

    /// <summary>The biome being drawn.</summary>
    private static TerrainPalette Land = MeadowLand;

    private const int T = ArtLayout.Tile;

    /// <summary>
    /// 32×32 tiles, 16 columns. Row 0 grass, 1 a lone tree, 3 a lone mountain (4 variants each); row 4 roads, by a 4-bit
    /// neighbour mask N=1, E=2, S=4, W=8; row 6 bridge decks over water (transparent around them) by the mask of the
    /// road or bridge cells they join; rows 11–26 mountain ranges, by neighbour mask (column) and the cell's position
    /// modulo 4 in each direction (row); rows 27–42 this biome's ground reaching into a neighbouring cell of another
    /// biome, by the 8-bit mask of its neighbours of this biome (row = mask / 16, column = mask % 16).
    /// Animated tiles keep their frames in the rows under the first one (<see cref="ArtLayout.TileFrames"/>): water
    /// (4 variants) from row 43, and woods by mask and by the cell's position modulo 2 from row 51 (four positions, each
    /// with its frames). Rows 67–68 hold the decor (one kind a column); from row 69, the shores of water cells by the
    /// 8-bit mask of the land around them, and from row 133 the coasts of land cells by the mask of the water around
    /// them (sixteen masks a row, each row followed by its frames).
    /// Rows 2, 5, 7–10 and 47–50 are free.
    /// </summary>
    private static PixelCanvas Terrain(TerrainPalette land)
    {
        Land = land;
        var c = new PixelCanvas(T * 16, T * ArtLayout.TerrainRows);
        for (int v = 0; v < ArtLayout.TerrainVariants; v++)
        {
            Cell(c, v, ArtLayout.GrassRow); GrassTile(c, v);
            Cell(c, v, ArtLayout.ForestRow); GrassTile(c, v + 10); TreeTile(c, v);
            Cell(c, v, ArtLayout.MountainRow); MountainTile(c, v);
            for (int frame = 0; frame < ArtLayout.TileFrames; frame++)
            {
                Cell(c, v, ArtLayout.WaterRow + frame);
                WaterTile(c, v, frame);
            }
        }
        for (int mask = 0; mask < 16; mask++)
        {
            Cell(c, mask, ArtLayout.RoadRow); GrassTile(c, mask + 20); RoadTile(c, mask);
            Cell(c, mask, ArtLayout.BridgeRow); BridgeTile(c, mask);
            for (int frame = 0; frame < ArtLayout.TileFrames; frame++)
            {
                for (int parity = 0; parity < 4; parity++)
                {
                    Cell(c, mask, ArtLayout.ForestRangeRow + parity * ArtLayout.TileFrames + frame);
                    ForestRangeTile(c, mask, parity, frame);
                }
            }
            for (int parity = 0; parity < 16; parity++)
            {
                Cell(c, mask, ArtLayout.MountainRangeRow + parity);
                MountainRangeTile(c, mask, parity);
            }
        }
        for (int mask = 0; mask < 256; mask++)
        {
            Cell(c, mask % 16, ArtLayout.BiomeBlendRow + mask / 16);
            BiomeBlendTile(c, mask);
            for (int frame = 0; frame < ArtLayout.TileFrames; frame++)
            {
                var shore = ArtLayout.ShoreTile(mask);
                Cell(c, shore.X, shore.Y + frame);
                CoastTile(c, mask, frame, onLand: false);
                var coast = ArtLayout.CoastTile(mask);
                Cell(c, coast.X, coast.Y + frame);
                CoastTile(c, mask, frame, onLand: true);
            }
        }
        for (int kind = 0; kind < ArtLayout.DecorKinds; kind++)
        {
            Cell(c, kind % 16, ArtLayout.DecorRow + kind / 16);
            DecorTile(c, kind);
        }
        return c;

        static void Cell(PixelCanvas canvas, int col, int row)
        {
            canvas.Origin = new Vector2I(col * T, row * T);
            canvas.Clip = new Rect2I(canvas.Origin, new Vector2I(T, T));
        }
    }

    /// <summary>Smooth value noise in 0..1 that repeats every tile, so neighbouring tiles of any variant meet without a seam.</summary>
    private static float TileNoise(float x, float y, float cell, int salt)
    {
        int n = (int)(T / cell);
        float gx = x / cell, gy = y / cell;
        int x0 = (int)Mathf.Floor(gx), y0 = (int)Mathf.Floor(gy);
        float fx = gx - x0, fy = gy - y0;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        float V(int i, int j) => Hash(((i % n) + n) % n, ((j % n) + n) % n, salt) / 999f;
        return Mathf.Lerp(Mathf.Lerp(V(x0, y0), V(x0 + 1, y0), fx), Mathf.Lerp(V(x0, y0 + 1), V(x0 + 1, y0 + 1), fx), fy);
    }

    /// <summary>
    /// Open ground: broad soft patches of lighter and darker ground (the same on every tile, so they run on across
    /// tiles), then the variant's own texture: short blades over the lusher patches and a few tufts fanning out over a
    /// dab of shadow in a meadow, ripples and a glitter of frost in the snow, wind ripples and grit in the sand.
    /// </summary>
    private static void GrassTile(PixelCanvas c, int v)
    {
        var g = Land.Ground;
        for (int y = 0; y < T; y++)
            for (int x = 0; x < T; x++)
            {
                // Rounded to a tone with a grain of noise rather than dithered: the patches get ragged, organic edges.
                float patch = (TileNoise(x, y, 16f, 3) - 0.5f) * 1.1f + (TileNoise(x, y, 8f, 4) - 0.5f) * 0.55f - 0.05f
                              + (Hash(x, y, 2) % 100 / 100f - 0.5f) * 0.4f;
                c.Px(x, y, g[(int)Mathf.Round(Mathf.Clamp(patch, -1.2f, 1f))], solid: false);
            }
        if (Land.Snow || Land.Palms)
        {
            // Now and then a ripple: a soft arc lit on its windward side, its shadow only under the crest; and a
            // glint of frost or of a grain of quartz.
            for (int r = 0; r < Hash(v, 5, 24) % 2; r++)
            {
                int length = 7 + Hash(r, v, 23) % 7;
                int y0 = 4 + Hash(r, v, 21) % 25, x0 = 1 + Hash(v, r, 22) % (T - length - 2);
                for (int i = 0; i < length; i++)
                {
                    int y = y0 + (int)Mathf.Round(Mathf.Sin(i * 0.45f + r) * 1.2f);
                    bool crest = i > length / 4 && i < length * 3 / 4;
                    c.Px(x0 + i, y, crest ? g.Light : g.Base, solid: false);
                    if (crest)
                        c.Px(x0 + i, y + 1, g.Shade, solid: false);
                }
            }
            for (int k = 0; k < 4; k++)
            {
                int x = 1 + Hash(k, v, 25) % (T - 2), y = 1 + Hash(v, k, 26) % (T - 2);
                c.Px(x, y, Land.Snow ? g.Glint : Land.Rock.Light, solid: false);
                if (Land.Palms)
                    c.Px(x + 1, y, Land.Rock.Shade, solid: false);
            }
            return;
        }
        // Short blades scattered over the lusher patches: a lit tip over a dark root.
        for (int y = 1; y < T; y++)
            for (int x = 0; x < T; x++)
            {
                float lush = TileNoise(x, y, 8f, 5);
                int h = Hash(x, y, v * 7 + 1);
                if (h >= 8 + lush * 40f)
                    continue;
                c.Px(x, y, g.Shade, solid: false);
                c.Px(x, y - 1, h % 3 == 0 ? Land.Tuft : g.Light, solid: false);
            }
        // Tufts: blades fanning out of a dab of shadow, the ones on the sunny side lit.
        for (int t = 0; t < 4; t++)
        {
            int x = 3 + Hash(t, v, 7) % 26, y = 6 + Hash(v, t, 9) % 23;
            for (int i = -2; i <= 3; i++)
                c.Px(x + i, y + 1, g.Deep with { A = Mathf.Abs(i - 0.5f) > 2f ? 0.25f : 0.5f }, solid: false);
            int blades = 3 + Hash(t, v, 13) % 3;
            float mid = (blades - 1) / 2f;
            for (int b = 0; b < blades; b++)
            {
                int tall = 3 + Hash(t * 5 + b, v, 11) % 3;
                for (int i = 0; i < tall; i++)
                {
                    float k = i / (float)(tall - 1);
                    int px = x + (int)Mathf.Round((b - mid) * 1.2f * (0.4f + 0.6f * k));
                    var color = i == tall - 1 ? Land.Tuft : i == 0 ? g.Deep : b < mid ? g.Light : g.Shade;
                    c.Px(px, y - i, color, solid: false);
                }
            }
        }
    }

    /// <summary>
    /// A leafy crown: a ring of clumps round a high middle one, so the outline billows; each clump lit on its upper left
    /// and shadowed where it tucks under the next, the whole lit as one ball, and broken into small leaves.
    /// </summary>
    private static void Crown(PixelCanvas c, float cx, float cy, float r, int salt, int sway, Func<int, int, bool>? clip = null)
    {
        var leaf = Land.Leaf;
        var clumps = new System.Collections.Generic.List<(float X, float Y, float R)>();
        float turn = Hash(salt, 1, 41) % 60 * Mathf.Pi / 180f;
        for (int k = 0; k < 6; k++)
        {
            float a = turn + k * Mathf.Tau / 6f;
            float d = r * (0.46f + Hash(salt, k, 43) % 10 * 0.012f);
            clumps.Add((cx + Mathf.Cos(a) * d, cy + Mathf.Sin(a) * d * 0.9f, r * (0.5f + Hash(k, salt, 44) % 10 * 0.012f)));
        }
        clumps.Sort((p, q) => p.Y.CompareTo(q.Y));
        clumps.Add((cx - r * 0.1f, cy - r * 0.16f, r * 0.6f)); // the top of the crown, nearest the sun, over the rest
        foreach (var k in clumps)
        {
            for (int y = (int)Mathf.Floor(k.Y - k.R); y <= (int)Mathf.Ceil(k.Y + k.R); y++)
                for (int x = (int)Mathf.Floor(k.X - k.R); x <= (int)Mathf.Ceil(k.X + k.R); x++)
                {
                    float nx = (x + 0.5f - k.X) / k.R, ny = (y + 0.5f - k.Y) / k.R;
                    float d = nx * nx + ny * ny;
                    // Leaves breaking the clump's rim.
                    float rim = 1f + (Hash(x, y, salt + 3) % 5 - 2) * 0.06f;
                    if (d > rim)
                        continue;
                    int px = x + (y < cy - r * 0.2f ? sway : 0);
                    if (clip != null && !clip(px, y))
                        continue;
                    float nz = Mathf.Sqrt(Mathf.Max(0f, 1f - d));
                    float local = (-0.55f * nx - 0.62f * ny + 0.56f * nz) * 2.4f - 0.5f;
                    float gx = (x + 0.5f - cx) / r, gy = (y + 0.5f - cy) / r;
                    float global = -0.9f * gx - 1.1f * gy;
                    float leaves = (Hash(x / 2, y / 2, salt) % 5 - 2) * 0.22f;
                    float light = local * 0.55f + global * 0.6f + leaves - 0.15f;
                    if (d > 0.72f && nx + ny > 0.2f)
                        light = Mathf.Min(light, -1.4f); // where the clump tucks under its neighbour
                    c.Shade(px, y, leaf, light, solid: false);
                }
        }
    }

    /// <summary>A fir: tiers of drooping boughs, each lit on the left, snow along their tops in the winter.</summary>
    private static void Fir(PixelCanvas c, float cx, float top, float height, int salt, int sway, Func<int, int, bool>? clip = null)
    {
        var leaf = Land.Leaf;
        int tiers = 4;
        for (int t = 0; t < tiers; t++)
        {
            float ty = top + t * height / (tiers + 1);
            float half = 3.5f + t * height / 6.5f;
            for (int y = (int)ty; y < (int)(ty + height / 3f); y++)
            {
                float k = (y - ty) / (height / 3f);
                float w = half * (0.35f + 0.65f * k);
                for (int x = (int)Mathf.Floor(cx - w); x <= (int)Mathf.Ceil(cx + w); x++)
                {
                    int px = x + (t == 0 ? sway : 0);
                    if (clip != null && !clip(px, y))
                        continue;
                    float across = (x + 0.5f - (cx - w)) / (2f * w);
                    if (across < 0f || across > 1f)
                        continue;
                    bool snow = Land.Conifers && k < 0.3f && Hash(x, t, salt) % 3 != 0;
                    if (snow)
                        c.Shade(px, y, Land.Cap, across < 0.5f ? 0.8f : -0.4f, solid: false);
                    else
                        c.Shade(px, y, leaf, 1f - 3f * Mathf.Abs(across - 0.3f) - k * 0.6f + (k > 0.85f ? -0.8f : 0f), solid: false);
                }
            }
        }
    }

    /// <summary>A palm: a curved trunk and a burst of drooping fronds.</summary>
    private static void Palm(PixelCanvas c, float cx, float baseY, float height, int salt, int sway, Func<int, int, bool>? clip = null)
    {
        void Plot(int x, int y, Ramp r, float light)
        {
            if (clip == null || clip(x, y))
                c.Shade(x, y, r, light, solid: false);
        }
        float lean = Hash(salt, 1, 3) % 2 == 0 ? 3f : -3f;
        for (int i = 0; i < (int)height; i++)
        {
            float t = i / height;
            int x = (int)(cx + lean * t * t), y = (int)(baseY - i);
            Plot(x, y, Land.Bark, i % 3 == 0 ? -1f : 0.5f);
            Plot(x + 1, y, Land.Bark, -0.6f);
        }
        float topX = cx + lean + sway, topY = baseY - height;
        for (int f = 0; f < 7; f++)
        {
            float a = -Mathf.Pi + f * Mathf.Pi / 6f;
            for (int i = 0; i < 9; i++)
            {
                float x = topX + Mathf.Cos(a) * i, y = topY + Mathf.Sin(a) * i * 0.6f + i * i * 0.07f;
                Plot((int)x, (int)y, Land.Leaf, f < 3 ? 0.8f - i * 0.1f : -0.2f - i * 0.1f);
                Plot((int)x, (int)y + 1, Land.Leaf, -1.2f);
            }
        }
    }

    /// <summary>A lone tree (or fir, or palm) with its shadow on the grass.</summary>
    private static void TreeTile(PixelCanvas c, int v)
    {
        float cx = 16 + (v % 2 == 0 ? -2 : 2), cy = 12 + v % 2 * 2;
        c.GroundShadow(cx + 4, 26f, 11f, 4f, 0.3f);
        if (Land.Conifers)
        {
            c.Stroke(cx, 24, cx, 29, 2.4f, Land.Bark);
            Fir(c, cx, 2 + v % 2 * 2, 24, v, 0);
            return;
        }
        if (Land.Palms)
        {
            Palm(c, cx, 28, 20, v, 0);
            return;
        }
        c.Stroke(cx, 18, cx - 0.5f, 28, 3.2f, Land.Bark);
        Crown(c, cx, cy, v == 2 ? 9f : 10.5f, v, 0);
    }

    /// <summary>
    /// The neighbourhood of a forest or mountain tile: which of the 3×3 tiles around it (−1..1) are of the same kind
    /// (its own, the four by the mask, a diagonal one when both tiles beside it are), and where the pattern's lattice
    /// lies — lattice points jittered by their place in the repeating period, so both tiles that draw a crown or a peak
    /// across their border draw it the same.
    /// </summary>
    private readonly struct Range
    {
        private readonly int _mask, _px, _py, _period;

        /// <param name="period">Tiles before the pattern repeats in each direction; <paramref name="parity"/> is the
        /// cell's position within it (x + y × period).</param>
        public Range(int mask, int parity, int period = 2)
        {
            _mask = mask;
            _period = period;
            _px = parity % period;
            _py = parity / period;
        }

        public bool Present(int tx, int ty) => (tx, ty) switch
        {
            (0, 0) => true,
            (0, -1) => (_mask & 1) != 0,
            (1, 0) => (_mask & 2) != 0,
            (0, 1) => (_mask & 4) != 0,
            (-1, 0) => (_mask & 8) != 0,
            _ when tx is < -1 or > 1 || ty is < -1 or > 1 => true, // beyond what this tile knows: assume more of it
            _ => Present(tx, 0) && Present(0, ty),
        };

        /// <summary>
        /// Lattice points owned by the present tiles, every <paramref name="spacing"/> px (16: four a tile, 32: one, with
        /// every other row of tiles shifted half a peak sideways): (x, y) in this tile's pixels, and a hash for their look.
        /// </summary>
        public System.Collections.Generic.IEnumerable<(float X, float Y, int Hash)> Points(int spacing)
        {
            int perTile = T / spacing;
            for (int ty = -1; ty <= 1; ty++)
                for (int tx = -1; tx <= 1; tx++)
                {
                    if (!Present(tx, ty))
                        continue;
                    bool shifted = perTile == 1 && ((_py + ty) % 2 + 2) % 2 == 1;
                    for (int j = 0; j < perTile; j++)
                        for (int i = 0; i < perTile; i++)
                        {
                            int span = _period * perTile;
                            int gx = (((_px + tx) * perTile + i) % span + span) % span, gy = (((_py + ty) * perTile + j) % span + span) % span;
                            int h = Hash(gx, gy, 61 + spacing);
                            // Big peaks wander more than small crowns.
                            int jitter = perTile == 1 ? 6 : 2;
                            float x = tx * T + spacing / 2 + spacing * i + h % (2 * jitter + 1) - jitter + (shifted ? 8 : -4);
                            float y = ty * T + spacing / 2 + spacing * j + h / 10 % (2 * jitter + 1) - jitter;
                            yield return (x, y, h);
                        }
                }
        }

        /// <summary>How far a shape centred at (x, y) may reach before it would spill onto a tile of another kind.</summary>
        public (float Left, float Right, float Up, float Down) Room(float x, float y)
        {
            int tx = (int)Mathf.Floor(x / T), ty = (int)Mathf.Floor(y / T);
            float left = Present(tx - 1, ty) ? 99f : x - tx * T;
            float right = Present(tx + 1, ty) ? 99f : (tx + 1) * T - 1 - x;
            float up = Present(tx, ty - 1) ? 99f : y - ty * T;
            float down = Present(tx, ty + 1) ? 99f : (ty + 1) * T - 1 - y;
            return (left, right, up, down);
        }
    }

    private static bool InTile(int x, int y) => x >= 0 && y >= 0 && x < T && y < T;

    private static void Plot(PixelCanvas c, int x, int y, Color color)
    {
        if (InTile(x, y))
            c.Px(x, y, color, solid: false);
    }

    /// <summary>
    /// Woods as one canopy: leafy crowns (firs in the snow, palms in the desert) on the lattice, drawn from the back row
    /// to the front so each overlaps the one behind, lit from the top-left, with their shadow on the ground; along the
    /// open southern edge the trunks show. Crowns near an open edge are smaller, so the wood ends in a rounded fringe.
    /// </summary>
    /// <remarks>Through the frames the crowns' tops lean with the wind and back: a pixel or two downwind, upright, upwind.</remarks>
    private static void ForestRangeTile(PixelCanvas c, int mask, int parity, int frame)
    {
        int sway = frame switch { 1 => 1, 3 => -1, _ => 0 };
        var range = new Range(mask, parity);
        GrassTile(c, 30 + parity);
        var crowns = new System.Collections.Generic.List<(float X, float Y, float R, int Hash)>();
        foreach (var (x, y, h) in range.Points(16))
        {
            var room = range.Room(x, y);
            float open = Mathf.Min(Mathf.Min(room.Left, room.Right), Mathf.Min(room.Up, room.Down - 4f));
            float r = 8.8f + h % 4 * 0.9f;
            if (open < r + 1f)
            {
                // On the wood's edge the crowns stand back from it unevenly, and some are missing: a ragged fringe.
                if (h % 4 == 0)
                    continue;
                r = Mathf.Min(r, open - 1f - h / 7 % 4);
            }
            if (r >= 4.4f)
                crowns.Add((x, y, r, h));
        }
        crowns.Sort((a, b) => a.Y.CompareTo(b.Y));

        bool InCrown(int px, int py, float dx, float dy) =>
            crowns.Exists(k => (px + 0.5f - k.X - dx) * (px + 0.5f - k.X - dx) + (py + 0.5f - k.Y - dy) * (py + 0.5f - k.Y - dy) <= k.R * k.R);
        // Shadows on the ground, below and to the right of the canopy.
        for (int y = 0; y < T; y++)
            for (int x = 0; x < T; x++)
                if (!InCrown(x, y, 0f, 0f) && InCrown(x, y, 2f, 5f))
                    c.Px(x, y, Land.Ground.Deep with { A = 0.75f }, solid: false);
        // Trunks under the crowns (only those at the front edge stay visible).
        foreach (var k in crowns)
            for (int ty = 0; ty < 6; ty++)
            {
                Plot(c, (int)k.X - 1, (int)(k.Y + k.R) + ty - 2, Land.Bark.Base);
                Plot(c, (int)k.X, (int)(k.Y + k.R) + ty - 2, Land.Bark.Shade);
                Plot(c, (int)k.X + 1, (int)(k.Y + k.R) + ty - 2, Land.Bark.Deep);
            }
        foreach (var k in crowns)
        {
            if (Land.Conifers)
                Fir(c, k.X, k.Y - k.R * 1.3f, k.R * 2.9f, k.Hash, sway, InTile);
            else if (Land.Palms)
                Palm(c, k.X, k.Y + k.R, k.R * 1.6f, k.Hash, sway, InTile);
            else
                Crown(c, k.X, k.Y, k.R, k.Hash % 97, sway, InTile);
        }
    }

    /// <summary>
    /// A mountain range drawn from the back row to the front, one lattice point a tile with every other row shifted, so
    /// summits stagger into jagged ridges; each point is its own: a tall capped peak, a low one, a twin summit, or none
    /// at all — a rocky saddle strewn with boulders — and scree spills from the foot of the peaks. Faces are lit from the
    /// west and cut by gullies; near an open edge the peaks are lower and narrower — foothills — so the range does not
    /// end in a cut. The pattern repeats only every four tiles.
    /// </summary>
    private static void MountainRangeTile(PixelCanvas c, int mask, int parity)
    {
        var range = new Range(mask, parity, period: 4);
        var rock = Land.Rock;
        GrassTile(c, 40 + parity % 4);
        // Rocky ground under the range. Towards open sides it ends raggedly six to twelve pixels short, and where two
        // open sides meet the corner is rounded off, so the massif has no square silhouette.
        for (int y = 0; y < T; y++)
            for (int x = 0; x < T; x++)
            {
                int Reach(int along, int salt) => 6 + (Hash(along / 3, salt, 71) % 5 + Hash((along + 2) / 3, salt, 71) % 5) / 2;
                bool open = (mask & 1) == 0 && y < Reach(x, 1) || (mask & 4) == 0 && y > T - 1 - Reach(x, 4)
                            || (mask & 8) == 0 && x < Reach(y, 8) || (mask & 2) == 0 && x > T - 1 - Reach(y, 2);
                foreach (var (bits, cx, cy) in new[] { (1 | 8, 0, 0), (1 | 2, T - 1, 0), (4 | 8, 0, T - 1), (4 | 2, T - 1, T - 1) })
                    if ((mask & bits) == 0 && (x - cx) * (x - cx) + (y - cy) * (y - cy) < 256)
                        open = true;
                if (!open)
                    c.Shade(x, y, rock, (TileNoise(x, y, 4f, 91) - 0.5f) * 1.6f - 0.7f, solid: false);
            }

        var peaks = new System.Collections.Generic.List<(float X, float Apex, float Base, float Half, int Hash)>();
        var saddles = new System.Collections.Generic.List<(float X, float Y, int Hash)>();
        foreach (var (x, y, h) in range.Points(T))
        {
            var room = range.Room(x, y);
            if (h % 7 == 0)
            {
                saddles.Add((x, y, h)); // no peak here: a saddle with boulders
                continue;
            }
            float tall = 12f + h / 3 % 6 * 3.2f;
            float half = Mathf.Min(11f + h / 11 % 5 * 2f, Mathf.Min(room.Left, room.Right) + 2f);
            float apex = Mathf.Max(y - tall, y - room.Up + 2f);
            float baseY = Mathf.Min(y + 14f, y + room.Down);
            if (half < 6f || baseY - apex < 10f)
                continue;
            if (h % 3 == 0)
            {
                // A twin summit: a smaller peak shouldering the main one, a little behind it.
                float side = h % 2 == 0 ? 1f : -1f;
                float twinHalf = half * 0.7f;
                float twinX = Mathf.Clamp(x + side * half * 0.55f, x - room.Left + twinHalf, x + room.Right - twinHalf);
                peaks.Add((twinX, apex + (baseY - apex) * 0.3f, baseY - 2f, twinHalf, h + 1));
            }
            peaks.Add((x, apex, baseY, half, h));
        }
        peaks.Sort((a, b) => a.Base.CompareTo(b.Base));

        foreach (var s in saddles)
            for (int k = 0; k < 6; k++)
                Boulder(c, (int)(s.X - 10 + Hash(s.Hash, k, 3) % 21), (int)(s.Y - 6 + Hash(k, s.Hash, 4) % 15), 2 + k % 2);

        foreach (var p in peaks)
        {
            float height = p.Base - p.Apex;
            float capLine = height >= 24f ? p.Apex + height * 0.32f : float.MinValue;
            // The ridge from the summit runs a little right of centre: the lit west face is the broader.
            float ridge = p.X + p.Half * 0.12f;
            for (int y = (int)p.Apex; y <= (int)p.Base; y++)
            {
                float w = p.Half * (y - p.Apex) / height;
                float ridgeX = p.X + (ridge - p.X) * (y - p.Apex) / height;
                for (int x = (int)Mathf.Floor(p.X - w - 1f); x <= (int)Mathf.Ceil(p.X + w + 1f); x++)
                {
                    float fx = x + 0.5f - p.X;
                    if (Mathf.Abs(fx) > w + 0.5f || !InTile(x, y))
                        continue;
                    bool rim = Mathf.Abs(fx) > w - 0.8f;
                    bool west = x + 0.5f < ridgeX;
                    // Gullies running down the faces, and a cap line that wavers.
                    float gully = Hash((int)((x - p.X) * 0.5f + 40), 0, p.Hash % 17) % 4 == 0 ? -0.7f : 0f;
                    bool cap = y < capLine + Hash(x / 2, 0, p.Hash % 13) % 5 - 2;
                    float faceLight = west ? 0.9f - (ridgeX - x) / Mathf.Max(1f, w) * 0.5f : -0.8f - (x - ridgeX) / Mathf.Max(1f, w) * 0.5f;
                    if (rim)
                        c.Px(x, y, west && fx < 0 ? rock.Deep : SilhouetteRim, solid: false);
                    else if (cap)
                        c.Shade(x, y, Land.Cap, west ? 1f : -0.6f, solid: false);
                    else
                        c.Shade(x, y, rock, faceLight + gully + (Hash(x / 2, y / 2, 33) % 5 - 2) * 0.12f - (y - p.Apex) / height * 0.4f, solid: false);
                }
            }
            // Scree spilling from the foot of the peak.
            for (int k = 0; k < 4; k++)
            {
                int sx = (int)(p.X - p.Half + Hash(p.Hash, k, 5) % (int)(2 * p.Half + 1));
                int sy = (int)p.Base - Hash(k, p.Hash, 6) % 5;
                Boulder(c, sx, sy, 2 + k % 2);
            }
        }
    }

    /// <summary>A small boulder: a lit top over a dark underside, a shadow beneath.</summary>
    private static void Boulder(PixelCanvas c, int x, int y, int r)
    {
        for (int j = -r; j <= r; j++)
            for (int i = -r - 1; i <= r + 1; i++)
            {
                float d = (i * i) / (float)((r + 1) * (r + 1)) + (j * j) / (float)(r * r);
                if (d > 1f || !InTile(x + i, y + j))
                    continue;
                float light = d > 0.7f && i + j > 0 ? -2f : -(i + j) * 0.5f;
                c.Shade(x + i, y + j, Land.Rock, light, solid: false);
            }
    }

    /// <summary>
    /// The pull on pixel (x, y) of a cell of the neighbours set in <paramref name="mask"/> (bit order of
    /// <see cref="ArtLayout.BlendNeighbours"/>) against the cell itself and the others, from 0 to 1. Every cell pulls with
    /// a weight that falls off smoothly with the distance from its centre, and reaches only a little over a tile, so
    /// only the eight neighbours matter and two tiles on either side of a border, each with its own mask, agree on it.
    /// </summary>
    private static float Share(int mask, int x, int y)
    {
        const float reach = 42f;
        static float Weight(float d) => d >= reach ? 0f : (1f - d / reach) * (1f - d / reach);
        float half = T / 2f, px = x + 0.5f, py = y + 0.5f;
        float own = Weight(Mathf.Sqrt((px - half) * (px - half) + (py - half) * (py - half))), other = 0f;
        for (int i = 0; i < 8; i++)
        {
            var (dx, dy) = ArtLayout.BlendNeighbours[i];
            float w = Weight(Mathf.Sqrt((px - half - dx * T) * (px - half - dx * T) + (py - half - dy * T) * (py - half - dy * T)));
            if ((mask & (1 << i)) != 0)
                other += w;
            else
                own += w;
        }
        return other / (own + other);
    }

    /// <summary>
    /// This biome's ground over the part of a cell of another biome where this one prevails. Every cell around (the
    /// eight neighbours in <paramref name="mask"/> are of this biome, the cell itself and the rest are not) pulls on
    /// each pixel with a weight that falls off smoothly with the distance from its centre; the pixel takes this biome
    /// where its share is the larger. The same rule on both sides of a border (each cell draws the other's overlay)
    /// gives one smooth curve: the tile grid's steps are cut across diagonally and corners rounded off.
    /// </summary>
    private static void BiomeBlendTile(PixelCanvas c, int mask)
    {
        GrassTile(c, 60 + mask % 7);
        for (int y = 0; y < T; y++)
            for (int x = 0; x < T; x++)
            {
                // A gentle wobble, so long borders are not ruled straight.
                float wobble = (Hash(x / 5, y / 5, 13) % 5 - 2) * 0.012f;
                float share = Share(mask, x, y);
                if (share <= 0.5f + wobble)
                    c.Erase(x, y);
                else if (share <= 0.53f + wobble && (x + y) % 2 == 0)
                    c.Erase(x, y); // a stippled fringe
            }
    }

    /// <summary>
    /// Open water, darker in rippling bands; through the frames its wavelets drift and swell, and now and then the sun
    /// glints on it. Everything wraps around inside the tile, so the frames loop and tiles join without a seam.
    /// </summary>
    private static void WaterTile(PixelCanvas c, int v, int frame)
    {
        for (int y = 0; y < T; y++)
            for (int x = 0; x < T; x++)
            {
                float band = Mathf.Sin((y + TileNoise(x, y, 8f, 40) * 6f) * Mathf.Tau / 16f + frame * Mathf.Pi / 2f);
                float light = band * 0.3f - 0.45f + (TileNoise(x, y, 16f, 41) - 0.5f) * 0.7f;
                c.Shade(x, y, WaterR, light, solid: false);
            }
        // Wavelets: short arcs, lit on top, drifting a pixel a frame.
        for (int w = 0; w < 3; w++)
        {
            int x = Hash(w, v, 3) % T + frame * 2, y = Hash(v, w, 5) % (T - 4) + 2;
            int length = 4 + (frame + w) % 3 * 2;
            for (int i = 0; i < length; i++)
            {
                int lift = i == 0 || i == length - 1 ? 0 : 1;
                c.Px((x + i) % T, y - lift, WaterR.Light, solid: false);
                c.Px((x + i) % T, y - lift + 1, WaterR.Shade, solid: false);
            }
        }
        if (Hash(v, frame, 77) % 3 == 0)
        {
            int gx = Hash(frame, v, 78) % (T - 4) + 2, gy = Hash(v, frame, 79) % (T - 4) + 2;
            c.Px(gx, gy, WaterR.Glint, solid: false);
            c.Px(gx + 1, gy, WaterR.Light, solid: false);
        }
    }

    /// <summary>A lone mountain on rocky ground.</summary>
    private static void MountainTile(PixelCanvas c, int v)
    {
        for (int y = 0; y < T; y++)
            for (int x = 0; x < T; x++)
                c.Shade(x, y, Land.Rock, (TileNoise(x, y, 4f, 90) - 0.5f) * 1.4f - 1f, solid: false);
        int peak = 12 + v % 3 * 3, top = 2 + v % 2 * 2;
        for (int y = top; y < T; y++)
        {
            float half = (y - top) * 16f / (T - 1 - top);
            for (int x = (int)(peak - half); x <= (int)(peak + half); x++)
            {
                if (!InTile(x, y))
                    continue;
                bool cap = y < top + 6 + Hash(x, 0, v) % 2;
                c.Shade(x, y, cap ? Land.Cap : Land.Rock, x < peak ? 0.8f : -0.8f, solid: false);
            }
        }
    }

    /// <summary>A dirt road through the tile and on to its road neighbours: wheel ruts, pebbles, a grassy fringe.</summary>
    private static void RoadTile(PixelCanvas c, int mask)
    {
        const int from = 8, to = 24; // the band is 16 px wide
        bool Dirt(int x, int y)
        {
            bool centre = x >= from && x < to && y >= from && y < to;
            return centre || (mask & 1) != 0 && x >= from && x < to && y < from || (mask & 2) != 0 && y >= from && y < to && x >= to
                   || (mask & 4) != 0 && x >= from && x < to && y >= to || (mask & 8) != 0 && y >= from && y < to && x < from;
        }
        // A ragged edge: a pixel of the fringe in or out by a hash, kept straight where the road leaves the tile.
        bool Road(int x, int y)
        {
            if (!InTile(x, y))
                return false;
            if (Dirt(x, y))
                return !(Hash(x, y, mask + 3) % 9 == 0 && (!Dirt(x - 1, y) || !Dirt(x + 1, y) || !Dirt(x, y - 1) || !Dirt(x, y + 1)));
            return Hash(x, y, mask + 4) % 11 == 0 && (Dirt(x - 1, y) || Dirt(x + 1, y) || Dirt(x, y - 1) || Dirt(x, y + 1));
        }
        bool vertical = (mask & 5) != 0 && (mask & 10) == 0;
        for (int y = 0; y < T; y++)
            for (int x = 0; x < T; x++)
            {
                if (!Road(x, y))
                    continue;
                bool rim = !Road(x - 1, y) && x > 0 || !Road(x + 1, y) && x < T - 1 || !Road(x, y - 1) && y > 0 || !Road(x, y + 1) && y < T - 1;
                // Two wheel ruts along the way.
                int across = vertical ? x : y;
                bool rut = across is 12 or 19;
                float light = rim ? -1.2f : rut ? -0.7f : (TileNoise(x, y, 4f, 60) - 0.5f) * 1.2f + (Hash(x, y, 61) % 13 == 0 ? 1f : 0f);
                c.Shade(x, y, Land.Dirt, light, solid: false);
            }
        // Pebbles.
        for (int p = 0; p < 5; p++)
        {
            int x = Hash(p, mask, 62) % T, y = Hash(mask, p, 63) % T;
            if (Road(x, y) && Road(x + 1, y + 1))
            {
                c.Px(x, y, Land.Rock.Light, solid: false);
                c.Px(x + 1, y, Land.Rock.Base, solid: false);
                c.Px(x, y + 1, Land.Rock.Shade, solid: false);
            }
        }
    }

    /// <summary>
    /// A wooden deck, like a road tile shaped by the 4-bit mask of the road or bridge cells it joins (N=1, E=2, S=4,
    /// W=8): straight runs, corners, forks and ends. Planks lie across the way (at a corner, across the way in), rails
    /// run along the open sides with a post at each corner, and the deck throws a shadow on the water below it. The
    /// rest of the tile is transparent: the water and its shores are drawn underneath.
    /// </summary>
    private static void BridgeTile(PixelCanvas c, int mask)
    {
        const int from = 6, to = 26; // deck 20 px wide through the centre
        bool north = (mask & 1) != 0, east = (mask & 2) != 0, south = (mask & 4) != 0, west = (mask & 8) != 0;
        var deck = new bool[T, T];
        for (int y = 0; y < T; y++)
            for (int x = 0; x < T; x++)
            {
                bool across = y >= from && y < to, along = x >= from && x < to;
                deck[x, y] = across && along
                             || along && (north && y < from || south && y >= to)
                             || across && (west && x < from || east && x >= to);
            }
        bool verticalCentre = (north || south) && !(east || west);
        bool Inside(int x, int y) => InTile(x, y) && deck[x, y];
        // An open edge of the deck has no deck beyond it, unless it is the tile's border where the way goes on.
        bool Open(int x, int y) => InTile(x, y) && !deck[x, y];
        var wood = WoodR;
        for (int y = 0; y < T; y++)
            for (int x = 0; x < T; x++)
            {
                if (!deck[x, y])
                {
                    if (Inside(x, y - 1) || Inside(x, y - 2) || Inside(x - 1, y - 1) || Inside(x - 2, y - 2))
                        c.Px(x, y, WaterR.Deep, solid: false); // the deck's shadow on the water
                    continue;
                }
                bool rail = Open(x - 1, y) || Open(x + 1, y) || Open(x, y - 1) || Open(x, y + 1)
                            || Open(x - 2, y) || Open(x + 2, y) || Open(x, y - 2) || Open(x, y + 2);
                bool post = rail && (Open(x - 2, y) || Open(x + 2, y) || Open(x - 1, y) || Open(x + 1, y))
                            && (Open(x, y - 2) || Open(x, y + 2) || Open(x, y - 1) || Open(x, y + 1));
                bool outer = Open(x - 1, y) || Open(x + 1, y) || Open(x, y - 1) || Open(x, y + 1);
                // Which way the planks run here: across a north–south way, along an east–west one.
                bool vertical = y < from || y >= to ? true : x < from || x >= to ? false : verticalCentre;
                int plank = vertical ? y : x;
                int grain = vertical ? x : y;
                float light = post ? (outer ? -2f : 0.6f)
                    : rail ? (outer ? -1.6f : 0.9f)
                    : plank % 4 == 3 ? -1.5f
                    : plank % 4 == 0 ? 0.6f
                    : (Hash(grain / 3, plank / 4, 17 + mask) % 5 - 2) * 0.25f;
                c.Shade(x, y, post || rail ? DarkWoodR : wood, light, solid: false);
                if (!post && !rail && plank % 4 == 1 && grain % 9 == 4)
                    c.Px(x, y, IronR.Shade, solid: false); // a nail
            }
    }

    /// <summary>
    /// The coast's bands by the land's share of a pixel (see <see cref="Share"/>): the turf ends at <c>TurfLine</c>, dry
    /// sand runs down to <c>WetLine</c>, wet sand to the waterline at an even share, the water beyond.
    /// </summary>
    private const float TurfLine = 0.68f, WetLine = 0.535f, WaterLine = 0.495f;

    /// <summary>
    /// A stretch of coast, drawn where water meets land on either side of the cell border so that the coastline cuts
    /// across the grid's steps in one smooth, slightly wandering curve, as biomes meet: on a water cell
    /// (<paramref name="onLand"/> false) the land's turf and beach reaching into it, by the 8-bit mask of the land
    /// around it; on a land cell, the water biting into its corners and edges, by the mask of the water around it. Then
    /// the turf's edge, a beach of dry then wet sand with pebbles in it, the waterline and the shallows. The overlay is
    /// transparent where the cell's own ground or water shows. Through the frames the surf washes in over the wet sand
    /// and falls back, leaving bubbles.
    /// </summary>
    private static void CoastTile(PixelCanvas c, int mask, int frame, bool onLand)
    {
        var g = Land.Ground;
        var sand = Land.Sand;
        float foam = frame switch { 0 => 0.43f, 1 => 0.46f, 2 => 0.51f, _ => 0.445f };
        if (onLand)
            WaterTile(c, mask % ArtLayout.TerrainVariants, frame);
        else
            GrassTile(c, 70 + mask % 5);
        for (int y = 0; y < T; y++)
            for (int x = 0; x < T; x++)
            {
                float share = Share(mask, x, y);
                float land = (onLand ? 1f - share : share) + (TileNoise(x, y, 16f, 80) - 0.5f) * 0.12f + (TileNoise(x, y, 8f, 81) - 0.5f) * 0.08f + (TileNoise(x, y, 4f, 82) - 0.5f) * 0.03f;
                if (land >= TurfLine + 0.03f)
                {
                    if (onLand)
                        c.Erase(x, y);
                    continue;
                }
                if (land >= TurfLine)
                {
                    c.Shade(x, y, g, -1.1f, solid: false); // the turf's edge
                    continue;
                }
                if (Mathf.Abs(land - foam) <= 0.018f)
                {
                    c.Erase(x, y);
                    c.Px(x, y, Hash(x, y, frame + 90) % 7 == 0 ? WaterR.Light : FoamColor, solid: false);
                    continue;
                }
                if (land > foam)
                {
                    if (land >= WetLine)
                    {
                        float light = land >= TurfLine - 0.025f ? -0.9f // the turf's shadow on the sand
                            : 0.6f - (TurfLine - land) / (TurfLine - WetLine) * 1.1f + (TileNoise(x, y, 4f, 83) - 0.5f) * 0.6f;
                        c.Shade(x, y, sand, light, solid: false);
                        if (Hash(x, y, mask % 7 + 84) % 29 == 0)
                            c.Px(x, y, Land.Rock.Light, solid: false); // a pebble
                    }
                    else if (land >= WaterLine)
                    {
                        c.Shade(x, y, sand, -1.4f, solid: false);
                    }
                    else
                    {
                        if (!onLand)
                            c.Erase(x, y);
                        c.Px(x, y, WaterR.Light with { A = 0.55f }, solid: false); // a film of water over the sand
                    }
                    if (frame == 3 && land < WetLine && Hash(x, y, 91) % 5 == 0)
                        c.Px(x, y, FoamColor, solid: false); // bubbles the wave left
                    continue;
                }
                if (!onLand)
                    c.Erase(x, y);
                if (land > foam - 0.06f)
                    c.Px(x, y, WaterR.Light with { A = 0.45f }, solid: false);
                else if (land > foam - 0.18f)
                    c.Px(x, y, WaterR.Light with { A = 0.18f }, solid: false); // the shallows
            }
    }
}

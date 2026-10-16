using System;
using Godot;
using Warcrown.World;

namespace Warcrown.Dev;

/// <summary>The terrain sheet: grass, water, shores, roads and bridges, biome blends.</summary>
public static partial class ArtGenerator
{
    /// <summary>
    /// 16×16 tiles, 16 columns. Row 0 grass, 1 a lone tree, 3 a lone mountain (4 variants each); row 4 roads, by a 4-bit
    /// neighbour mask N=1, E=2, S=4, W=8; row 6 bridge decks over water (transparent around them) by the mask of the
    /// road or bridge cells they join; rows 11–26 mountain ranges, by neighbour mask (column) and the cell's position
    /// modulo 4 in each direction (row); rows 27–42 this biome's ground reaching into a neighbouring cell of another
    /// biome, by the 8-bit mask of its neighbours of this biome (row = mask / 16, column = mask % 16).
    /// Animated tiles keep their frames in the rows under the first one (<see cref="ArtLayout.TileFrames"/>): water
    /// (4 variants) from row 43, shore overlays by mask from row 47, and woods by mask and by the cell's position
    /// modulo 2 from row 51 (four positions, each with its frames). Rows 67–68 hold the decor (one kind a column).
    /// Rows 2, 5 and 7–10 are free.
    /// </summary>
    private static PixelCanvas Terrain()
    {
        var c = new PixelCanvas(ArtLayout.Tile * 16, ArtLayout.Tile * ArtLayout.TerrainRows);
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
                Cell(c, mask, ArtLayout.ShoreRow + frame);
                ShoreTile(c, mask, frame);
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
        }
        for (int kind = 0; kind < ArtLayout.DecorKinds; kind++)
        {
            Cell(c, kind % 16, ArtLayout.DecorRow + kind / 16);
            DecorTile(c, kind);
        }
        return c;

        static void Cell(PixelCanvas canvas, int col, int row) =>
            canvas.Origin = new Vector2I(col * ArtLayout.Tile, row * ArtLayout.Tile);
    }

    private static void GrassTile(PixelCanvas c, int v)
    {
        c.Ground(0, 0, 16, 16, Grass);
        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                int h = Hash(x, y, v);
                if (h < 70) c.Px(x, y, GrassDark, solid: false);
                else if (h < 115) c.Px(x, y, GrassLight, solid: false);
            }
        }
        // A couple of grass tufts: a light pixel over a dark one.
        for (int t = 0; t < 3; t++)
        {
            int x = Hash(t, v, 7) % 14 + 1, y = Hash(v, t, 9) % 13 + 2;
            c.Px(x, y, GrassLight, solid: false);
            c.Px(x - 1, y + 1, GrassDark, solid: false);
            c.Px(x + 1, y + 1, GrassDark, solid: false);
        }
    }

    private static void TreeTile(PixelCanvas c, int v)
    {
        int cx = 8 + (v % 2 == 0 ? -1 : 1), cy = 6 + v % 2;
        float r = v == 2 ? 4.5f : 5.5f;
        c.Rect(cx - 1, cy + 4, 2, 4, Bark);
        c.Disc(cx, cy, r, Leaf);
        // Light from the top-left: lighter crescent there, shadow at the bottom-right.
        for (int y = -6; y <= 6; y++)
        {
            for (int x = -6; x <= 6; x++)
            {
                if (x * x + y * y > r * r)
                    continue;
                if (x + y < -4) c.Px(cx + x, cy + y, LeafLight);
                else if (x + y > 4) c.Px(cx + x, cy + y, LeafDark);
            }
        }
        c.Px(cx - 2, cy - 3, LeafHighlight);
        c.Px(cx - 3, cy - 1, LeafHighlight);
        c.Outline(TreeOutline, new Rect2I(c.Origin, new Vector2I(16, 16)));
    }

    /// <summary>
    /// The neighbourhood of a forest or mountain tile: which of the 3×3 tiles around it (−1..1) are of the same kind
    /// (its own, the four by the mask, a diagonal one when both tiles beside it are), and where the pattern's lattice
    /// lies — lattice points every 8 px, jittered by their place in a 2×2-tile period, so both tiles that draw a
    /// crown or a peak across their border draw it the same.
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
        /// Lattice points owned by the present tiles, every <paramref name="spacing"/> px (8: four a tile, 16: one, with
        /// every other row of tiles shifted half a peak sideways): (x, y) in this tile's pixels, and a hash for their look.
        /// </summary>
        public System.Collections.Generic.IEnumerable<(float X, float Y, int Hash)> Points(int spacing)
        {
            int perTile = 16 / spacing;
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
                            int jitter = perTile == 1 ? 3 : 1;
                            float x = tx * 16 + spacing / 2 + spacing * i + h % (2 * jitter + 1) - jitter + (shifted ? 4 : -2);
                            float y = ty * 16 + spacing / 2 + spacing * j + h / 10 % (2 * jitter - 1 + 2) - jitter;
                            yield return (x, y, h);
                        }
                }
        }

        /// <summary>How far a shape centred at (x, y) may reach before it would spill onto a tile of another kind.</summary>
        public (float Left, float Right, float Up, float Down) Room(float x, float y)
        {
            int tx = (int)Mathf.Floor(x / 16f), ty = (int)Mathf.Floor(y / 16f);
            float left = Present(tx - 1, ty) ? 99f : x - tx * 16;
            float right = Present(tx + 1, ty) ? 99f : (tx + 1) * 16 - 1 - x;
            float up = Present(tx, ty - 1) ? 99f : y - ty * 16;
            float down = Present(tx, ty + 1) ? 99f : (ty + 1) * 16 - 1 - y;
            return (left, right, up, down);
        }
    }

    private static void Plot(PixelCanvas c, int x, int y, Color color)
    {
        if (x >= 0 && y >= 0 && x < 16 && y < 16)
            c.Px(x, y, color, solid: false);
    }

    /// <summary>
    /// Woods as one canopy: round crowns on the lattice, drawn from the back row to the front so each overlaps the one
    /// behind, lit from the top-left, with a dark rim and their shadow on the ground; along the open southern edge the
    /// trunks show. Crowns near an open edge are smaller, so the wood ends in a rounded fringe, not a cut.
    /// </summary>
    /// <remarks>Through the frames the crowns' tops lean with the wind and back: one pixel downwind, upright, one upwind.</remarks>
    private static void ForestRangeTile(PixelCanvas c, int mask, int parity, int frame)
    {
        int sway = frame switch { 1 => 1, 3 => -1, _ => 0 };
        var range = new Range(mask, parity);
        GrassTile(c, 30 + parity);
        var crowns = new System.Collections.Generic.List<(float X, float Y, float R, int Hash)>();
        foreach (var (x, y, h) in range.Points(8))
        {
            var room = range.Room(x, y);
            float r = Mathf.Min(4.4f + h % 4 * 0.45f, Mathf.Min(Mathf.Min(room.Left, room.Right), Mathf.Min(room.Up, room.Down - 2f)) + 0.6f);
            if (r >= 2.2f)
                crowns.Add((x, y, r, h));
        }
        crowns.Sort((a, b) => a.Y.CompareTo(b.Y));

        bool InCrown(int px, int py, float dx, float dy) =>
            crowns.Exists(k => (px + 0.5f - k.X - dx) * (px + 0.5f - k.X - dx) + (py + 0.5f - k.Y - dy) * (py + 0.5f - k.Y - dy) <= k.R * k.R);
        // Shadows on the ground, a little below and to the right of the canopy.
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
                if (!InCrown(x, y, 0f, 0f) && InCrown(x, y, 1f, 2.5f))
                    Plot(c, x, y, ForestShadow);
        // Trunks under the crowns (only those at the front edge stay visible).
        foreach (var k in crowns)
        {
            for (int ty = 0; ty < 3; ty++)
            {
                Plot(c, (int)k.X - 1, (int)(k.Y + k.R) + ty - 1, Bark);
                Plot(c, (int)k.X, (int)(k.Y + k.R) + ty - 1, BarkDark);
            }
        }
        foreach (var k in crowns)
        {
            int r = (int)Mathf.Ceil(k.R);
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    float fx = (int)k.X + dx + 0.5f - k.X, fy = (int)k.Y + dy + 0.5f - k.Y;
                    float d2 = fx * fx + fy * fy;
                    if (d2 > k.R * k.R)
                        continue;
                    float light = fx + fy;
                    var color = d2 > (k.R - 1f) * (k.R - 1f) && light > -k.R * 0.6f ? TreeOutline
                        : light < -k.R * 0.75f ? LeafLight
                        : light > k.R * 0.55f ? LeafDark
                        : Hash((int)k.X + dx + 40, (int)k.Y + dy + 40, k.Hash % 7) < 90 ? LeafDark
                        : Leaf;
                    Plot(c, (int)k.X + dx + (fy < -k.R * 0.2f ? sway : 0), (int)k.Y + dy, color);
                }
            Plot(c, (int)(k.X - k.R * 0.4f) + sway, (int)(k.Y - k.R * 0.45f), LeafHighlight);
            Plot(c, (int)(k.X - k.R * 0.15f) + sway, (int)(k.Y - k.R * 0.6f), LeafHighlight);
        }
    }

    /// <summary>
    /// A mountain range drawn from the back row to the front, one lattice point a tile with every other row shifted, so
    /// summits stagger into jagged ridges; each point is its own: a tall snowcapped peak, a low one, a twin summit, or
    /// none at all — a rocky saddle strewn with boulders — and scree spills from the foot of the peaks. Faces are lit
    /// from the west, rims dark; near an open edge the peaks are lower and narrower — foothills — so the range does
    /// not end in a cut. The pattern repeats only every four tiles.
    /// </summary>
    private static void MountainRangeTile(PixelCanvas c, int mask, int parity)
    {
        var range = new Range(mask, parity, period: 4);
        GrassTile(c, 40 + parity % 4);
        // Rocky ground under the range. Towards open sides it ends raggedly three to six pixels short, and where two open
        // sides meet the corner is rounded off, so the massif has no square silhouette.
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
            {
                int Reach(int along, int salt) => 3 + (Hash(along / 2, salt, 71) % 3 + Hash((along + 1) / 2, salt, 71) % 3) / 2;
                bool open = (mask & 1) == 0 && y < Reach(x, 1) || (mask & 4) == 0 && y > 15 - Reach(x, 4)
                            || (mask & 8) == 0 && x < Reach(y, 8) || (mask & 2) == 0 && x > 15 - Reach(y, 2);
                foreach (var (bits, cx, cy) in new[] { (1 | 8, 0, 0), (1 | 2, 15, 0), (4 | 8, 0, 15), (4 | 2, 15, 15) })
                    if ((mask & bits) == 0 && (x - cx) * (x - cx) + (y - cy) * (y - cy) < 64)
                        open = true;
                if (!open)
                    Plot(c, x, y, Hash(x, y, 91) < 60 ? Rock : RockDark);
            }

        var peaks = new System.Collections.Generic.List<(float X, float Apex, float Base, float Half, int Hash)>();
        var saddles = new System.Collections.Generic.List<(float X, float Y, int Hash)>();
        foreach (var (x, y, h) in range.Points(16))
        {
            var room = range.Room(x, y);
            if (h % 7 == 0)
            {
                saddles.Add((x, y, h)); // no peak here: a saddle with boulders
                continue;
            }
            float tall = 6f + h / 3 % 6 * 1.6f;
            float half = Mathf.Min(5.5f + h / 11 % 5, Mathf.Min(room.Left, room.Right) + 1f);
            float apex = Mathf.Max(y - tall, y - room.Up + 1f);
            float baseY = Mathf.Min(y + 7f, y + room.Down);
            if (half < 3f || baseY - apex < 5f)
                continue;
            if (h % 3 == 0)
            {
                // A twin summit: a smaller peak shouldering the main one, a little behind it.
                float side = h % 2 == 0 ? 1f : -1f;
                float twinHalf = half * 0.7f;
                float twinX = Mathf.Clamp(x + side * half * 0.55f, x - room.Left + twinHalf, x + room.Right - twinHalf);
                peaks.Add((twinX, apex + (baseY - apex) * 0.3f, baseY - 1f, twinHalf, h + 1));
            }
            peaks.Add((x, apex, baseY, half, h));
        }
        peaks.Sort((a, b) => a.Base.CompareTo(b.Base));

        foreach (var s in saddles)
            for (int k = 0; k < 5; k++)
                Boulder(c, (int)(s.X - 5 + Hash(s.Hash, k, 3) % 11), (int)(s.Y - 3 + Hash(k, s.Hash, 4) % 8));

        foreach (var p in peaks)
        {
            float height = p.Base - p.Apex;
            float snowLine = height >= 13f ? p.Apex + height * 0.3f : float.MinValue;
            for (int y = (int)p.Apex; y <= (int)p.Base; y++)
            {
                float w = p.Half * (y - p.Apex) / height;
                for (int x = (int)Mathf.Floor(p.X - w - 1f); x <= (int)Mathf.Ceil(p.X + w + 1f); x++)
                {
                    float fx = x + 0.5f - p.X;
                    if (Mathf.Abs(fx) > w + 0.5f)
                        continue;
                    bool rim = Mathf.Abs(fx) > w - 0.7f;
                    // The snow line wavers a little, so caps are not ruled straight.
                    bool snow = y < snowLine + (Hash(x, 0, p.Hash % 13) % 3) - 1;
                    var color = rim ? Outline
                        : snow ? (fx < 0.5f ? Snow : SnowShade)
                        : fx < 0.5f ? (Hash(x, y, 33) < 50 ? Rock : RockLight)
                        : Hash(x, y, 34) < 60 ? RockDark : Rock;
                    Plot(c, x, y, color);
                }
            }
            // Scree spilling from the foot of the peak.
            for (int k = 0; k < 3; k++)
            {
                int sx = (int)(p.X - p.Half + Hash(p.Hash, k, 5) % (int)(2 * p.Half + 1));
                int sy = (int)p.Base - Hash(k, p.Hash, 6) % 3;
                Boulder(c, sx, sy);
            }
        }
    }

    /// <summary>A two-pixel boulder: a lit top over a dark underside.</summary>
    private static void Boulder(PixelCanvas c, int x, int y)
    {
        Plot(c, x, y, RockLight);
        Plot(c, x + 1, y, Rock);
        Plot(c, x, y + 1, RockDark);
        Plot(c, x + 1, y + 1, Outline);
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
        const float reach = 21f; // px: a little over a tile, so only the eight neighbours matter
        static float Weight(float d) => d >= reach ? 0f : (1f - d / reach) * (1f - d / reach);
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                float own = Weight(Mathf.Sqrt((px - 8f) * (px - 8f) + (py - 8f) * (py - 8f))), other = 0f;
                for (int i = 0; i < 8; i++)
                {
                    var (dx, dy) = ArtLayout.BlendNeighbours[i];
                    float w = Weight(Mathf.Sqrt((px - 8f - dx * 16) * (px - 8f - dx * 16) + (py - 8f - dy * 16) * (py - 8f - dy * 16)));
                    if ((mask & (1 << i)) != 0)
                        other += w;
                    else
                        own += w;
                }
                // A gentle wobble, so long borders are not ruled straight.
                float wobble = (Hash(x / 3, y / 3, 13) % 5 - 2) * 0.012f;
                if (other / (own + other) <= 0.5f + wobble)
                    c.Base.SetPixel(c.Origin.X + x, c.Origin.Y + y, Colors.Transparent);
            }
    }

    /// <summary>
    /// Open water; through the frames its wavelets drift a pixel at a time, swell and ebb, and now and then the sun
    /// glints on it. Wavelets wrap around inside the tile, so the frames loop without a seam.
    /// </summary>
    private static void WaterTile(PixelCanvas c, int v, int frame)
    {
        c.Ground(0, 0, 16, 16, Water);
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
                if (Hash(x, y, v + 40) < 60)
                    c.Px(x, y, WaterDark, solid: false);
        int[] lengths = { 3, 4, 4, 2 };
        for (int w = 0; w < 3; w++)
        {
            int x = (Hash(w, v, 3) % 11) + 1 + frame, y = (Hash(v, w, 5) % 13) + 1;
            int length = lengths[(frame + w) % 4];
            for (int i = 0; i < length; i++)
                c.Px((x + i) % 16, y, WaterLight, solid: false);
            if (length >= 3)
                c.Px((x + 1) % 16, y - 1, WaterLight, solid: false);
        }
        if (Hash(v, frame, 77) % 2 == 0)
            c.Px(Hash(frame, v, 78) % 14 + 1, Hash(v, frame, 79) % 14 + 1, Foam, solid: false);
    }

    private static void MountainTile(PixelCanvas c, int v)
    {
        c.Ground(0, 0, 16, 16, RockDark);
        int peak = 6 + v % 3, top = 1 + v % 2;
        for (int y = top; y < 16; y++)
        {
            int half = (y - top) * 8 / (15 - top);
            for (int x = peak - half; x <= peak + half; x++)
            {
                if (x < 0 || x > 15)
                    continue;
                var color = y < top + 3 ? Snow : x < peak ? RockLight : Rock;
                c.Px(x, y, color);
            }
        }
        c.Outline(Outline, new Rect2I(c.Origin, new Vector2I(16, 16)));
    }

    private static void RoadTile(PixelCanvas c, int mask)
    {
        // Dirt band 8 px wide through the centre, extended towards connected neighbours.
        int from = 4, to = 12;
        void Dirt(int x0, int y0, int x1, int y1)
        {
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                    c.Px(x, y, ArtGenerator.Dirt, solid: false);
        }
        Dirt(from, from, to, to);
        if ((mask & 1) != 0) Dirt(from, 0, to, from);
        if ((mask & 2) != 0) Dirt(to, from, 16, to);
        if ((mask & 4) != 0) Dirt(from, to, to, 16);
        if ((mask & 8) != 0) Dirt(0, from, from, to);

        // Darker rim where dirt meets grass, pebbles inside.
        var dirt = new bool[16, 16];
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
                dirt[x, y] = c.Base.GetPixel(c.Origin.X + x, c.Origin.Y + y) == ArtGenerator.Dirt;
        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                if (!dirt[x, y])
                    continue;
                bool rim = (x > 0 && !dirt[x - 1, y]) || (x < 15 && !dirt[x + 1, y]) || (y > 0 && !dirt[x, y - 1]) || (y < 15 && !dirt[x, y + 1]);
                if (rim) c.Px(x, y, DirtDark, solid: false);
                else if (Hash(x, y, mask + 60) < 70) c.Px(x, y, DirtLight, solid: false);
                else if (Hash(x, y, mask + 61) < 50) c.Px(x, y, DirtDark, solid: false);
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
        const int from = 3, to = 13; // deck 10 px wide through the centre
        bool north = (mask & 1) != 0, east = (mask & 2) != 0, south = (mask & 4) != 0, west = (mask & 8) != 0;
        var deck = new bool[16, 16];
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
            {
                bool across = y >= from && y < to, along = x >= from && x < to;
                deck[x, y] = across && along
                             || along && (north && y < from || south && y >= to)
                             || across && (west && x < from || east && x >= to);
            }
        bool verticalCentre = (north || south) && !(east || west);
        bool Inside(int x, int y) => x >= 0 && y >= 0 && x < 16 && y < 16 && deck[x, y];
        // An open edge of the deck has no deck beyond it, unless it is the tile's border where the way goes on.
        bool Open(int x, int y) => x < 0 || y < 0 || x > 15 || y > 15 ? false : !deck[x, y];

        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
            {
                if (!deck[x, y])
                {
                    if (Inside(x, y - 1) || Inside(x - 1, y - 1))
                        c.Px(x, y, WaterDark, solid: false); // the deck's shadow on the water
                    continue;
                }
                bool rail = Open(x - 1, y) || Open(x + 1, y) || Open(x, y - 1) || Open(x, y + 1);
                bool post = rail && (Open(x - 1, y) || Open(x + 1, y)) && (Open(x, y - 1) || Open(x, y + 1));
                // Which way the planks run here: across a north–south way, along an east–west one.
                bool vertical = y < from || y >= to ? true : x < from || x >= to ? false : verticalCentre;
                int plank = vertical ? y : x;
                var color = post ? Outline
                    : rail ? WoodDark
                    : plank % 4 == 3 ? WoodDark
                    : Hash(x, y, 17 + mask) < 120 ? WoodLight
                    : Wood;
                c.Px(x, y, color, solid: false);
            }
    }

    /// <summary>
    /// Transparent tile with sand and foam along the sides where land is. Through the frames the surf washes in and
    /// out: the foam line comes up to the sand, wets it, and falls back leaving bubbles.
    /// </summary>
    private static void ShoreTile(PixelCanvas c, int mask, int frame)
    {
        int foam = frame is 1 or 2 ? 2 : 3;
        var wet = frame == 2 ? WaterLight : SandDark;
        for (int i = 0; i < 16; i++)
        {
            bool bubble = frame == 3 && (i + mask) % 3 == 0 || frame == 0 && (i + mask) % 4 == 1;
            void Side(Func<int, Vector2I> at)
            {
                c.Px(at(0).X, at(0).Y, Sand, false);
                c.Px(at(1).X, at(1).Y, wet, false);
                c.Px(at(foam).X, at(foam).Y, Foam, false);
                if (bubble)
                    c.Px(at(foam + 1).X, at(foam + 1).Y, Foam, false);
            }
            if ((mask & 1) != 0) Side(d => new Vector2I(i, d));
            if ((mask & 4) != 0) Side(d => new Vector2I(i, 15 - d));
            if ((mask & 8) != 0) Side(d => new Vector2I(d, i));
            if ((mask & 2) != 0) Side(d => new Vector2I(15 - d, i));
        }
    }
}

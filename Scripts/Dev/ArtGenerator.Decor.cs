using System;
using Godot;
using Lordsio.World;

namespace Lordsio.Dev;

/// <summary>Decor: the small things lying about on open ground, in each biome.</summary>
public static partial class ArtGenerator
{
    private static readonly Color FlowerRed = new("d8433a");
    private static readonly Color FlowerYellow = new("f2d04a");
    private static readonly Color FlowerWhite = new("f4f1e6");
    private static readonly Color FlowerBlue = new("7f8fe6");
    private static readonly Color Berry = new("c42a4a");
    private static readonly Color Bone = new("e8e2d0");
    private static readonly Color BoneDark = new("b5ab92");
    private static readonly Color Ice = new("bfe6f6");
    private static readonly Color IceDark = new("7fb4d4");
    private static readonly Color IceOutline = new("3d6a8a");
    private static readonly Color GroundShadow = new(0f, 0f, 0f, 0.22f);

    /// <summary>
    /// One small thing lying on open ground, on a transparent 16×16 tile (see <see cref="ArtLayout.MeadowDecor"/> and the
    /// other pools for which kinds each biome uses; the winter and desert sheets recolour them like the rest).
    /// </summary>
    private static void DecorTile(PixelCanvas c, int kind)
    {
        var cell = new Rect2I(c.Origin, new Vector2I(16, 16));
        switch (kind)
        {
            case 0: Flowers(c, FlowerRed, 0); break;
            case 1: Flowers(c, FlowerYellow, 1); break;
            case 2: Flowers(c, FlowerWhite, 2); break;
            case 3: Flowers(c, FlowerBlue, 3); break;
            case 4: // tall grass
                for (int b = 0; b < 7; b++)
                {
                    int x = 3 + b * 2 - (b > 3 ? 1 : 0), h = 3 + Hash(b, 4, 1) % 4, lean = b % 3 - 1;
                    for (int i = 0; i < h; i++)
                        c.Px(x + (i == h - 1 ? lean : 0), 13 - i, i == h - 1 ? new Color("8cc35a") : GrassDark, solid: false);
                }
                break;
            case 5: Bush(c, 8, 10, 4.2f, false); c.Outline(TreeOutline, cell); break;
            case 6: // pebbles
                foreach (var (x, y) in new[] { (4, 6), (10, 4), (7, 11), (12, 12), (3, 13) })
                {
                    c.Px(x, y, RockLight, solid: false);
                    c.Px(x + 1, y, Rock, solid: false);
                    c.Px(x, y + 1, RockDark, solid: false);
                }
                break;
            case 7: // mushrooms
                foreach (var (x, y, w) in new[] { (5, 10, 4), (10, 12, 3) })
                {
                    c.Rect(x + w / 2 - 1, y, 2, 3, Bone);
                    c.Rect(x, y - 2, w, 2, new Color("c8502e"));
                    c.Rect(x + 1, y - 3, w - 2, 1, new Color("c8502e"));
                    c.Px(x + 1, y - 2, FlowerWhite);
                }
                c.Outline(new Color("3a2418"), cell);
                break;
            case 8: Stump(c, false); c.Outline(Outline, cell); break;
            case 9: // fallen log
                Shadow(c, 3, 13, 11, 1);
                c.Rect(2, 9, 11, 4, Bark);
                c.Rect(2, 9, 11, 1, WoodLight);
                c.Rect(2, 12, 11, 1, BarkDark);
                c.Rect(12, 9, 2, 4, WoodLight);
                c.Px(12, 10, Wood);
                c.Px(5, 8, Leaf);
                c.Px(6, 8, LeafLight);
                c.Outline(Outline, cell);
                break;
            case 10: Rock3(c, 8, 9, 3.4f); c.Outline(Outline, cell); break;
            case 11: // a broken column of an old ruin, a drum fallen beside it
                Shadow(c, 4, 14, 11, 1);
                c.Rect(5, 12, 7, 2, Stone);
                c.Rect(6, 4, 5, 8, StoneLight);
                c.Rect(9, 4, 2, 8, Stone);
                c.Rect(6, 6, 1, 6, Stone);
                c.Px(6, 3, StoneLight); c.Px(8, 3, StoneLight); c.Px(9, 2, Stone);
                c.Rect(12, 11, 3, 3, StoneLight);
                c.Px(14, 13, Stone);
                c.Px(7, 9, Leaf); c.Px(6, 10, LeafLight); // ivy
                c.Outline(Outline, cell);
                break;
            case 12: // berry bush
                Bush(c, 8, 10, 4f, false);
                foreach (var (x, y) in new[] { (6, 8), (9, 9), (7, 12), (10, 11), (5, 10) })
                    c.Px(x, y, Berry);
                c.Outline(TreeOutline, cell);
                break;
            case 13: // snowdrift
                for (int y = 0; y < 5; y++)
                {
                    int half = 6 - Math.Abs(y - 1) - (y > 2 ? 1 : 0);
                    for (int x = 8 - half; x < 8 + half; x++)
                        c.Px(x, 8 + y, y >= 3 ? SnowShade : y == 0 ? Colors.White : Snow, solid: false);
                }
                c.Rect(4, 13, 8, 1, new Color("aebccb"));
                break;
            case 14: // ice crystals
                Shadow(c, 4, 13, 9, 1);
                foreach (var (x, h) in new[] { (5, 5), (8, 8), (11, 4) })
                {
                    c.Rect(x, 13 - h, 2, h, Ice);
                    c.Rect(x + 1, 13 - h, 1, h, IceDark);
                    c.Px(x, 12 - h, Ice);
                }
                c.Outline(IceOutline, cell);
                break;
            case 15: Twigs(c, BarkDark); break;
            case 16: Stump(c, true); c.Outline(Outline, cell); break;
            case 17: // young fir with snow on its boughs
                Shadow(c, 5, 14, 7, 1);
                for (int row = 0; row < 10; row++)
                {
                    int half = 1 + row / 2 - (row % 3 == 0 && row > 0 ? 1 : 0);
                    for (int x = 8 - half; x <= 8 + half; x++)
                        c.Px(x, 3 + row, row % 3 == 0 ? Snow : x < 8 ? LeafLight : LeafDark);
                }
                c.Rect(8, 13, 1, 2, Bark);
                c.Outline(TreeOutline, cell);
                break;
            case 18: // frozen puddle
                for (int y = 0; y < 5; y++)
                    for (int x = 0; x < 11; x++)
                    {
                        float dx = (x - 5f) / 5.5f, dy = (y - 2f) / 2.5f;
                        if (dx * dx + dy * dy <= 1f)
                            c.Px(3 + x, 8 + y, dx * dx + dy * dy > 0.6f ? IceDark : Ice, solid: false);
                    }
                c.Px(6, 9, Colors.White, solid: false);
                c.Px(7, 9, Colors.White, solid: false);
                break;
            case 19: Rock3(c, 8, 9, 3.4f); c.Rect(6, 6, 4, 1, Snow); c.Rect(5, 7, 3, 1, Snow); c.Outline(Outline, cell); break;
            case 20: // tracks in the snow
                for (int s = 0; s < 4; s++)
                {
                    c.Px(3 + s * 3, 12 - s * 2, SnowShade, solid: false);
                    c.Px(4 + s * 3, 13 - s * 2, SnowShade, solid: false);
                    c.Px(5 + s * 3, 11 - s * 2, SnowShade, solid: false);
                }
                break;
            case 21: // saguaro
                Shadow(c, 6, 14, 6, 1);
                c.Rect(7, 3, 3, 11, Leaf);
                c.Rect(7, 3, 1, 11, LeafLight);
                c.Rect(9, 3, 1, 11, LeafDark);
                c.Rect(4, 8, 3, 2, Leaf); c.Rect(4, 5, 2, 3, Leaf); c.Px(4, 5, LeafLight);
                c.Rect(10, 6, 2, 2, Leaf); c.Rect(11, 3, 2, 3, Leaf); c.Px(12, 3, LeafDark);
                c.Px(8, 2, FlowerRed);
                c.Outline(TreeOutline, cell);
                break;
            case 22: // barrel cactus
                Shadow(c, 5, 13, 7, 1);
                c.Disc(8, 10, 3.2f, Leaf);
                c.Rect(6, 8, 1, 4, LeafLight);
                c.Rect(10, 8, 1, 4, LeafDark);
                c.Px(8, 7, FlowerYellow);
                c.Px(7, 6, FlowerRed);
                c.Outline(TreeOutline, cell);
                break;
            case 23: // skull
                Shadow(c, 5, 13, 7, 1);
                c.Rect(5, 8, 6, 4, Bone);
                c.Rect(6, 7, 4, 1, Bone);
                c.Rect(6, 12, 4, 1, BoneDark);
                c.Px(6, 9, Outline); c.Px(9, 9, Outline);
                c.Px(7, 11, BoneDark); c.Px(8, 11, BoneDark);
                c.Outline(new Color("5a4a34"), cell);
                break;
            case 24: // ribcage half in the sand
                c.Rect(3, 11, 11, 1, Bone);
                for (int r = 0; r < 4; r++)
                {
                    c.Rect(4 + r * 3, 7 + r % 2, 1, 4 - r % 2, Bone);
                    c.Px(5 + r * 3, 7 + r % 2, BoneDark);
                }
                c.Outline(new Color("8a7a5a"), cell);
                break;
            case 25: Twigs(c, new Color("8a6a3a")); break; // tumbleweed
            case 26: // cracked ground
                foreach (var (x0, y0, x1, y1) in new[] { (2, 6, 7, 9), (7, 9, 13, 7), (7, 9, 8, 14), (10, 8, 12, 12) })
                    LineGround(c, x0, y0, x1, y1, DirtDark);
                break;
            case 27: // a pile of rocks
                Rock3(c, 6, 10, 3f);
                Rock3(c, 11, 11, 2.2f);
                Rock3(c, 9, 7, 2f);
                c.Outline(Outline, cell);
                break;
            case 28: // ripples of wind-blown sand
                for (int r = 0; r < 3; r++)
                    for (int x = 2; x < 14; x++)
                    {
                        int y = 5 + r * 4 + (int)Mathf.Round(Mathf.Sin(x * 0.7f + r) * 0.8f);
                        c.Px(x, y, GrassLight, solid: false);
                        c.Px(x, y + 1, GrassDark, solid: false);
                    }
                break;
            case 29: // a crumbling sandstone wall
                Shadow(c, 2, 14, 13, 1);
                for (int x = 2; x < 14; x++)
                {
                    int top = 6 + Hash(x / 2, 3, 29) % 4;
                    for (int y = top; y < 14; y++)
                        c.Px(x, y, (y - top) % 3 == 2 || (x + (y / 3) * 2) % 5 == 0 ? SandDark : Sand);
                }
                c.Outline(Outline, cell);
                break;
            case 30: // scattered bones
                c.Rect(4, 9, 5, 1, Bone); c.Px(3, 8, Bone); c.Px(3, 10, Bone); c.Px(9, 8, Bone); c.Px(9, 10, Bone);
                c.Rect(8, 12, 4, 1, BoneDark); c.Px(7, 11, Bone); c.Px(12, 13, Bone);
                break;
            default: // a mixed patch of flowers
                Flowers(c, FlowerYellow, 5);
                Flowers(c, FlowerBlue, 6);
                break;
        }
    }

    private static void Flowers(PixelCanvas c, Color petal, int seed)
    {
        for (int f = 0; f < 4; f++)
        {
            int x = 2 + Hash(f, seed, 3) % 12, y = 3 + Hash(seed, f, 4) % 10;
            c.Px(x, y + 1, GrassDark, solid: false);
            c.Px(x, y + 2, GrassDark, solid: false);
            c.Px(x - 1, y, petal, solid: false);
            c.Px(x + 1, y, petal, solid: false);
            c.Px(x, y - 1, petal, solid: false);
            c.Px(x, y, f % 2 == 0 ? FlowerYellow : petal.Darkened(0.25f), solid: false);
        }
    }

    private static void Bush(PixelCanvas c, int cx, int cy, float r, bool snow)
    {
        Shadow(c, cx - (int)r + 1, cy + (int)r, (int)(2 * r), 1);
        c.Disc(cx, cy, r, Leaf);
        int ri = (int)Mathf.Ceil(r);
        for (int y = -ri; y <= ri; y++)
            for (int x = -ri; x <= ri; x++)
            {
                if (x * x + y * y > r * r)
                    continue;
                if (x + y < -r * 0.7f) c.Px(cx + x, cy + y, LeafLight);
                else if (x + y > r * 0.6f) c.Px(cx + x, cy + y, LeafDark);
            }
        c.Px(cx - 2, cy - 2, snow ? Snow : LeafHighlight);
    }

    private static void Stump(PixelCanvas c, bool snow)
    {
        Shadow(c, 5, 13, 8, 1);
        c.Rect(5, 8, 6, 5, Bark);
        c.Rect(9, 8, 2, 5, BarkDark);
        c.Rect(5, 7, 6, 2, WoodLight);
        c.Px(7, 7, Wood); c.Px(8, 8, Wood);
        c.Px(4, 12, Bark); c.Px(11, 12, BarkDark);
        if (snow)
        {
            c.Rect(5, 6, 6, 2, Snow);
            c.Px(4, 7, Snow);
        }
    }

    private static void Rock3(PixelCanvas c, int cx, int cy, float r)
    {
        Shadow(c, cx - (int)r, cy + (int)r - 1, (int)(2 * r) + 1, 1);
        int ri = (int)Mathf.Ceil(r);
        for (int y = -ri; y <= ri; y++)
            for (int x = -ri - 1; x <= ri + 1; x++)
            {
                float dx = x / (r + 0.8f), dy = y / r;
                if (dx * dx + dy * dy > 1f || y > r * 0.6f)
                    continue;
                c.Px(cx + x, cy + y, x + y < -r * 0.5f ? RockLight : x + y > r * 0.4f ? RockDark : Rock);
            }
    }

    /// <summary>A dry, leafless bush: twigs branching from the ground.</summary>
    private static void Twigs(PixelCanvas c, Color color)
    {
        Shadow(c, 4, 13, 8, 1);
        foreach (var (x0, y0, x1, y1) in new[] { (8, 13, 8, 7), (8, 10, 5, 6), (8, 9, 11, 5), (5, 6, 4, 4), (11, 5, 12, 3), (8, 7, 7, 4), (8, 11, 12, 9) })
            LineGround(c, x0, y0, x1, y1, color);
    }

    private static void LineGround(PixelCanvas c, int x0, int y0, int x1, int y1, Color color)
    {
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1, dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1, err = dx + dy;
        while (true)
        {
            c.Px(x0, y0, color, solid: false);
            if (x0 == x1 && y0 == y1)
                break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    /// <summary>A soft dark strip on the ground under a thing.</summary>
    private static void Shadow(PixelCanvas c, int x, int y, int w, int h)
    {
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
                c.Px(x + i, y + j, GroundShadow, solid: false);
    }
}

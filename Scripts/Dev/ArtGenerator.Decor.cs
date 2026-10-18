using System;
using Godot;
using Warcrown.World;

namespace Warcrown.Dev;

/// <summary>Decor: the small things lying about on open ground, in each biome.</summary>
public static partial class ArtGenerator
{
    private static readonly Color FlowerRed = new("d8433a");
    private static readonly Color FlowerYellow = new("f2d04a");
    private static readonly Color FlowerWhite = new("f4f1e6");
    private static readonly Color FlowerBlue = new("7f8fe6");
    private static readonly Ramp BerryR = Ramp.Of(new Color("a8304a"), mood: FigureMood);
    private static readonly Ramp BoneR = Ramp.Of(new Color("cbc2aa"), 0.7f, mood: FigureMood);
    private static readonly Ramp IceR = Ramp.Of(new Color("a9daf0"), 0.7f);
    private static readonly Ramp CapR = Ramp.Of(new Color("a84a30"), mood: FigureMood);
    private static readonly Ramp CactusR = Ramp.Of(new Color("5e8f3c"), mood: FigureMood);

    /// <summary>
    /// One small thing lying on open ground, on a transparent 32×32 tile (see <see cref="ArtLayout.MeadowDecor"/> and
    /// the other pools for which kinds each biome uses), drawn in the biome's own materials.
    /// </summary>
    private static void DecorTile(PixelCanvas c, int kind)
    {
        var cell = new Rect2I(c.Origin, new Vector2I(T, T));
        var g = Land.Ground;
        switch (kind)
        {
            case 0: Flowers(c, FlowerRed, 0); break;
            case 1: Flowers(c, FlowerYellow, 1); break;
            case 2: Flowers(c, FlowerWhite, 2); break;
            case 3: Flowers(c, FlowerBlue, 3); break;
            case 4: // tall grass, a clump of leaning blades
                for (int b = 0; b < 11; b++)
                {
                    float x = 7 + b * 1.8f, tall = 6 + Hash(b, 4, 1) % 7, lean = (b - 5) * 0.5f;
                    for (int i = 0; i < (int)tall; i++)
                    {
                        float t = i / tall;
                        int px = (int)(x + lean * t * t * 3f);
                        c.Px(px, 27 - i, t > 0.7f ? Land.Tuft : g.Shade.Lerp(Land.Leaf.Base, 0.4f), solid: false);
                    }
                }
                break;
            case 5: Bush(c, 16, 19, 8.5f, false); c.SelOut(SilhouetteRim, cell); break;
            case 6: // pebbles
                foreach (var (x, y) in new[] { (8, 12), (20, 8), (14, 22), (24, 24), (6, 26) })
                {
                    c.GroundShadow(x + 1.5f, y + 2.5f, 3f, 1.4f, 0.25f);
                    c.Ball(x, y, 2.2f, 1.7f, Land.Rock, 0.4f);
                }
                c.SelOut(SilhouetteRim, cell);
                break;
            case 7: // mushrooms
                foreach (var (x, y, w) in new[] { (10f, 22f, 4.5f), (21f, 25f, 3.5f) })
                {
                    c.GroundShadow(x + 2, y + 4, w, 1.6f, 0.25f);
                    c.Stroke(x, y, x, y + 4, 2.4f, BoneR);
                    c.Ball(x, y - 1f, w, w * 0.6f, CapR, 0.3f, 0.3f);
                    c.Px((int)x - 1, (int)y - 3, FlowerWhite);
                    c.Px((int)x + 2, (int)y - 2, FlowerWhite);
                }
                c.SelOut(SilhouetteRim, cell);
                break;
            case 8: Stump(c, false); c.SelOut(SilhouetteRim, cell); break;
            case 9: // fallen log, moss on its back
                c.GroundShadow(17, 27, 12, 2.5f, 0.3f);
                c.Column(5, 18, 22, 8, Land.Bark, 0.1f, horizontal: true);
                c.Ball(26f, 22f, 2.5f, 4f, WoodR, 0.6f, 1f);
                c.Ball(26f, 22f, 1.2f, 2f, DarkWoodR, 0f, 1f);
                c.Ball(11f, 17.5f, 3.5f, 1.5f, Land.Leaf, 0.4f);
                c.SelOut(SilhouetteRim, cell);
                break;
            case 10: RockLump(c, 16, 19, 7f); c.SelOut(SilhouetteRim, cell); break;
            case 11: // a broken column of an old ruin, a drum fallen beside it, ivy
                c.GroundShadow(18, 28, 11, 2.5f, 0.3f);
                c.Block(9, 24, 15, 4, WallR, top: 1);
                c.Column(11, 8, 10, 16, WallLightR);
                for (int x = 11; x < 21; x++)
                    if (Hash(x, 0, 11) % 3 == 0)
                        c.Erase(x, 8 + Hash(x, 1, 11) % 3);
                c.Column(24, 21, 6, 6, WallLightR, 0.2f, horizontal: true);
                c.Ball(13f, 18f, 2f, 3f, Land.Leaf, 0.2f);
                c.Ball(12f, 21f, 1.5f, 2f, Land.Leaf, -0.4f);
                c.SelOut(SilhouetteRim, cell);
                break;
            case 12: // berry bush
                Bush(c, 16, 19, 8f, false);
                foreach (var (x, y) in new[] { (12, 15), (18, 17), (14, 23), (21, 21), (10, 20), (17, 12) })
                    c.Ball(x, y, 1.3f, 1.3f, BerryR, 0.6f);
                c.SelOut(SilhouetteRim, cell);
                break;
            case 13: // snowdrift
                c.Ball(16f, 22f, 12f, 5.5f, Land.Cap, 0.6f, 0.4f);
                for (int x = 5; x < 28; x++)
                    c.Px(x, 27, Land.Cap.Shade, solid: false);
                break;
            case 14: // ice crystals
                c.GroundShadow(17, 27, 9, 2.2f, 0.25f);
                foreach (var (x, h) in new[] { (10f, 10f), (16f, 16f), (22f, 8f) })
                {
                    c.Polygon(new[] { new Vector2(x - 2.5f, 27), new Vector2(x - 2f, 27 - h + 3), new Vector2(x, 27 - h), new Vector2(x + 2f, 27 - h + 3), new Vector2(x + 2.5f, 27) },
                        IceR, (px, py) => px < x ? 1f : -0.8f);
                }
                c.SelOut(IceR.Deep, cell);
                break;
            case 15: Twigs(c, Land.Bark.Shade); break;
            case 16: Stump(c, true); c.SelOut(SilhouetteRim, cell); break;
            case 17: // young fir, snow on its boughs
                c.GroundShadow(18, 28, 7, 2f, 0.3f);
                c.Stroke(16, 25, 16, 29, 2f, Land.Bark);
                Fir(c, 16, 6, 21, 17, 0);
                c.SelOut(SilhouetteRim, cell);
                break;
            case 18: // frozen puddle
                for (int y = 0; y < 10; y++)
                    for (int x = 0; x < 22; x++)
                    {
                        float dx = (x - 10.5f) / 11f, dy = (y - 4.5f) / 5f;
                        float d = dx * dx + dy * dy;
                        if (d <= 1f)
                            c.Shade(5 + x, 16 + y, IceR, d > 0.7f ? -1f : (x - y) % 7 == 0 ? 1.5f : 0.2f, solid: false);
                    }
                break;
            case 19: RockLump(c, 16, 19, 7f); c.Ball(14f, 13f, 5f, 2f, Land.Cap, 0.6f, 1f); c.SelOut(SilhouetteRim, cell); break;
            case 20: // tracks in the snow
                for (int s = 0; s < 5; s++)
                {
                    int x = 5 + s * 5, y = 26 - s * 4 + (s % 2) * 2;
                    c.Px(x, y, g.Shade, solid: false);
                    c.Px(x + 1, y, g.Shade, solid: false);
                    c.Px(x, y + 1, g.Deep, solid: false);
                    c.Px(x + 1, y + 1, g.Shade, solid: false);
                }
                break;
            case 21: // saguaro
                c.GroundShadow(18, 28, 6, 2f, 0.3f);
                c.Column(14, 6, 5, 22, CactusR);
                c.Stroke(9, 17, 13, 17, 3.4f, CactusR);
                c.Column(8, 10, 3, 8, CactusR);
                c.Stroke(19, 13, 22, 13, 3f, CactusR, -0.3f);
                c.Column(21, 7, 3, 7, CactusR, -0.2f);
                for (int y = 8; y < 27; y += 3)
                    c.Px(16, y, CactusR.Deep);
                c.Px(16, 5, FlowerRed);
                c.SelOut(SilhouetteRim, cell);
                break;
            case 22: // barrel cactus
                c.GroundShadow(17, 27, 7, 2f, 0.3f);
                c.Ball(16f, 21f, 6.5f, 6.5f, CactusR, 0.2f);
                for (int x = 12; x <= 20; x += 4)
                    c.Stroke(x, 16, x, 26, 0.8f, CactusR, -1.2f);
                c.Ball(16f, 14.5f, 2f, 1.3f, Ramp.Of(FlowerYellow), 0.6f);
                c.Px(14, 13, FlowerRed);
                c.SelOut(SilhouetteRim, cell);
                break;
            case 23: // skull
                c.GroundShadow(17, 26, 7, 2f, 0.3f);
                c.Ball(16f, 19f, 6f, 5f, BoneR, 0.3f);
                c.Block(12, 22, 8, 3, BoneR, top: 0, bias: -0.4f);
                c.Rect(12, 18, 3, 3, SilhouetteRim);
                c.Rect(18, 18, 3, 3, SilhouetteRim);
                c.Px(16, 21, BoneR.Deep);
                for (int x = 13; x < 20; x += 2)
                    c.Px(x, 24, BoneR.Deep);
                c.SelOut(SilhouetteRim, cell);
                break;
            case 24: // ribcage half in the sand
                c.Stroke(6, 23, 27, 23, 2f, BoneR);
                for (int r = 0; r < 5; r++)
                    c.Stroke(8 + r * 4, 23, 9 + r * 4 + (r % 2), 14 + r % 2 * 2, 1.6f, BoneR, 0.3f);
                c.SelOut(BoneR.Deep, cell);
                break;
            case 25: Twigs(c, Land.Bark.Base); break; // tumbleweed
            case 26: // cracked ground
                foreach (var (x0, y0, x1, y1) in new[] { (4, 12, 14, 18), (14, 18, 26, 14), (14, 18, 16, 28), (20, 16, 24, 24), (8, 22, 14, 18) })
                    LineGround(c, x0, y0, x1, y1, Land.Ground.Deep);
                break;
            case 27: // a pile of rocks
                RockLump(c, 12, 20, 6f);
                RockLump(c, 22, 22, 4.5f);
                RockLump(c, 17, 14, 4f);
                c.SelOut(SilhouetteRim, cell);
                break;
            case 28: // ripples of wind-blown sand
                for (int r = 0; r < 4; r++)
                    for (int x = 3; x < 29; x++)
                    {
                        int y = 7 + r * 6 + (int)Mathf.Round(Mathf.Sin(x * 0.35f + r) * 1.5f);
                        c.Px(x, y, g.Light, solid: false);
                        c.Px(x, y + 1, g.Shade, solid: false);
                    }
                break;
            case 29: // a crumbling sandstone wall
                c.GroundShadow(17, 28, 13, 2.5f, 0.3f);
                for (int x = 4; x < 28; x++)
                {
                    int top = 12 + Hash(x / 3, 3, 29) % 7;
                    for (int y = top; y < 28; y++)
                    {
                        bool joint = (y - 12) % 4 == 3 || (x + (y / 4 % 2) * 3) % 7 == 0;
                        c.Shade(x, y, Land.Sand, joint ? -1.2f : y == top ? 1f : x > 25 ? -0.6f : 0f);
                    }
                }
                c.SelOut(SilhouetteRim, cell);
                break;
            case 30: // scattered bones
                c.Stroke(7, 18, 17, 18, 1.8f, BoneR, 0.3f);
                c.Ball(6f, 17f, 1.5f, 1.5f, BoneR, 0.5f); c.Ball(6f, 19.5f, 1.5f, 1.5f, BoneR);
                c.Ball(18f, 17f, 1.5f, 1.5f, BoneR, 0.5f); c.Ball(18f, 19.5f, 1.5f, 1.5f, BoneR);
                c.Stroke(15, 25, 23, 24, 1.6f, BoneR, -0.4f);
                c.SelOut(BoneR.Deep, cell);
                break;
            default: // a mixed patch of flowers
                Flowers(c, FlowerYellow, 5);
                Flowers(c, FlowerBlue, 6);
                break;
        }
    }

    /// <summary>A few flowers: a stem, four petals round a heart, a leaf.</summary>
    private static void Flowers(PixelCanvas c, Color petal, int seed)
    {
        var petals = Ramp.Of(petal, 0.6f);
        for (int f = 0; f < 5; f++)
        {
            int x = 5 + Hash(f, seed, 3) % 22, y = 6 + Hash(seed, f, 4) % 20;
            for (int s = 1; s <= 4; s++)
                c.Px(x, y + s, Land.Leaf.Base, solid: false);
            c.Px(x + 1, y + 3, Land.Leaf.Light, solid: false);
            c.Px(x - 1, y, petals.Light, solid: false);
            c.Px(x + 1, y, petals.Shade, solid: false);
            c.Px(x, y - 1, petals.Glint, solid: false);
            c.Px(x, y + 1, petals.Shade, solid: false);
            c.Px(x, y, f % 2 == 0 ? FlowerYellow : petals.Deep, solid: false);
        }
    }

    private static void Bush(PixelCanvas c, int cx, int cy, float r, bool snow)
    {
        c.GroundShadow(cx + 2, cy + r - 1, r * 1.1f, r * 0.35f, 0.3f);
        Crown(c, cx, cy, r, cx + cy, 0);
        if (snow)
            c.Ball(cx - 2, cy - r * 0.6f, r * 0.6f, 2f, Land.Cap, 0.6f, 1f);
    }

    private static void Stump(PixelCanvas c, bool snow)
    {
        c.GroundShadow(18, 27, 9, 2.5f, 0.3f);
        c.Column(10, 16, 12, 10, Land.Bark);
        c.Stroke(8, 26, 11, 24, 2f, Land.Bark, 0.2f);
        c.Stroke(24, 26, 21, 24, 2f, Land.Bark, -0.6f);
        c.Ball(16f, 16f, 6f, 2.5f, WoodR, 0.5f, 1f);
        c.Ball(16f, 16f, 3.5f, 1.4f, WoodR, -0.3f, 1f);
        c.Px(16, 16, DarkWoodR.Base);
        if (snow)
            c.Ball(15f, 14.5f, 6.5f, 2.5f, Land.Cap, 0.8f, 1f);
    }

    private static void RockLump(PixelCanvas c, int cx, int cy, float r)
    {
        c.GroundShadow(cx + 2, cy + r * 0.7f, r * 1.25f, r * 0.4f, 0.3f);
        for (int y = (int)(cy - r); y <= (int)(cy + r * 0.7f); y++)
            for (int x = (int)(cx - r - 2); x <= (int)(cx + r + 2); x++)
            {
                float dx = (x + 0.5f - cx) / (r + 1.5f), dy = (y + 0.5f - cy) / r;
                float d = dx * dx + dy * dy;
                if (d > 1f)
                    continue;
                // Facets: a lit top, a mid face, a shaded side.
                float light = dy < -0.35f ? 1.2f : dx > 0.35f ? -1f : 0.1f;
                light += (Hash(x / 2, y / 2, cx) % 3 - 1) * 0.2f;
                c.Shade(x, y, Land.Rock, light);
            }
    }

    /// <summary>A dry, leafless bush: twigs branching from the ground.</summary>
    private static void Twigs(PixelCanvas c, Color color)
    {
        c.GroundShadow(17, 27, 8, 2f, 0.25f);
        foreach (var (x0, y0, x1, y1) in new[] { (16, 27, 16, 14), (16, 21, 10, 13), (16, 19, 22, 10), (10, 13, 8, 8), (22, 10, 24, 6), (16, 15, 14, 8), (16, 23, 24, 19), (13, 17, 9, 18) })
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
}

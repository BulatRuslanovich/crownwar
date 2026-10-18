using System;
using System.Collections.Generic;
using Godot;
using Warcrown.World;

namespace Warcrown.Dev;

/// <summary>
/// The interface images, in the grim key of the rest: blackened iron plates and frames, bevelled and riveted, with
/// brass corner plates and trim; antique gold for the titles. Panels, buttons, fields, bars, the realm's arms (the logo
/// and the application icon), the engraved title and a small crown.
/// </summary>
public static partial class ArtGenerator
{
    private static readonly Color UiOutline = new("0a090c");
    private static readonly Color IronGlint = new("8c919e");
    private static readonly Color IronLight = new("5e626e");
    private static readonly Color IronMid = new("3d4049");
    private static readonly Color IronDark = new("272930");
    private static readonly Color IronDeep = new("18191d");
    private static readonly Color BrassLight = new("e0be74");
    private static readonly Color Brass = new("ad873b");
    private static readonly Color BrassDark = new("684d20");
    private static readonly Color PanelFill = new("141317");

    /// <summary>
    /// The interface's nine-slice pictures (Assets/UI/*.png, used by theme.tres): framed panels, buttons in their four
    /// states, text fields, tooltips and bar backs, drawn at two screen pixels a pixel so they are as chunky as the
    /// world; the realm's arms for the title screen, the engraved title, and the crown that marks a winner.
    /// </summary>
    private static void Interface(string uiDir)
    {
        void Save(Image image, string name) => Doubled(image).SavePng(System.IO.Path.Combine(uiDir, name + ".png"));
        Save(Panel(), "panel");
        Save(Button(IronLight, IronDark, new Color("434752"), new Color("2b2d34"), IronLight, IronGlint, pressed: false), "button_normal");
        Save(Button(BrassLight, BrassDark, new Color("4b4f5b"), new Color("30333b"), new Color("7b808e"), BrassLight, pressed: false), "button_hover");
        Save(Button(Brass, BrassDark, new Color("2a2c33"), new Color("373a43"), IronDeep, Brass, pressed: true), "button_pressed");
        Save(Button(IronDark, IronDeep, new Color("25262b"), new Color("1b1c20"), new Color("2e3036"), IronMid, pressed: false), "button_disabled");
        Save(Field(), "field");
        Save(Tooltip(), "tooltip");
        Save(BarBack(), "bar_back");
        Save(Emblem().Composite(new Color("3c6fd8")), "logo");
        Save(SmallCrown(), "crown");
        var title = Title("WARCROWN");
        title.Resize(title.GetWidth() * 3, title.GetHeight() * 3, Image.Interpolation.Nearest);
        title.SavePng(System.IO.Path.Combine(uiDir, "title.png"));
    }

    private static Image Doubled(Image image)
    {
        var copy = (Image)image.Duplicate();
        copy.Resize(image.GetWidth() * 2, image.GetHeight() * 2, Image.Interpolation.Nearest);
        return copy;
    }

    /// <summary>
    /// 32×32, slice 10: a black outline, a narrow band of iron bevelled to the light (top-left) and riveted in the middle
    /// of each side (the sides tile, so the rivets repeat along them), a brass trim inside it, and a near-black centre
    /// with a faint grain that tiles; brass plates with a rivet over the corners, which are chamfered.
    /// </summary>
    private static Image Panel()
    {
        const int size = 32, plate = 9;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        bool Corner(int x, int y)
        {
            int cx = Math.Min(x, size - 1 - x), cy = Math.Min(y, size - 1 - y);
            return cx < plate && cy < 5 || cx < 5 && cy < plate;
        }
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int edge = Math.Min(Math.Min(x, y), Math.Min(size - 1 - x, size - 1 - y));
                bool lit = x + y < size - 1;
                var color = edge switch
                {
                    0 => UiOutline,
                    1 => lit ? IronGlint : IronMid,
                    2 => lit ? IronLight : IronDark,
                    3 => lit ? Brass : BrassDark,
                    4 => UiOutline,
                    _ => Hash(x, y, 5) % 7 == 0 ? new Color("1c1a20") : Hash(x, y, 6) % 9 == 0 ? new Color("0f0e12") : PanelFill,
                };
                if (edge >= 1 && edge <= 4 && Corner(x, y))
                {
                    // A brass plate: lit where it meets the light, dark where it turns away.
                    bool upLeft = !Corner(x - 1, y) || !Corner(x, y - 1) || x == 1 || y == 1;
                    bool downRight = !Corner(x + 1, y) || !Corner(x, y + 1) || x == size - 2 || y == size - 2;
                    color = downRight ? BrassDark : upLeft ? BrassLight : Brass;
                }
                if (edge == 0 && Corner(x, y) && (Math.Min(x, size - 1 - x) + Math.Min(y, size - 1 - y)) < 2)
                    color = Colors.Transparent; // chamfered corners
                image.SetPixel(x, y, color with { A = color.A > 0 && edge >= 5 ? 0.96f : color.A });
            }
        // Rivets: on each corner plate, and in the middle of each side of the band.
        foreach (var (rx, ry) in new[] { (2, 2), (size - 4, 2), (2, size - 4), (size - 4, size - 4) })
            Rivet(image, rx, ry, BrassLight, Brass, BrassDark);
        foreach (var (rx, ry) in new[] { (size / 2 - 1, 1), (size / 2 - 1, size - 3), (1, size / 2 - 1), (size - 3, size / 2 - 1) })
            Rivet(image, rx, ry, IronGlint, IronLight, IronDeep);
        return image;
    }

    /// <summary>A 2×2 rivet head at (x, y): lit top-left, shaded bottom-right.</summary>
    private static void Rivet(Image image, int x, int y, Color light, Color mid, Color dark)
    {
        image.SetPixel(x, y, light);
        image.SetPixel(x + 1, y, mid);
        image.SetPixel(x, y + 1, mid);
        image.SetPixel(x + 1, y + 1, dark);
    }

    /// <summary>
    /// 24×24, slice 8: an iron plate inside a black outline and a rim (<paramref name="rimLight"/> on the lit sides,
    /// <paramref name="rimDark"/> on the others), its face shading from <paramref name="faceTop"/> to
    /// <paramref name="faceBottom"/> under a lit top edge, a rivet in each corner. Pressed, the light edge goes to the
    /// bottom, as if the plate were pushed in. The middle is plain: it is stretched, and grain would smear into streaks.
    /// </summary>
    private static Image Button(Color rimLight, Color rimDark, Color faceTop, Color faceBottom, Color edge, Color rivet, bool pressed)
    {
        const int size = 24;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int rim = Math.Min(Math.Min(x, y), Math.Min(size - 1 - x, size - 1 - y));
                if (rim == 0 && (x == 0 || x == size - 1) && (y == 0 || y == size - 1))
                    continue; // chamfered corners
                Color color;
                if (rim == 0)
                    color = UiOutline;
                else if (rim == 1)
                    color = x + y < size - 1 ? rimLight : rimDark;
                else if (y == (pressed ? size - 3 : 2))
                    color = edge;
                else if (y == (pressed ? 2 : size - 3))
                    color = UiOutline.Lerp(faceBottom, 0.4f);
                else
                    color = faceTop.Lerp(faceBottom, (y - 3f) / (size - 7f));
                image.SetPixel(x, y, color);
            }
        foreach (var (rx, ry) in new[] { (3, 4), (size - 4, 4), (3, size - 5), (size - 4, size - 5) })
        {
            image.SetPixel(rx, ry, rivet);
            image.SetPixel(rx, ry + 1, UiOutline.Lerp(faceBottom, 0.3f));
        }
        return image;
    }

    /// <summary>16×16, slice 5: a field sunk into iron: shadowed along the top and left, catching light along the bottom and right.</summary>
    private static Image Field()
    {
        const int size = 16;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int edge = Math.Min(Math.Min(x, y), Math.Min(size - 1 - x, size - 1 - y));
                var color = edge == 0 ? (x == 0 || y == 0 ? IronDark : IronLight)
                    : edge == 1 ? (x == 1 || y == 1 ? new Color("050406") : IronDark)
                    : new Color("0c0b0e");
                image.SetPixel(x, y, color);
            }
        return image;
    }

    /// <summary>12×12, slice 4: a dark plate in a thin brass rim.</summary>
    private static Image Tooltip()
    {
        const int size = 12;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int edge = Math.Min(Math.Min(x, y), Math.Min(size - 1 - x, size - 1 - y));
                var color = edge == 0 ? UiOutline : edge == 1 ? (x + y < size - 1 ? Brass : BrassDark) : PanelFill with { A = 0.97f };
                if ((x == 0 || x == size - 1) && (y == 0 || y == size - 1))
                    color = Colors.Transparent;
                image.SetPixel(x, y, color);
            }
        return image;
    }

    /// <summary>8×8, slice 3: the dark groove a bar fills, in an iron rim.</summary>
    private static Image BarBack()
    {
        const int size = 8;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool border = x == 0 || y == 0 || x == size - 1 || y == size - 1;
                image.SetPixel(x, y, border ? (y == size - 1 || x == size - 1 ? IronLight : IronDark) : y == 1 ? new Color("040305") : new Color("0e0d10"));
            }
        return image;
    }

    /// <summary>
    /// The realm's arms, 64 px square (faction colour in the mask): a heater shield bordered in gold with the gilded lily
    /// on its field, two swords crossed behind it and a gold crown on top. The application icon and the title screen's
    /// emblem.
    /// </summary>
    private static PixelCanvas Emblem()
    {
        const int size = 64;
        var c = new PixelCanvas(size, size);
        // Two swords crossed behind the shield: the points out at the top corners, the hilts at the bottom ones.
        foreach (int side in new[] { -1, 1 })
        {
            var guard = new Vector2(32 - side * 20, 50);
            var point = new Vector2(32 + side * 25, 5);
            var along = (point - guard).Normalized();
            var across = new Vector2(-along.Y, along.X);
            c.Taper(guard.X, guard.Y, point.X, point.Y, 5f, 1.2f, SteelR, 0.3f);
            c.Stroke(guard.X + along.X * 2, guard.Y + along.Y * 2, point.X - along.X * 6, point.Y - along.Y * 6, 1f, SteelR, 1.6f); // the fuller's glint
            var grip = guard - along * 7f;
            c.Stroke(guard.X, guard.Y, grip.X, grip.Y, 2.6f, LeatherR, -0.2f);
            c.Stroke(guard.X - across.X * 6, guard.Y - across.Y * 6, guard.X + across.X * 6, guard.Y + across.Y * 6, 2.6f, GoldR, 0.3f);
            c.Ball(grip.X - along.X * 1.5f, grip.Y - along.Y * 1.5f, 2.4f, 2.4f, GoldR, 0.5f);
        }
        // The shield: a gold border, the field in faction colours lit from the left, the lily in the middle.
        Vector2[] Heater(float inset)
        {
            var points = new List<Vector2> { new(14 + inset, 17 + inset), new(50 - inset, 17 + inset), new(50 - inset, 33) };
            for (int i = 1; i <= 8; i++)
            {
                float t = i / 8f;
                points.Add(new Vector2(Mathf.Lerp(50 - inset, Mathf.Lerp(50 - inset, 32, t), t),
                    Mathf.Lerp(Mathf.Lerp(33, 50, t), Mathf.Lerp(50, 60 - inset * 1.4f, t), t)));
            }
            for (int i = 7; i >= 1; i--)
            {
                var q = points[3 + i - 1];
                points.Add(new Vector2(64 - q.X, q.Y));
            }
            points.Add(new Vector2(14 + inset, 33));
            return points.ToArray();
        }
        c.Polygon(Heater(0f), GoldR, (x, y) => 0.9f - (x - 14) / 36f * 1.6f + (y < 20 ? 0.6f : 0f));
        c.Polygon(Heater(2.5f), null, (x, y) => 0.5f - (x - 16) / 32f * 1.9f + (y < 23 ? 0.3f : 0f) - (y - 20) * 0.012f);
        string[] lily =
        {
            "..#..",
            ".###.",
            "#.#.#",
            "#####",
            "..#..",
            ".#.#.",
        };
        // Its shadow on the field first, a pixel down and right, so it stands out.
        foreach (bool shadow in new[] { true, false })
            for (int j = 0; j < lily.Length; j++)
                for (int i = 0; i < 5; i++)
                    if (lily[j][i] == '#')
                        for (int k = 0; k < 4; k++)
                        {
                            int x = 27 + i * 2 + k % 2, y = 29 + j * 2 + k / 2;
                            if (shadow)
                                c.TeamLit(x + 1, y + 1, -2f);
                            else
                                c.Shade(x, y, GoldR, (j < 2 ? 1.3f : i < 2 ? 0.7f : 0f) + (k == 0 ? 0.4f : k == 3 ? -0.5f : 0f));
                        }
        // The crown over the shield: five points with pearls, a band set with rubies.
        var ruby = Ramp.Of(new Color("b02438"));
        foreach (var (x, tip) in new[] { (19.5f, 7f), (25.75f, 5f), (32f, 2.5f), (38.25f, 5f), (44.5f, 7f) })
        {
            c.Polygon(new[] { new Vector2(x - 3.4f, 14), new Vector2(x, tip + 1.5f), new Vector2(x + 3.4f, 14) }, GoldR,
                (px, py) => px + 0.5f < x ? 1.1f : -0.2f);
            c.Ball(x, tip, 1.7f, 1.7f, FeatherR, 0.6f);
        }
        c.Block(17, 13, 30, 7, GoldR, top: 1, bias: 0.2f);
        for (int x = 17; x < 47; x++)
            c.Shade(x, 19, GoldR, -1.2f);
        foreach (float x in new[] { 23f, 32f, 41f })
            c.Ball(x, 16f, 1.8f, 1.6f, ruby, 0.6f);
        c.RimLight(new Rect2I(0, 0, size, size), SkyRim, 0.3f);
        c.SelOut(SilhouetteRim);
        return c;
    }

    /// <summary>13×7: a little gold crown with pearls and rubies, marking a winner on the end screen.</summary>
    private static Image SmallCrown()
    {
        string[] rows =
        {
            "p.....p.....p",
            "l....lgl....g",
            "gl..lgggd..dg",
            "ggllgggggddgg",
            "lllllllllllll",
            "grgggglggggrg",
            "ddddddddddddd",
        };
        var c = new PixelCanvas(rows[0].Length + 2, rows.Length + 2);
        for (int y = 0; y < rows.Length; y++)
            for (int x = 0; x < rows[y].Length; x++)
            {
                var color = rows[y][x] switch
                {
                    'p' => new Color("fff4dc"),
                    'l' => GoldR.Light,
                    'g' => GoldR.Base,
                    'd' => GoldR.Shade,
                    'r' => new Color("c02840"),
                    _ => Colors.Transparent,
                };
                c.Px(x + 1, y + 1, color);
            }
        c.Outline(UiOutline);
        return c.Base;
    }

    /// <summary>
    /// The title, engraved in antique gold: capitals with serifs, thick and thin strokes as a chisel cuts them, lit
    /// along their tops, shaded towards their feet, outlined and casting a shadow. Only the letters of the name are drawn.
    /// </summary>
    private static Image Title(string text)
    {
        // Each letter: its advance and its strokes (x0, y0, x1, y1, width) in a box 12 px high.
        var glyphs = new Dictionary<char, (int Advance, (float, float, float, float, float)[] Strokes)>
        {
            ['A'] = (11, new[] { (5f, 0.5f, 1.5f, 11f, 1.2f), (5.5f, 0.5f, 9f, 11f, 2.4f), (3f, 7.5f, 7.5f, 7.5f, 1f), (0f, 11.5f, 3f, 11.5f, 1f), (7.5f, 11.5f, 10.5f, 11.5f, 1f), (4.5f, 0.5f, 6f, 0.5f, 1f) }),
            ['W'] = (15, new[] { (1f, 0.5f, 4f, 11f, 2.4f), (4f, 11f, 7f, 2f, 1.2f), (7f, 2f, 10f, 11f, 2.4f), (10f, 11f, 13f, 0.5f, 1.2f), (0f, 0.5f, 2.5f, 0.5f, 1f), (11.5f, 0.5f, 14.5f, 0.5f, 1f) }),
            ['R'] = (11, new[] { (2f, 0.5f, 2f, 11f, 2.4f), (1f, 0.5f, 6.5f, 0.5f, 1f), (6.5f, 0.5f, 8.5f, 2.5f, 1.2f), (8.5f, 2.5f, 8.5f, 4f, 2.2f), (8.5f, 4f, 6.5f, 6f, 1.2f), (6.5f, 6f, 2f, 6f, 1f), (4.5f, 6f, 8.5f, 11f, 2.2f), (0f, 11.5f, 4f, 11.5f, 1f), (7.5f, 11.5f, 10.5f, 11.5f, 1f) }),
            ['C'] = (10, new[] { (8.5f, 0.5f, 4f, 0.5f, 1f), (4f, 0.5f, 1.8f, 2.8f, 1.4f), (1.6f, 3f, 1.6f, 8.5f, 2.4f), (1.8f, 8.8f, 4f, 11f, 1.4f), (4f, 11.2f, 8.5f, 11.2f, 1f), (8.5f, 0.5f, 8.5f, 3f, 1f) }),
            ['O'] = (11, Array.Empty<(float, float, float, float, float)>()),
            ['N'] = (12, new[] { (1.5f, 0.5f, 1.5f, 11f, 1.2f), (1.5f, 0.5f, 9.5f, 11f, 2.4f), (9.5f, 0f, 9.5f, 11f, 1.2f), (0f, 0.5f, 2.5f, 0.5f, 1f), (8f, 0.5f, 11f, 0.5f, 1f), (0f, 11.5f, 3f, 11.5f, 1f) }),
        };
        const int top = 2, height = 12;
        int width = 4;
        foreach (char ch in text)
            width += glyphs[ch].Advance + 1;
        var shape = new PixelCanvas(width, height + 5);
        int pen = 2;
        foreach (char ch in text)
        {
            var (advance, strokes) = glyphs[ch];
            foreach (var (x0, y0, x1, y1, w) in strokes)
                shape.Stroke(pen + x0, top + y0, pen + x1, top + y1, w, GoldR);
            if (ch == 'O')
                for (int y = 0; y <= height; y++)
                    for (int x = 0; x <= 10; x++)
                    {
                        float ox = (x + 0.5f - 5f) / 4.7f, oy = (y + 0.5f - 6f) / 5.9f;
                        float ix = (x + 0.5f - 5f) / 2.5f, iy = (y + 0.5f - 6f) / 4.7f;
                        if (ox * ox + oy * oy <= 1f && ix * ix + iy * iy > 1f)
                            shape.Px(pen + x, top + y, GoldR.Base);
                    }
            pen += advance + 1;
        }
        // Shade the letters as cut gold, with a shadow under them, and outline them.
        var c = new PixelCanvas(width, height + 5);
        bool Solid(int x, int y) => shape.IsSolid(x, y);
        for (int y = 0; y < c.Height; y++)
            for (int x = 0; x < c.Width; x++)
                if (Solid(x, y) && !Solid(x + 1, y + 1))
                    c.Px(x + 1, y + 1, new Color("120c08"), solid: false);
        for (int y = 0; y < c.Height; y++)
            for (int x = 0; x < c.Width; x++)
            {
                if (!Solid(x, y))
                    continue;
                float light = 1.3f - 2.4f * (y - top) / height + (!Solid(x, y - 1) ? 0.8f : 0f) - (!Solid(x, y + 1) ? 0.7f : 0f)
                              + (!Solid(x - 1, y) ? 0.3f : 0f);
                c.Shade(x, y, GoldR, light);
            }
        c.SelOut(UiOutline);
        return c.Base;
    }
}

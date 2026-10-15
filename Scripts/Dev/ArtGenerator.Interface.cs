using System;
using Godot;
using Lordsio.World;

namespace Lordsio.Dev;

/// <summary>The interface images: panels, buttons, fields, bars, the logo and the splash screen.</summary>
public static partial class ArtGenerator
{
    private static readonly Color UiOutline = new("140d08");
    private static readonly Color UiGold = new("d9b060");
    private static readonly Color UiGoldDark = new("8a6430");
    private static readonly Color UiGoldDeep = new("5a3e1c");
    private static readonly Color UiLeather = new("2b2017");
    private static readonly Color UiLeatherDark = new("231910");
    private static readonly Color UiWood = new("5a3f24");
    private static readonly Color UiWoodLight = new("7a5832");
    private static readonly Color UiWoodDark = new("3a2715");

    /// <summary>
    /// The interface's nine-slice pictures (Assets/UI/*.png, used by theme.tres): framed panels, buttons in their four
    /// states, text fields, tooltips and bar backs, drawn at two screen pixels a pixel so they are as chunky as the
    /// world; and the crown logo of the title screen.
    /// </summary>
    private static void Interface(string uiDir)
    {
        void Save(Image image, string name) => Doubled(image).SavePng(System.IO.Path.Combine(uiDir, name + ".png"));
        Save(Panel(), "panel");
        Save(Button(UiWood, UiWoodLight, UiWoodDark, UiGoldDeep, pressed: false), "button_normal");
        Save(Button(new Color("6a4a2a"), new Color("8f6a3c"), UiWoodDark, UiGold, pressed: false), "button_hover");
        Save(Button(new Color("4a321b"), UiWoodDark, new Color("6a4a2a"), UiGold, pressed: true), "button_pressed");
        Save(Button(new Color("3a3029"), new Color("4a3e35"), new Color("2a221c"), new Color("3a2e22"), pressed: false), "button_disabled");
        Save(Field(), "field");
        Save(Tooltip(), "tooltip");
        Save(BarBack(), "bar_back");
        Logo().SavePng(System.IO.Path.Combine(uiDir, "logo.png"));
    }

    private static Image Doubled(Image image)
    {
        var copy = (Image)image.Duplicate();
        copy.Resize(image.GetWidth() * 2, image.GetHeight() * 2, Image.Interpolation.Nearest);
        return copy;
    }

    /// <summary>
    /// 24×24, slice 6: a dark outline, a bevelled gold frame (lit top-left), a dark groove, rivets in the corners and a
    /// leather centre with a faint weave that tiles.
    /// </summary>
    private static Image Panel()
    {
        const int size = 24;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int edge = Math.Min(Math.Min(x, y), Math.Min(size - 1 - x, size - 1 - y));
                bool lit = x + y < size - 1;
                var color = edge switch
                {
                    0 => UiOutline,
                    1 => lit ? UiGold : UiGoldDark,
                    2 => lit ? UiGoldDark : UiGoldDeep,
                    3 => UiOutline,
                    _ => (x + y) % 4 == 0 || (x - y + 64) % 4 == 0 ? UiLeatherDark : UiLeather,
                };
                // Corners cut off, so the frame looks forged rather than ruled.
                if ((x == 0 || x == size - 1) && (y == 0 || y == size - 1))
                    color = Colors.Transparent;
                image.SetPixel(x, y, color with { A = color.A > 0 && edge >= 4 ? 0.95f : color.A });
            }
        foreach (var (rx, ry) in new[] { (1, 1), (size - 3, 1), (1, size - 3), (size - 3, size - 3) })
        {
            image.SetPixel(rx, ry, UiGold);
            image.SetPixel(rx + 1, ry, UiGoldDark);
            image.SetPixel(rx, ry + 1, UiGoldDark);
            image.SetPixel(rx + 1, ry + 1, UiGoldDeep);
        }
        return image;
    }

    /// <summary>
    /// 16×16, slice 4: a raised wooden plate with a lit top edge and a thick dark lip below, framed in
    /// <paramref name="rim"/>; pressed, the lip moves to the top, as if pushed in.
    /// </summary>
    private static Image Button(Color face, Color light, Color dark, Color rim, bool pressed)
    {
        const int size = 16;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool border = x == 0 || y == 0 || x == size - 1 || y == size - 1;
                bool corner = (x == 0 || x == size - 1) && (y == 0 || y == size - 1);
                if (corner)
                    continue;
                Color color;
                if (border)
                    color = UiOutline;
                else if (x == 1 || x == size - 2 || y == 1 || y == size - 2)
                    color = rim;
                else if (pressed ? y <= 3 : y >= size - 4)
                    color = dark;
                else if (pressed ? y == size - 3 : y == 2)
                    color = light;
                else
                    color = face; // plain: the middle is stretched, so any grain would smear into streaks
                image.SetPixel(x, y, color);
            }
        return image;
    }

    /// <summary>16×16, slice 4: a sunken dark field, shadowed at the top-left and lit at the bottom-right.</summary>
    private static Image Field()
    {
        const int size = 16;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool border = x == 0 || y == 0 || x == size - 1 || y == size - 1;
                var color = border ? UiGoldDeep
                    : x == 1 || y == 1 ? new Color("070504")
                    : x == size - 2 || y == size - 2 ? new Color("2a2016")
                    : new Color("120c08");
                image.SetPixel(x, y, color);
            }
        return image;
    }

    /// <summary>12×12, slice 3: a thin gold-rimmed dark plate.</summary>
    private static Image Tooltip()
    {
        const int size = 12;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int edge = Math.Min(Math.Min(x, y), Math.Min(size - 1 - x, size - 1 - y));
                var color = edge == 0 ? UiOutline : edge == 1 ? (x + y < size - 1 ? UiGold : UiGoldDark) : new Color(0.1f, 0.075f, 0.055f, 0.97f);
                if ((x == 0 || x == size - 1) && (y == 0 || y == size - 1))
                    color = Colors.Transparent;
                image.SetPixel(x, y, color);
            }
        return image;
    }

    /// <summary>8×8, slice 2: the dark groove a bar fills.</summary>
    private static Image BarBack()
    {
        const int size = 8;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool border = x == 0 || y == 0 || x == size - 1 || y == size - 1;
                image.SetPixel(x, y, border ? UiGoldDeep : y == 1 ? new Color("050302") : new Color("0f0a06"));
            }
        return image;
    }

    /// <summary>
    /// 64×40 (shown enlarged): a golden crown with red and blue gems, over two crossed swords — the title's emblem.
    /// </summary>
    private static Image Logo()
    {
        var c = new PixelCanvas(64, 40);
        var ruby = new Color("d0303a");
        var sapphire = new Color("3c6fd8");
        // Crossed swords behind the crown, blades up, hilts in the lower corners.
        for (int i = 0; i < 30; i++)
        {
            c.Px(13 + i, 33 - i, SteelLight);
            c.Px(14 + i, 33 - i, SteelDark);
            c.Px(50 - i, 33 - i, SteelLight);
            c.Px(49 - i, 33 - i, SteelDark);
        }
        foreach (int side in new[] { 1, -1 })
        {
            int hx = side == 1 ? 13 : 50;
            for (int g = -2; g <= 2; g++)
                c.Px(hx + g * side, 33 + g, g == 0 ? Gold : GoldDark); // cross-guard
            c.Px(hx - side, 35, Leather);
            c.Px(hx - 2 * side, 36, Leather);
            c.Px(hx - 3 * side, 37, Gold); // pommel
        }
        // The crown: a band with three points, each tipped with a ball.
        for (int y = 0; y < 18; y++)
        {
            for (int x = 0; x < 34; x++)
            {
                int px = 15 + x, py = 8 + y;
                bool band = y >= 11;
                int tooth = Math.Min(Math.Min(Math.Abs(x - 2), Math.Abs(x - 17)), Math.Abs(x - 31));
                bool point = !band && y >= tooth * 0.9f + (x is > 6 and < 27 && Math.Abs(x - 17) > 3 ? 2f : 0f);
                if (!band && !point)
                    continue;
                var color = y == 11 || y == 17 ? GoldDark : x < 17 ? Gold : new Color("e8b830");
                if (band && y > 12 && y < 16 && (x + 1) % 4 == 0)
                    color = GoldDark;
                c.Px(px, py, color);
            }
        }
        foreach (int x in new[] { 17, 32, 46 })
        {
            c.Disc(x, 7, 1.6f, Gold);
            c.Px(x - 1, 6, new Color("fff1a8"));
        }
        c.Rect(23, 21, 3, 3, ruby);
        c.Rect(30, 20, 4, 4, sapphire);
        c.Rect(38, 21, 3, 3, ruby);
        c.Px(23, 21, new Color("ff8a8a"));
        c.Px(30, 20, new Color("9fc0ff"));
        c.Px(38, 21, new Color("ff8a8a"));
        c.Outline(Outline);
        return c.Base;
    }
}

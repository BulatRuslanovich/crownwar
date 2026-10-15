using Godot;

namespace Lordsio.UI;

/// <summary>
/// Pixel-art mouse cursors drawn in code: a gold arrow for everything, and a sword (on the
/// <see cref="Input.CursorShape.Cross"/> shape) when a right click would attack.
/// </summary>
public static class Cursors
{
    public const Input.CursorShape Attack = Input.CursorShape.Cross;

    private static readonly string[] ArrowShape =
    {
        "x...........",
        "xx..........",
        "xhx.........",
        "xhhx........",
        "xhhhx.......",
        "xhhhhx......",
        "xhhhhhx.....",
        "xhhhhhhx....",
        "xhhhhhhhx...",
        "xhhhhxxxxx..",
        "xhhxhx......",
        "xhx.xhx.....",
        "xx..xhx.....",
        "x....xhx....",
        ".....xxx....",
    };

    private static readonly string[] SwordShape =
    {
        "s...........",
        ".s..........",
        "..s.........",
        "...s........",
        "....s.......",
        ".....s......",
        "......s..g..",
        ".......s.g..",
        "........gg..",
        ".....gggg...",
        "........hh..",
        ".........hh.",
        "..........p.",
    };

    /// <summary>
    /// Back to the system cursors. Called before quitting: the cursor textures must be released while the
    /// renderer still exists, or they are reported as leaked at exit.
    /// </summary>
    public static void Uninstall()
    {
        Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
        Input.SetCustomMouseCursor(null, Input.CursorShape.Arrow);
        Input.SetCustomMouseCursor(null, Attack);
    }

    /// <summary>Installs the cursors for the whole application (two screen pixels per art pixel).</summary>
    public static void Install()
    {
        Input.SetCustomMouseCursor(Draw(ArrowShape, 2), Input.CursorShape.Arrow, Vector2.Zero);
        Input.SetCustomMouseCursor(Draw(SwordShape, 2), Attack, new Vector2(1, 1));
    }

    private static Texture2D Draw(string[] rows, int scale)
    {
        var outline = UiPalette.Outline;
        int w = rows[0].Length + 2, h = rows.Length + 2;
        var image = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        for (int y = 0; y < rows.Length; y++)
        {
            for (int x = 0; x < rows[y].Length; x++)
            {
                var c = rows[y][x] switch
                {
                    'x' => outline,
                    'h' => new Color("f2d06a"),   // gold arrow
                    's' => new Color("dfe2e7"),   // steel blade
                    'g' => new Color("c9a050"),   // crossguard
                    'p' => UiPalette.Gold,   // pommel
                    _ => new Color("6b4426") with { A = rows[y][x] == '.' ? 0 : 1 }, // grip
                };
                if (c.A > 0)
                    image.SetPixel(x + 1, y + 1, c);
            }
        }
        // A dark rim around every drawn pixel keeps the cursor readable on grass and sand alike.
        var rim = (Image)image.Duplicate();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (image.GetPixel(x, y).A == 0 && Neighbour(image, x, y))
                    rim.SetPixel(x, y, outline);
        rim.Resize(w * scale, h * scale, Image.Interpolation.Nearest);
        return ImageTexture.CreateFromImage(rim);
    }

    private static bool Neighbour(Image image, int x, int y)
    {
        foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
        {
            int nx = x + dx, ny = y + dy;
            if (nx >= 0 && ny >= 0 && nx < image.GetWidth() && ny < image.GetHeight() && image.GetPixel(nx, ny).A > 0)
                return true;
        }
        return false;
    }
}

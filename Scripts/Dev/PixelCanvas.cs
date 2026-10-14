using Godot;

namespace Lordsio.Dev;

/// <summary>
/// Drawing surface for the art generator: a colour layer plus a faction-colour mask layer of the
/// same size. Mask pixels are greyscale (white = full faction colour, grey = shaded); the game tints
/// them with the owner's colour. An outline pass rings everything drawn with a dark border.
/// </summary>
public sealed class PixelCanvas
{
    private readonly bool[] _occupied;

    public PixelCanvas(int width, int height)
    {
        Width = width;
        Height = height;
        Base = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        Mask = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        Glow = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        _occupied = new bool[width * height];
    }

    public int Width { get; }
    public int Height { get; }
    public Image Base { get; }
    public Image Mask { get; }
    /// <summary>What shines at night (lit windows, fires): drawn over the building, additively, after dusk.</summary>
    public Image Glow { get; }

    /// <summary>Whether anything was marked with <see cref="Lit"/>.</summary>
    public bool HasGlow { get; private set; }

    /// <summary>Offset added to every drawing call, so frames can be drawn in sheet cells with local coordinates.</summary>
    public Vector2I Origin { get; set; }

    /// <param name="solid">Background pixels (ground, water) are not outlined; solid ones (objects) are.</param>
    public void Px(int x, int y, Color color, bool solid = true)
    {
        x += Origin.X;
        y += Origin.Y;
        if (x < 0 || y < 0 || x >= Width || y >= Height)
            return;
        if (color.A <= 0f)
            return;
        Base.SetPixel(x, y, color);
        Mask.SetPixel(x, y, Colors.Transparent);
        _occupied[y * Width + x] = solid;
    }

    /// <summary>Fills a rectangle as background (never outlined).</summary>
    public void Ground(int x, int y, int w, int h, Color color)
    {
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
                Px(x + i, y + j, color, solid: false);
    }

    /// <summary>A faction-coloured pixel; <paramref name="shade"/> 1 = full colour, lower = darker.</summary>
    public void Team(int x, int y, float shade = 1f)
    {
        x += Origin.X;
        y += Origin.Y;
        if (x < 0 || y < 0 || x >= Width || y >= Height)
            return;
        Base.SetPixel(x, y, Colors.Transparent);
        Mask.SetPixel(x, y, new Color(shade, shade, shade));
        _occupied[y * Width + x] = true;
    }

    /// <summary>Marks a rectangle as shining at night in <paramref name="color"/> (the day picture is not changed).</summary>
    public void Lit(int x, int y, int w, int h, Color color)
    {
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
            {
                int px = x + i + Origin.X, py = y + j + Origin.Y;
                if (px < 0 || py < 0 || px >= Width || py >= Height)
                    continue;
                Glow.SetPixel(px, py, color);
                HasGlow = true;
            }
    }

    public void Rect(int x, int y, int w, int h, Color color)
    {
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
                Px(x + i, y + j, color);
    }

    /// <summary>Faction-coloured rectangle, lit from the top-left: lighter top row and left column, darker bottom row.</summary>
    public void TeamRect(int x, int y, int w, int h)
    {
        for (int j = 0; j < h; j++)
        {
            for (int i = 0; i < w; i++)
            {
                float shade = j == 0 || i == 0 ? 1f : j == h - 1 || i == w - 1 ? 0.62f : 0.82f;
                Team(x + i, y + j, shade);
            }
        }
    }

    /// <summary>Rectangle with a highlight on top/left and a shadow on bottom/right.</summary>
    public void Shaded(int x, int y, int w, int h, Color mid, Color light, Color dark)
    {
        Rect(x, y, w, h, mid);
        Rect(x, y, w, 1, light);
        Rect(x, y, 1, h, light);
        Rect(x, y + h - 1, w, 1, dark);
        Rect(x + w - 1, y, 1, h, dark);
    }

    public void Disc(int cx, int cy, float radius, Color color)
    {
        int r = (int)Mathf.Ceil(radius);
        for (int y = -r; y <= r; y++)
            for (int x = -r; x <= r; x++)
                if (x * x + y * y <= radius * radius)
                    Px(cx + x, cy + y, color);
    }

    public void Line(int x0, int y0, int x1, int y1, Color color)
    {
        int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        while (true)
        {
            Px(x0, y0, color);
            if (x0 == x1 && y0 == y1)
                break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    /// <summary>
    /// Rings every drawn shape inside the rectangle with <paramref name="color"/> (4-neighbourhood),
    /// the classic pixel-art outline. Works on the whole canvas when no rectangle is given.
    /// </summary>
    public void Outline(Color color, Rect2I? area = null)
    {
        var r = area ?? new Rect2I(0, 0, Width, Height);
        var add = new System.Collections.Generic.List<int>();
        for (int y = r.Position.Y; y < r.End.Y; y++)
        {
            for (int x = r.Position.X; x < r.End.X; x++)
            {
                if (_occupied[y * Width + x])
                    continue;
                bool edge = (x > r.Position.X && _occupied[y * Width + x - 1])
                            || (x < r.End.X - 1 && _occupied[y * Width + x + 1])
                            || (y > r.Position.Y && _occupied[(y - 1) * Width + x])
                            || (y < r.End.Y - 1 && _occupied[(y + 1) * Width + x]);
                if (edge)
                    add.Add(y * Width + x);
            }
        }
        foreach (int i in add)
        {
            Base.SetPixel(i % Width, i / Width, color);
            _occupied[i] = true;
        }
    }

    public void Save(string basePath, string? maskPath = null, string? glowPath = null)
    {
        Base.SavePng(basePath);
        if (maskPath != null)
            Mask.SavePng(maskPath);
        if (glowPath != null && HasGlow)
            Glow.SavePng(glowPath);
    }
}

using System;
using Godot;

namespace Warcrown.Dev;

/// <summary>
/// A colour ramp of one material, from the deepest shadow to the highlight. In the moody, high-contrast look of the
/// game's art (think Dead Cells, in a medieval key) shadows fall steeply into deep indigo, the mid tone keeps its full
/// colour, and lights warm towards candle-yellow — the hue shift that keeps shaded pixel art from looking muddy.
/// </summary>
public readonly record struct Ramp(Color Deep, Color Shade, Color Base, Color Light, Color Glint)
{
    private static readonly Color Cool = new("140f30");
    private static readonly Color Dusk = new("2c2550");
    private static readonly Color Warm = new("ffe9b0");

    /// <summary>
    /// The ramp around <paramref name="base"/>: two steps darker and cooler, two lighter and warmer. <paramref name="mood"/>
    /// (0..1) sinks the mid and shadow tones towards the dusk but leaves the lights: figures and buildings are drawn
    /// moodier than the ground under them, which must stay easy to read.
    /// </summary>
    public static Ramp Of(Color @base, float contrast = 1f, float mood = 0f)
    {
        // A little more saturation in the mid tone, so the colour holds against the dark shadows; the mood darkens the
        // middle and the shadows but leaves the lights bright, for contrast.
        var vivid = Color.FromHsv(@base.H, Mathf.Clamp(@base.S * (1f + 0.15f * contrast), 0f, 1f), @base.V);
        var mid = vivid.Lerp(Dusk, mood * 0.6f);
        return new(
            mid.Lerp(Cool, Mathf.Min(1f, (0.66f + mood * 0.3f) * contrast)),
            mid.Lerp(Dusk, Mathf.Min(1f, (0.38f + mood * 0.4f) * contrast)),
            mid,
            vivid.Lerp(Warm, 0.26f * contrast),
            vivid.Lerp(Warm, 0.58f * contrast));
    }

    /// <summary>The tone for a light level from -2 (deep shadow) to 2 (highlight).</summary>
    public Color this[int tone] => tone switch
    {
        <= -2 => Deep,
        -1 => Shade,
        0 => Base,
        1 => Light,
        _ => Glint,
    };
}

/// <summary>
/// Drawing surface for the art generator: a colour layer plus a faction-colour mask layer of the
/// same size. Mask pixels are greyscale (white = full faction colour, grey = shaded); the game tints
/// them with the owner's colour. Shapes are lit from one sun at the top-left: the shading helpers take a light level
/// per pixel and dither between the tones of a <see cref="Ramp"/>. An outline pass rings everything drawn: in a darker
/// shade of what it borders ("selective" outline), dark only along the bottom where the shape meets its shadow.
/// </summary>
public sealed class PixelCanvas
{
    private readonly bool[] _occupied;

    /// <summary>4×4 ordered (Bayer) dither thresholds in (0, 1).</summary>
    private static readonly float[] Bayer =
    {
        0.5f / 16, 8.5f / 16, 2.5f / 16, 10.5f / 16,
        12.5f / 16, 4.5f / 16, 14.5f / 16, 6.5f / 16,
        3.5f / 16, 11.5f / 16, 1.5f / 16, 9.5f / 16,
        15.5f / 16, 7.5f / 16, 13.5f / 16, 5.5f / 16,
    };

    /// <summary>Shades of the faction-colour mask from deep shadow to highlight (the mask can only darken the colour).</summary>
    public static readonly float[] TeamTones = { 0.42f, 0.6f, 0.78f, 0.92f, 1f };

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

    /// <summary>When set, nothing is drawn outside this rectangle (canvas pixels): a sheet's frame cannot spill into the next.</summary>
    public Rect2I? Clip { get; set; }

    /// <summary>Whether local (x, y) is on the canvas (and inside <see cref="Clip"/>).</summary>
    private bool Inside(int x, int y, out int ax, out int ay)
    {
        ax = x + Origin.X;
        ay = y + Origin.Y;
        return ax >= 0 && ay >= 0 && ax < Width && ay < Height && (Clip is not { } clip || clip.HasPoint(new Vector2I(ax, ay)));
    }

    /// <param name="solid">Background pixels (ground, water) are not outlined; solid ones (objects) are.</param>
    public void Px(int x, int y, Color color, bool solid = true)
    {
        if (!Inside(x, y, out x, out y) || color.A <= 0f)
            return;
        if (color.A < 1f)
        {
            // Translucent paint (a shadow) darkens what is under it.
            var under = Base.GetPixel(x, y);
            color = under.A > 0f ? new Color(under.Lerp(color with { A = 1f }, color.A), under.A) : color;
        }
        Base.SetPixel(x, y, color);
        Mask.SetPixel(x, y, Colors.Transparent);
        _occupied[y * Width + x] = solid;
    }

    /// <summary>The colour at local (x, y) (transparent off the canvas or where faction colour is).</summary>
    public Color Get(int x, int y) => Inside(x, y, out x, out y) ? Base.GetPixel(x, y) : Colors.Transparent;

    /// <summary>Whether something solid is drawn at local (x, y).</summary>
    public bool IsSolid(int x, int y) => Inside(x, y, out x, out y) && _occupied[y * Width + x];

    /// <summary>Clears local (x, y) back to nothing.</summary>
    public void Erase(int x, int y)
    {
        if (!Inside(x, y, out x, out y))
            return;
        Base.SetPixel(x, y, Colors.Transparent);
        Mask.SetPixel(x, y, Colors.Transparent);
        _occupied[y * Width + x] = false;
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
        if (!Inside(x, y, out x, out y))
            return;
        Base.SetPixel(x, y, Colors.Transparent);
        Mask.SetPixel(x, y, new Color(shade, shade, shade));
        _occupied[y * Width + x] = true;
    }

    /// <summary>A faction-coloured pixel lit to <paramref name="light"/> (-2..2), dithered between the mask's tones.</summary>
    public void TeamLit(int x, int y, float light) => Team(x, y, TeamTones[Tone(x, y, light) + 2]);

    /// <summary>Whether local (x, y) holds faction colour.</summary>
    public bool IsTeam(int x, int y) => Inside(x, y, out x, out y) && Mask.GetPixel(x, y).A > 0f;

    /// <summary>Marks a rectangle as shining at night in <paramref name="color"/> (the day picture is not changed).</summary>
    public void Lit(int x, int y, int w, int h, Color color)
    {
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
            {
                if (!Inside(x + i, y + j, out int px, out int py))
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
            for (int i = 0; i < w; i++)
            {
                float light = j == 0 ? 1.4f : i == 0 ? 0.8f : j == h - 1 ? -1.4f : i == w - 1 ? -0.8f : 0f;
                TeamLit(x + i, y + j, light);
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

    // ---- Lit shapes ----------------------------------------------------------------------------------------------

    /// <summary>The ramp tone (-2..2) for a light level at a pixel, dithered between the two nearest tones.</summary>
    public int Tone(int x, int y, float light)
    {
        light = Mathf.Clamp(light, -2f, 2f);
        int low = (int)Mathf.Floor(light);
        float frac = light - low;
        int ax = x + Origin.X, ay = y + Origin.Y;
        float threshold = Bayer[(ay & 3) * 4 + (ax & 3)];
        // Dither only in the middle of the step: wide flat bands with a thin stippled seam read cleaner.
        float t = Mathf.Clamp((frac - 0.5f) * 2.2f + 0.5f, 0f, 1f);
        return Math.Clamp(t > threshold ? low + 1 : low, -2, 2);
    }

    /// <summary>A pixel of <paramref name="ramp"/> lit to <paramref name="light"/> (-2..2).</summary>
    public void Shade(int x, int y, Ramp ramp, float light, bool solid = true) => Px(x, y, ramp[Tone(x, y, light)], solid);

    /// <summary>
    /// A filled ellipse lit like a ball (or, with <paramref name="flat"/> 1, like a disc facing up): bright to the
    /// top-left, deep in the bottom-right; <paramref name="bias"/> lifts or lowers the whole.
    /// </summary>
    public void Ball(float cx, float cy, float rx, float ry, Ramp ramp, float bias = 0f, float flat = 0f)
    {
        for (int y = (int)Mathf.Floor(cy - ry); y <= (int)Mathf.Ceil(cy + ry); y++)
            for (int x = (int)Mathf.Floor(cx - rx); x <= (int)Mathf.Ceil(cx + rx); x++)
            {
                float nx = (x + 0.5f - cx) / rx, ny = (y + 0.5f - cy) / ry;
                float d = nx * nx + ny * ny;
                if (d > 1f)
                    continue;
                Shade(x, y, ramp, BallLight(nx, ny, d, flat) + bias);
            }
    }

    /// <summary>The same as <see cref="Ball"/> in faction colour.</summary>
    public void TeamBall(float cx, float cy, float rx, float ry, float bias = 0f, float flat = 0f)
    {
        for (int y = (int)Mathf.Floor(cy - ry); y <= (int)Mathf.Ceil(cy + ry); y++)
            for (int x = (int)Mathf.Floor(cx - rx); x <= (int)Mathf.Ceil(cx + rx); x++)
            {
                float nx = (x + 0.5f - cx) / rx, ny = (y + 0.5f - cy) / ry;
                float d = nx * nx + ny * ny;
                if (d <= 1f)
                    TeamLit(x, y, BallLight(nx, ny, d, flat) + bias);
            }
    }

    /// <summary>Light on a ball at normalised (nx, ny): the sun up-left, a rim of reflected light at the lower right.</summary>
    private static float BallLight(float nx, float ny, float d, float flat)
    {
        float nz = Mathf.Sqrt(Mathf.Max(0f, 1f - d));
        float lambert = -0.55f * nx - 0.62f * ny + 0.56f * nz; // the sun's direction (-0.55, -0.62, 0.56)
        lambert = Mathf.Lerp(lambert, 0.35f - 0.4f * ny, flat);
        float light = lambert * 3.2f - 0.9f;
        if (d > 0.72f && nx + ny > 0.9f)
            light += 0.6f; // bounce light on the shadowed rim
        return light;
    }

    /// <summary>
    /// A box lit as a cylinder standing up (or lying, <paramref name="horizontal"/>): bright band left of the middle,
    /// falling off to the right; top rows a touch lighter.
    /// </summary>
    public void Column(int x, int y, int w, int h, Ramp ramp, float bias = 0f, bool horizontal = false)
    {
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
                Shade(x + i, y + j, ramp, CylinderLight(i, j, w, h, horizontal) + bias);
    }

    /// <summary>The same as <see cref="Column"/> in faction colour.</summary>
    public void TeamColumn(int x, int y, int w, int h, float bias = 0f, bool horizontal = false)
    {
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
                TeamLit(x + i, y + j, CylinderLight(i, j, w, h, horizontal) + bias);
    }

    private static float CylinderLight(int i, int j, int w, int h, bool horizontal)
    {
        float across = horizontal ? (j + 0.5f) / h : (i + 0.5f) / w; // 0 at the lit side
        float light = 1.3f - 3.2f * Mathf.Abs(across - 0.3f) - (across > 0.3f ? 0.6f * (across - 0.3f) : 0f);
        float along = horizontal ? (i + 0.5f) / w : (j + 0.5f) / h;
        return light + (horizontal ? 0f : 0.35f - 0.5f * along);
    }

    /// <summary>A flat-faced block: <paramref name="top"/> rows of lit top face, the front lit evenly, the right edge shaded.</summary>
    public void Block(int x, int y, int w, int h, Ramp ramp, int top = 0, float bias = 0f)
    {
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
            {
                float light = j < top ? 1.5f : i == 0 ? 0.6f : i >= w - 2 ? -0.9f : j >= h - 2 ? -0.6f : 0f;
                Shade(x + i, y + j, ramp, light + bias);
            }
    }

    /// <summary>
    /// A stroke <paramref name="width"/> pixels thick from (x0, y0) to (x1, y1), shaded across like a rod (spear shafts,
    /// limbs, timbers), with round ends.
    /// </summary>
    public void Stroke(float x0, float y0, float x1, float y1, float width, Ramp ramp, float bias = 0f)
    {
        var a = new Vector2(x0, y0);
        var b = new Vector2(x1, y1);
        var ab = b - a;
        float len2 = Mathf.Max(ab.LengthSquared(), 1e-4f);
        var normal = new Vector2(-ab.Y, ab.X).Normalized();
        // Turn the normal to face the sun (up-left), so the lit side of the rod is that one.
        if (normal.X + normal.Y > 0f)
            normal = -normal;
        float r = width / 2f;
        int minX = (int)Mathf.Floor(Mathf.Min(x0, x1) - r - 1), maxX = (int)Mathf.Ceil(Mathf.Max(x0, x1) + r + 1);
        int minY = (int)Mathf.Floor(Mathf.Min(y0, y1) - r - 1), maxY = (int)Mathf.Ceil(Mathf.Max(y0, y1) + r + 1);
        for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                float t = Mathf.Clamp((p - a).Dot(ab) / len2, 0f, 1f);
                var offset = p - (a + ab * t);
                if (offset.Length() > r + 0.15f)
                    continue;
                float side = r > 0.6f ? offset.Dot(normal) / r : 0f; // 1 on the lit side
                Shade(x, y, ramp, side * 1.3f - 0.1f + bias);
            }
    }

    /// <summary>The same as <see cref="Stroke"/> in faction colour.</summary>
    public void TeamStroke(float x0, float y0, float x1, float y1, float width, float bias = 0f)
    {
        var a = new Vector2(x0, y0);
        var ab = new Vector2(x1, y1) - a;
        float len2 = Mathf.Max(ab.LengthSquared(), 1e-4f);
        var normal = new Vector2(-ab.Y, ab.X).Normalized();
        if (normal.X + normal.Y > 0f)
            normal = -normal;
        float r = width / 2f;
        for (int y = (int)Mathf.Floor(Mathf.Min(y0, y1) - r - 1); y <= (int)Mathf.Ceil(Mathf.Max(y0, y1) + r + 1); y++)
            for (int x = (int)Mathf.Floor(Mathf.Min(x0, x1) - r - 1); x <= (int)Mathf.Ceil(Mathf.Max(x0, x1) + r + 1); x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                float t = Mathf.Clamp((p - a).Dot(ab) / len2, 0f, 1f);
                var offset = p - (a + ab * t);
                if (offset.Length() > r + 0.15f)
                    continue;
                float side = r > 0.6f ? offset.Dot(normal) / r : 0f;
                TeamLit(x, y, side * 1.3f - 0.1f + bias);
            }
    }

    /// <summary>
    /// Fills a polygon (points in local pixels), each pixel lit by <paramref name="light"/> (given the pixel), or in
    /// faction colour when <paramref name="ramp"/> is null.
    /// </summary>
    public void Polygon(Vector2[] points, Ramp? ramp, Func<int, int, float> light)
    {
        float minY = float.MaxValue, maxY = float.MinValue, minX = float.MaxValue, maxX = float.MinValue;
        foreach (var p in points)
        {
            minY = Mathf.Min(minY, p.Y); maxY = Mathf.Max(maxY, p.Y);
            minX = Mathf.Min(minX, p.X); maxX = Mathf.Max(maxX, p.X);
        }
        for (int y = (int)Mathf.Floor(minY); y <= (int)Mathf.Ceil(maxY); y++)
            for (int x = (int)Mathf.Floor(minX); x <= (int)Mathf.Ceil(maxX); x++)
            {
                if (!Contains(points, new Vector2(x + 0.5f, y + 0.5f)))
                    continue;
                if (ramp is { } r)
                    Shade(x, y, r, light(x, y));
                else
                    TeamLit(x, y, light(x, y));
            }
    }

    private static bool Contains(Vector2[] poly, Vector2 p)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            if ((poly[i].Y > p.Y) != (poly[j].Y > p.Y)
                && p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X)
                inside = !inside;
        }
        return inside;
    }

    /// <summary>A soft oval of shade on the ground (not outlined): darker in the middle, dithered out at the rim.</summary>
    public void GroundShadow(float cx, float cy, float rx, float ry, float strength = 0.3f)
    {
        for (int y = (int)Mathf.Floor(cy - ry); y <= (int)Mathf.Ceil(cy + ry); y++)
            for (int x = (int)Mathf.Floor(cx - rx); x <= (int)Mathf.Ceil(cx + rx); x++)
            {
                float nx = (x + 0.5f - cx) / rx, ny = (y + 0.5f - cy) / ry;
                float d = nx * nx + ny * ny;
                if (d > 1f || d > 0.6f && Bayer[((y + Origin.Y) & 3) * 4 + ((x + Origin.X) & 3)] < (d - 0.6f) * 2.5f)
                    continue;
                if (IsSolid(x, y))
                    continue;
                Px(x, y, new Color(0.08f, 0.06f, 0.12f, strength * (d < 0.35f ? 1.25f : 1f)), solid: false);
            }
    }

    // ---- Outlines -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Rings every drawn shape inside the rectangle with <paramref name="color"/> (4-neighbourhood),
    /// the classic pixel-art outline. Works on the whole canvas when no rectangle is given.
    /// </summary>
    public void Outline(Color color, Rect2I? area = null) => Ring(area, _ => color, bottomColor: color);

    /// <summary>
    /// Selective outline: each new rim pixel takes a deep shade of the solid pixel it borders (faction colour borders get
    /// a deep faction-colour rim), except where it lies under the shape — there <paramref name="dark"/>, to set it on
    /// the ground. Softer than a black line all round, and the silhouette still reads.
    /// </summary>
    public void SelOut(Color dark, Rect2I? area = null) => Ring(area, null, bottomColor: dark);

    private void Ring(Rect2I? area, Func<Color, Color>? flat, Color bottomColor)
    {
        var r = area ?? new Rect2I(0, 0, Width, Height);
        var add = new System.Collections.Generic.List<(int Index, Color Color, bool Team)>();
        for (int y = r.Position.Y; y < r.End.Y; y++)
        {
            for (int x = r.Position.X; x < r.End.X; x++)
            {
                if (_occupied[y * Width + x])
                    continue;
                int source = -1;
                bool below = false;
                if (y > r.Position.Y && _occupied[(y - 1) * Width + x]) { source = (y - 1) * Width + x; below = true; }
                else if (x > r.Position.X && _occupied[y * Width + x - 1]) source = y * Width + x - 1;
                else if (x < r.End.X - 1 && _occupied[y * Width + x + 1]) source = y * Width + x + 1;
                else if (y < r.End.Y - 1 && _occupied[(y + 1) * Width + x]) source = (y + 1) * Width + x;
                if (source < 0)
                    continue;
                if (flat != null || below)
                {
                    add.Add((y * Width + x, flat?.Invoke(bottomColor) ?? bottomColor, false));
                    continue;
                }
                var mask = Mask.GetPixel(source % Width, source / Width);
                if (mask.A > 0f)
                {
                    add.Add((y * Width + x, new Color(0.3f, 0.3f, 0.3f), true));
                    continue;
                }
                var c = Base.GetPixel(source % Width, source / Width);
                add.Add((y * Width + x, Darken(c), false));
            }
        }
        foreach (var (i, color, team) in add)
        {
            if (team)
            {
                Base.SetPixel(i % Width, i / Width, Colors.Transparent);
                Mask.SetPixel(i % Width, i / Width, color);
            }
            else
            {
                Base.SetPixel(i % Width, i / Width, color);
            }
            _occupied[i] = true;
        }
    }

    /// <summary>The rim colour next to <paramref name="c"/>: nearly black, keeping a hint of its hue.</summary>
    private static Color Darken(Color c)
    {
        var deep = c.Lerp(new Color("0b0818"), 0.8f);
        return new Color(deep.R, deep.G, deep.B, 1f);
    }

    // ---- Lighting passes and effects --------------------------------------------------------------------------------

    /// <summary>
    /// Light on the silhouette's edges, before the outline: a cold rim light along the edges facing the right (the far
    /// side from the sun, as if lit from behind by the sky), and a thin warm catch-light along the top-left edges. Faction
    /// colour on the rim goes to its brightest tone.
    /// </summary>
    public void RimLight(Rect2I area, Color rim, float strength = 0.55f, Color? catchLight = null)
    {
        var warm = catchLight ?? new Color("ffe4a0");
        var changes = new System.Collections.Generic.List<(int X, int Y, Color? Base, float Mask)>();
        bool Empty(int x, int y) => x < area.Position.X || y < area.Position.Y || x >= area.End.X || y >= area.End.Y
                                    || !_occupied[y * Width + x];
        for (int y = area.Position.Y; y < area.End.Y; y++)
            for (int x = area.Position.X; x < area.End.X; x++)
            {
                if (!_occupied[y * Width + x])
                    continue;
                bool back = Empty(x + 1, y);
                bool front = Empty(x, y - 1) || Empty(x - 1, y);
                if (!back && !front)
                    continue;
                var mask = Mask.GetPixel(x, y);
                if (mask.A > 0f)
                {
                    changes.Add((x, y, null, back ? 1f : Mathf.Min(1f, mask.R + 0.15f)));
                    continue;
                }
                var c = Base.GetPixel(x, y);
                var lit = back ? c.Lerp(rim, strength) : c.Lerp(warm, strength * 0.4f);
                changes.Add((x, y, new Color(lit.R, lit.G, lit.B, c.A), 0f));
            }
        foreach (var (x, y, color, shade) in changes)
        {
            if (color is { } b)
                Base.SetPixel(x, y, b);
            else
                Mask.SetPixel(x, y, new Color(shade, shade, shade));
        }
    }

    /// <summary>
    /// A weapon's smear: a crescent of light along the arc a blow sweeps, from <paramref name="from"/> to
    /// <paramref name="to"/> radians around (cx, cy) at <paramref name="radius"/>, thick in the middle and tapering at both
    /// ends, white at its leading edge fading to <paramref name="tint"/> and transparency behind.
    /// </summary>
    public void Smear(float cx, float cy, float radius, float from, float to, float thickness, Color tint, float alpha = 0.85f)
    {
        float span = to - from;
        int steps = (int)Mathf.Ceil(Mathf.Abs(span) * radius * 1.5f) + 2;
        for (int s = 0; s <= steps; s++)
        {
            float t = s / (float)steps;
            float a = from + span * t;
            float w = thickness * Mathf.Sin(t * Mathf.Pi);
            for (float r = radius - w; r <= radius + 0.01f; r += 0.5f)
            {
                int x = (int)Mathf.Round(cx + Mathf.Cos(a) * r), y = (int)Mathf.Round(cy + Mathf.Sin(a) * r);
                if (!Inside(x, y, out int ax, out int ay) || _occupied[ay * Width + ax])
                    continue;
                float lead = (radius - r) / Mathf.Max(0.5f, w); // 0 at the outer, leading edge
                var color = Colors.White.Lerp(tint, Mathf.Clamp(lead * 1.4f, 0f, 1f));
                float fade = alpha * (0.35f + 0.65f * t) * (1f - lead * 0.5f);
                var under = Base.GetPixel(ax, ay);
                if (under.A > 0f)
                    continue;
                Base.SetPixel(ax, ay, color with { A = fade });
            }
        }
    }

    /// <summary>
    /// Copies the drawing in <paramref name="source"/> of <paramref name="from"/> (its pixels, colour and faction mask) turned a quarter left
    /// about its bottom middle, so a figure standing in it lies on its back: its head where the left side was, its feet
    /// on the right. The result is placed with its bottom middle at <paramref name="feet"/> (local pixels).
    /// </summary>
    public void CopyLying(PixelCanvas from, Rect2I source, Vector2I feet, bool solid = true)
    {
        int px = source.Position.X + source.Size.X / 2, py = source.End.Y - 1;
        var drawn = new System.Collections.Generic.List<(int Dx, int Dy, Color Base, Color Mask)>();
        int minDx = int.MaxValue, minDy = int.MaxValue, maxDy = int.MinValue;
        for (int y = source.Position.Y; y < source.End.Y; y++)
            for (int x = source.Position.X; x < source.End.X; x++)
            {
                var b = from.Base.GetPixel(x, y);
                var m = from.Mask.GetPixel(x, y);
                if (b.A <= 0.5f && m.A <= 0f)
                    continue; // translucent smears and shadows stay behind
                int dx = x - px, dy = y - py;
                drawn.Add((dx, dy, b, m));
                minDx = Math.Min(minDx, dx);
                minDy = Math.Min(minDy, dy);
                maxDy = Math.Max(maxDy, dy);
            }
        // A quarter turn: what stood up now lies to the left, what was in front now points up; the body is centred on
        // the feet's spot and its back rests on the ground.
        var pixels = new System.Collections.Generic.List<(int X, int Y, Color Base, Color Mask)>();
        foreach (var (dx, dy, b, m) in drawn)
            pixels.Add((feet.X + dy - (minDy + maxDy) / 2, feet.Y - dx + minDx, b, m));
        foreach (var (x, y, b, m) in pixels)
        {
            if (!Inside(x, y, out int ax, out int ay))
                continue;
            Base.SetPixel(ax, ay, b);
            Mask.SetPixel(ax, ay, m);
            _occupied[ay * Width + ax] = solid;
        }
    }

    /// <summary>Erases everything in a rectangle (canvas pixels).</summary>
    public void ClearRect(Rect2I area)
    {
        for (int y = area.Position.Y; y < area.End.Y; y++)
            for (int x = area.Position.X; x < area.End.X; x++)
            {
                Base.SetPixel(x, y, Colors.Transparent);
                Mask.SetPixel(x, y, Colors.Transparent);
                Glow.SetPixel(x, y, Colors.Transparent);
                _occupied[y * Width + x] = false;
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

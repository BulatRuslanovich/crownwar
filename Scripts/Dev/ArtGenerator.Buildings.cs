using System;
using Godot;
using Warcrown.World;

namespace Warcrown.Dev;

/// <summary>
/// The buildings, every level, with faction-colour masks and night lights; and the gold ore. They are drawn in the
/// soldiers' three-quarter view: the fronts of walls and towers face the viewer and the tops (wall-walks, platforms,
/// roofs) are seen from above, foreshortened by half, so a building stands on the front part of its footprint and rises
/// over the rest. The look is grim and weathered: dark, cool stone in uneven courses, grimy and mossy at the foot, dark
/// timber and iron, roofs of slate stained in the owner's colours, and the full
/// faction colour kept for banners and flags.
/// </summary>
public static partial class ArtGenerator
{
    /// <summary>Warm light of a lit window at night.</summary>
    private static readonly Color WindowGlow = new("ffc24a");
    /// <summary>Light of an open fire (beacon, torch) at night.</summary>
    private static readonly Color FireGlow = new("ff8a2a");
    private static readonly Color Fire = new("ff7a1a");
    private static readonly Color FireLight = new("ffd25a");

    /// <summary>How far the buildings' mid tones and shadows sink towards the dusk (see <see cref="Ramp.Of"/>).</summary>
    private const float BuildingMood = 0.3f;
    /// <summary>How much darker than the banners the slates stained in the owner's colours are (light levels).</summary>
    private const float RoofStain = 1.05f;

    private static readonly Ramp WallR = Ramp.Of(new Color("6d6a67"), mood: BuildingMood);
    private static readonly Ramp WallLightR = Ramp.Of(new Color("807b75"), mood: BuildingMood);
    private static readonly Ramp FlagstoneR = Ramp.Of(new Color("8a847b"), 0.8f, mood: BuildingMood);
    private static readonly Ramp MossR = Ramp.Of(new Color("4f5b35"), 0.8f, mood: BuildingMood);
    private static readonly Ramp PlasterR = Ramp.Of(new Color("a39780"), 0.8f, mood: BuildingMood);
    private static readonly Ramp SlateR = Ramp.Of(new Color("454a57"), mood: BuildingMood);
    private static readonly Ramp CourtR = Ramp.Of(new Color("5c5853"), 0.8f, mood: BuildingMood);
    private static readonly Ramp GlassR = Ramp.Of(new Color("2c2630"), mood: BuildingMood);
    private static readonly Ramp DoorR = Ramp.Of(new Color("4e3320"), mood: BuildingMood);
    private static readonly Ramp RockR = Ramp.Of(new Color("8a7f72"), mood: BuildingMood * 0.5f);
    private static readonly Ramp OreR = Ramp.Of(new Color("e9b93c"), mood: BuildingMood);

    /// <summary>
    /// A building's sheet: <see cref="ArtLayout.BuildingFrames"/> frames of <paramref name="w"/>×<paramref name="h"/>
    /// side by side (flags waving, wheels turning, fires flickering), each drawn with local coordinates, rim-lit and
    /// outlined.
    /// </summary>
    private static PixelCanvas Sheet(int w, int h, Action<PixelCanvas, int> draw)
    {
        var c = new PixelCanvas(w * ArtLayout.BuildingFrames, h);
        for (int frame = 0; frame < ArtLayout.BuildingFrames; frame++)
        {
            var cell = new Vector2I(frame * w, 0);
            c.Origin = cell;
            c.Clip = new Rect2I(cell, new Vector2I(w, h));
            draw(c, frame);
            c.Origin = Vector2I.Zero;
            c.RimLight(new Rect2I(cell, new Vector2I(w, h)), SkyRim, 0.35f);
            c.SelOut(SilhouetteRim, new Rect2I(cell, new Vector2I(w, h)));
        }
        return c;
    }

    // ---- Stone ------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The light of masonry <paramref name="along"/> px along a wall and <paramref name="up"/> px above its foot (both
    /// ≥ 0): courses 4 px high of blocks 5–9 long, each block its own shade (now and then a weathered dark one), lit
    /// along its top and shaded above the joint under it; the joints dark.
    /// </summary>
    private static float MasonryLight(int along, int up, int salt, out bool joint)
    {
        int course = up >> 2, row = up & 3;
        int start = -(Hash(course, salt, 51) % 6), block = 0;
        int length = 5 + Hash(course, 0, salt) % 5;
        while (start + length <= along)
        {
            start += length;
            block++;
            length = 5 + Hash(course, block, salt) % 5;
        }
        joint = row == 0 || along == start;
        if (joint)
            return -1.25f;
        int h = Hash(block, course, salt + 2);
        return (h % 5 - 2) * 0.07f + (h % 17 == 0 ? -0.3f : 0f) + (row == 3 ? 0.4f : row == 1 ? -0.15f : 0f)
               + (along == start + 1 ? 0.2f : 0f);
    }

    /// <summary>
    /// Weathering of a wall face at (x, y), <paramref name="up"/> px above the ground: grime darkening the last courses.
    /// Returns the light to add; <paramref name="moss"/> says whether moss grows there.
    /// </summary>
    private static float Weathering(int x, int y, int up, int salt, out bool moss)
    {
        moss = up < 1 + Hash(x / 4, salt, 7) % 3 && Hash(x / 2, y, salt + 5) % 3 != 0;
        return -Mathf.Max(0f, 6 - up) * 0.1f;
    }

    /// <summary>A flat face of masonry from (x, y), <paramref name="w"/>×<paramref name="h"/>, its foot on the ground (grimy, mossy) unless <paramref name="grime"/> is false.</summary>
    private static void Ashlar(PixelCanvas c, int x, int y, int w, int h, Ramp r, int salt, float bias = 0f, bool grime = true)
    {
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
            {
                int up = h - 1 - j;
                float light = MasonryLight(i + 3, up, salt, out bool joint) + bias + (i == 0 ? 0.35f : i >= w - 2 ? -0.5f : 0f);
                bool moss = false;
                if (grime)
                    light += Weathering(x + i, y + j, up, salt, out moss);
                if (moss && !joint)
                    c.Shade(x + i, y + j, MossR, light * 0.5f + 0.1f);
                else
                    c.Shade(x + i, y + j, r, light);
            }
    }

    /// <summary>
    /// A round tower of masonry, radius <paramref name="r"/>, its foot's front at (cx, foot), rising
    /// <paramref name="height"/>: the courses curve round it and the blocks narrow towards its sides; lit like a
    /// cylinder, grimy and mossy at the foot. Returns the centre of the ring at its top.
    /// </summary>
    private static Vector2 RoundTower(PixelCanvas c, float cx, float foot, float r, int height, Ramp ramp, int salt, bool grime = true, float bias = 0f)
    {
        float ry = r * 0.5f;
        for (int x = (int)Mathf.Floor(cx - r); x <= (int)Mathf.Ceil(cx + r); x++)
        {
            float u = (x + 0.5f - cx) / r;
            if (Mathf.Abs(u) > 1f)
                continue;
            int bottom = (int)Mathf.Round(foot - ry + ry * Mathf.Sqrt(1f - u * u));
            int along = (int)Mathf.Floor((Mathf.Asin(u) + 1.6f) * r);
            float shape = CylinderShade(u) + bias;
            for (int y = bottom - height; y < bottom; y++)
            {
                int up = bottom - 1 - y;
                float light = MasonryLight(along, up, salt, out bool joint) + shape;
                bool moss = false;
                if (grime)
                    light += Weathering(x, y, up, salt, out moss);
                if (moss && !joint)
                    c.Shade(x, y, MossR, light * 0.5f + 0.1f);
                else
                    c.Shade(x, y, ramp, light);
            }
        }
        return new Vector2(cx, foot - ry - height);
    }

    /// <summary>Light across a round body at <paramref name="u"/> (-1 left edge … 1 right edge): brightest left of the middle, falling into shade on the right.</summary>
    private static float CylinderShade(float u)
    {
        float across = (u + 1f) / 2f;
        return 1.05f - 2.6f * Mathf.Abs(across - 0.3f) - (across > 0.3f ? 0.5f * (across - 0.3f) : 0f);
    }

    /// <summary>A band of iron round a tower (radius r, centre x cx) at the height of the tower's front edge y = <paramref name="y"/> at its middle.</summary>
    private static void IronBand(PixelCanvas c, float cx, float y, float r)
    {
        float ry = r * 0.5f;
        for (int x = (int)Mathf.Floor(cx - r); x <= (int)Mathf.Ceil(cx + r); x++)
        {
            float u = (x + 0.5f - cx) / r;
            if (Mathf.Abs(u) > 1f)
                continue;
            int py = (int)Mathf.Round(y - ry + ry * Mathf.Sqrt(1f - u * u));
            c.Shade(x, py, IronR, CylinderShade(u) + 0.4f);
            c.Shade(x, py + 1, IronR, CylinderShade(u) - 0.6f);
            if ((int)((Mathf.Asin(u) + 1.6f) * r) % 6 == 0)
                c.Px(x, py, SteelR.Light); // a rivet
        }
    }

    /// <summary>Flagstones on a wall-walk or platform seen from above (x, y), w × d: lit by the sun, in rows foreshortened to 3 px.</summary>
    private static void Flagstones(PixelCanvas c, int x, int y, int w, int d, Ramp r, int salt)
    {
        for (int j = 0; j < d; j++)
            for (int i = 0; i < w; i++)
            {
                int row = j / 3;
                int stone = (i + row % 2 * 4) / 8;
                bool joint = j % 3 == 2 || (i + row % 2 * 4) % 8 == 7;
                float light = joint ? -0.8f : 0.55f + (Hash(stone, row, salt + 60) % 5 - 2) * 0.15f + (j % 3 == 0 ? 0.25f : 0f);
                c.Shade(x + i, y + j, r, light - (i >= w - 2 ? 0.5f : 0f));
            }
    }

    /// <summary>
    /// A parapet of merlons standing on the line y from x to x + w: each 4 wide and 5 high with gaps of 3, its front face
    /// lit evenly, its right side turned away, its top catching the sun.
    /// </summary>
    private static void Battlement(PixelCanvas c, int x, int y, int w, Ramp r, Ramp? caps = null, int height = 5)
    {
        for (int i = 0; i < w; i += 7)
        {
            int mw = Math.Min(4, w - i);
            for (int j = 0; j < height; j++)
                for (int k = 0; k < mw; k++)
                {
                    int px = x + i + k, py = y - height + j;
                    if (j == 0)
                        c.Shade(px, py, caps ?? r, 1.3f);
                    else
                        c.Shade(px, py, r, (k == mw - 1 ? -0.7f : k == 0 ? 0.4f : 0.05f) - j * 0.06f + (Hash(px, py, 3) % 7 == 0 ? -0.5f : 0f));
                }
        }
    }

    /// <summary>
    /// A parapet round the top of a round tower: a lit rim, the floor inside in shade, merlons round the ring; what
    /// rises from within (a cone roof) is drawn by <paramref name="inside"/> after the far merlons and before the near.
    /// </summary>
    private static void RingBattlement(PixelCanvas c, float cx, float cy, float r, Ramp ramp, Ramp? caps = null, Action? inside = null)
    {
        float ry = r * 0.5f;
        for (int y = (int)Mathf.Floor(cy - ry); y <= (int)Mathf.Ceil(cy + ry); y++)
            for (int x = (int)Mathf.Floor(cx - r); x <= (int)Mathf.Ceil(cx + r); x++)
            {
                float nx = (x + 0.5f - cx) / r, ny = (y + 0.5f - cy) / ry;
                float d = nx * nx + ny * ny;
                if (d > 1f)
                    continue;
                c.Shade(x, y, d > 0.55f ? ramp : FlagstoneR, d > 0.55f ? 0.9f - nx * 0.6f : -0.9f + (Hash(x, y, 4) % 3 - 1) * 0.2f);
            }
        int n = Math.Max(6, (int)(r * 0.8f));
        foreach (bool front in new[] { false, true })
        {
            if (front)
                inside?.Invoke();
            for (int k = 0; k < n; k++)
            {
                float a = (k + 0.5f) * Mathf.Tau / n;
                if (Mathf.Sin(a) > 0f != front)
                    continue;
                int mx = (int)Mathf.Round(cx + Mathf.Cos(a) * (r - 1.5f)), my = (int)Mathf.Round(cy + Mathf.Sin(a) * (ry - 0.8f));
                float side = -Mathf.Cos(a) * 0.7f;
                for (int j = 0; j < 5; j++)
                    for (int i = -1; i <= 1; i++)
                        c.Shade(mx + i, my - j, j == 4 ? caps ?? ramp : ramp, j == 4 ? 1.3f : side + (i == 1 ? -0.5f : 0f) + (front ? 0f : -0.3f));
            }
        }
    }

    // ---- Roofs ------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A cone roof seen from the front and above: the eaves an ellipse of radius <paramref name="r"/> centred at
    /// (cx, eaves), the apex <paramref name="tall"/> above. Rows of slates follow the eaves round, lit like a cylinder
    /// from the left; in the owner's colours stained dark, or in
    /// <paramref name="material"/>; the eaves' edge dark, or gilded.
    /// </summary>
    private static void ConeRoof(PixelCanvas c, float cx, float eaves, float r, float tall, Ramp? material = null, bool gilded = false, int salt = 0)
    {
        float apex = eaves - tall;
        float Front(float t, float dx) => apex + t * tall + 0.5f * Mathf.Sqrt(Mathf.Max(0f, r * r * t * t - dx * dx));
        for (int x = (int)Mathf.Floor(cx - r); x <= (int)Mathf.Ceil(cx + r); x++)
        {
            float dx = x + 0.5f - cx;
            float t0 = Mathf.Abs(dx) / r;
            if (t0 > 1f)
                continue;
            int yTop = (int)Mathf.Round(Front(t0, dx)), yBottom = (int)Mathf.Round(Front(1f, dx));
            for (int y = yTop; y <= yBottom; y++)
            {
                float py = y + 0.5f, lo = t0, hi = 1f;
                for (int k = 0; k < 16; k++)
                {
                    float mid = (lo + hi) / 2f;
                    if (Front(mid, dx) < py)
                        lo = mid;
                    else
                        hi = mid;
                }
                float t = (lo + hi) / 2f;
                float sin = Mathf.Clamp(dx / Mathf.Max(0.01f, r * t), -1f, 1f);
                float rows = t * tall / 3.2f;
                int row = (int)rows;
                float inRow = rows - row;
                int tab = (int)Mathf.Floor((Mathf.Asin(sin) * r * t + row % 2 * 1.5f) / 3f);
                int h = Hash(tab, row, salt + 31);
                float light = CylinderShade(sin) + 0.3f + (inRow > 0.72f ? -0.8f : inRow < 0.3f ? 0.25f : 0f) + (h % 3 - 1) * 0.07f;
                bool eave = y >= yBottom - 1;
                if (eave && gilded)
                    c.Shade(x, y, GoldR, light);
                else if (material is { } m)
                    c.Shade(x, y, m, eave ? -1.4f : light);
                else if (eave)
                    c.Shade(x, y, SlateR, -1.2f);
                else
                    c.TeamLit(x, y, light - RoofStain);
            }
        }
        if (gilded)
            c.Ball(cx, apex, 2.2f, 2.2f, GoldR, 0.6f); // a gilded finial
    }

    /// <summary>
    /// One plane of a pitched roof, its eaves and ridge running across: from x0 to x1, between <paramref name="top"/>
    /// and <paramref name="bottom"/>. Rows of slates 3 px deep, staggered, lit by <paramref name="light"/>; stained in
    /// the owner's colours, the lower edge dark.
    /// </summary>
    private static void SlateRoof(PixelCanvas c, int x0, int x1, int top, int bottom, float light, int salt)
    {
        for (int y = top; y < bottom; y++)
        {
            int row = (y - top) / 3, inRow = (y - top) % 3;
            for (int x = x0; x < x1; x++)
            {
                int tab = (x + row % 2 * 2) / 4;
                int h = Hash(tab, row, salt + 70);
                float l = light + (inRow == 0 ? 0.35f : inRow == 2 ? -0.7f : 0f) + ((x + row % 2 * 2) % 4 == 3 ? -0.4f : 0f)
                          + (h % 3 - 1) * 0.07f - (x >= x1 - 2 ? 0.4f : 0f) + (x <= x0 + 1 ? 0.3f : 0f);
                if (y >= bottom - 1)
                    c.Shade(x, y, SlateR, -1.3f);
                else
                    c.TeamLit(x, y, l - RoofStain);
            }
        }
    }

    // ---- Fittings ---------------------------------------------------------------------------------------------------

    /// <summary>An arched window in a stone frame, dark glass with a mullion; it shines at night.</summary>
    private static void ArchWindow(PixelCanvas c, int x, int y, int w, int h)
    {
        for (int j = -1; j <= h; j++)
            for (int i = -1; i <= w; i++)
            {
                bool arch = j < w / 2 && (i - (w - 1) / 2f) * (i - (w - 1) / 2f) + (j - w / 2f) * (j - w / 2f) * 1.2f > (w / 2f + 0.6f) * (w / 2f + 0.6f);
                if (arch)
                    continue;
                bool frame = i < 0 || i >= w || j < 0 || j >= h;
                if (frame)
                    c.Shade(x + i, y + j, WallLightR, j == h ? 1.2f : i < 0 ? 0.6f : -0.5f);
                else
                    c.Shade(x + i, y + j, GlassR, i == 0 || j == 0 ? -1.2f : (i + j) % 5 == 0 ? 0.4f : -0.3f);
            }
        if (w >= 4)
            c.Rect(x + w / 2, y + 1, 1, h - 1, WallR.Shade);
        c.Lit(x, y, w, h, WindowGlow);
    }

    /// <summary>An arrow slit: a narrow dark cut with a lit sill.</summary>
    private static void Slit(PixelCanvas c, int x, int y, int h)
    {
        c.Rect(x, y, 1, h, GlassR.Deep);
        c.Px(x, y + h, WallLightR.Light);
        c.Px(x + 1, y + 1, WallR.Shade);
    }

    /// <summary>A small shuttered window in a timber frame; it shines at night.</summary>
    private static void ShutteredWindow(PixelCanvas c, int x, int y, int w, int h)
    {
        c.Block(x - 1, y - 1, w + 2, h + 2, DarkWoodR, bias: -0.3f);
        c.Rect(x, y, w, h, GlassR.Shade);
        c.Rect(x + w / 2, y, 1, h, DarkWoodR.Deep);
        c.Rect(x, y + h / 2, w, 1, DarkWoodR.Deep);
        c.Lit(x, y, w, h, WindowGlow);
        // A shutter hanging open on either side.
        c.Block(x - 3, y, 2, h, WoodR, bias: 0.2f);
        c.Block(x + w + 1, y, 2, h, WoodR, bias: -0.4f);
    }

    /// <summary>An arched door of planks with iron bands and studs under an arch of stone (or gilded).</summary>
    private static void ArchDoor(PixelCanvas c, int x, int y, int w, int h, bool gilded = false)
    {
        float cx = x + w / 2f;
        for (int j = -2; j < h; j++)
            for (int i = -2; i < w + 2; i++)
            {
                float dx = i - (w - 1) / 2f, dy = j - w / 2f;
                bool inArch = j >= w / 2 || dx * dx + dy * dy <= (w / 2f) * (w / 2f);
                bool inRim = j >= w / 2 - 1 || dx * dx + dy * dy <= (w / 2f + 2f) * (w / 2f + 2f);
                if (!inRim)
                    continue;
                if (!inArch || i < 0 || i >= w)
                {
                    if (j < h)
                        c.Shade(x + i, y + j, gilded ? GoldR : WallLightR, (dx < 0 ? 0.8f : -0.5f) + (j < 0 ? 0.5f : 0f));
                    continue;
                }
                bool band = j == h / 3 || j == h * 2 / 3;
                float light = band ? 0.2f : (i % 3 == 2 ? -1.2f : 0f) + (i < 2 ? -0.8f : 0f) + (j < w / 2 + 1 ? -0.7f : 0f) - 0.2f;
                c.Shade(x + i, y + j, band ? IronR : DoorR, light);
                if (band && i % 3 == 1)
                    c.Px(x + i, y + j, SteelR.Light); // a stud
            }
        c.Px((int)cx + 2, y + h / 2, GoldR.Light); // a ring for a handle
    }

    /// <summary>
    /// A gate: a dark arched passage under voussoirs, a portcullis half raised in it; gilded voussoirs on a grand gate.
    /// </summary>
    private static void Gate(PixelCanvas c, int x, int y, int w, int h, bool gilded = false)
    {
        float rad = w / 2f;
        for (int j = -3; j < h; j++)
            for (int i = -3; i < w + 3; i++)
            {
                float dx = i + 0.5f - rad, dy = j + 0.5f - rad;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                bool arched = j + 0.5f < rad;
                bool inner = i >= 0 && i < w && (!arched || d <= rad);
                bool rim = arched ? d <= rad + 3f : i >= -3 && i < w + 3;
                if (inner)
                {
                    bool bar = j < h * 0.55f && (i % 3 == 1 || j % 4 == 3);
                    if (bar)
                        c.Shade(x + i, y + j, IronR, i % 3 == 1 ? 0.3f - j * 0.02f : -0.4f);
                    else
                        c.Shade(x + i, y + j, GlassR, -1.8f + (j > h - 4 ? 0.5f : 0f));
                }
                else if (rim)
                {
                    // Voussoirs: wedges round the arch, then the jambs' blocks.
                    int stone = arched ? (int)((Mathf.Atan2(dy, dx) + Mathf.Pi) * 5f) : j / 4;
                    float light = (dx < 0 ? 0.6f : -0.4f) + (stone % 2 == 0 ? 0.25f : -0.1f) + (arched && d > rad + 2.2f ? -0.6f : 0f);
                    c.Shade(x + i, y + j, gilded ? GoldR : WallLightR, light);
                }
            }
        c.Lit(x + 2, y + h - 5, w - 4, 4, new Color("6e3a12")); // lamplight on the passage floor
    }

    /// <summary>
    /// A banner in faction colours hanging from a bar with gilded ends: folds down its length, a swallowtail at its
    /// foot, and the realm's gilded lily on it (a small cross on a narrow one).
    /// </summary>
    private static void Banner(PixelCanvas c, int x, int y, int w, int h)
    {
        c.Stroke(x - 1, y, x + w, y, 1.6f, DarkWoodR);
        c.Px(x - 2, y, GoldR.Light);
        c.Px(x + w + 1, y, GoldR.Light);
        for (int j = 1; j <= h; j++)
            for (int i = 0; i < w; i++)
            {
                if (j > h - 3 && Mathf.Abs(i - (w - 1) / 2f) < (j - (h - 3)) * 0.8f)
                    continue; // the swallowtail notch
                float fold = Mathf.Sin((i + 0.5f) / w * Mathf.Tau * (w >= 10 ? 1.5f : 1f)) * 0.35f;
                float light = (i == 0 ? 0.5f : i == w - 1 ? -1f : 0f) + (j == 1 ? 0.8f : 0f) - j * 0.04f + fold - 0.1f;
                c.TeamLit(x + i, y + j, light);
            }
        int cx = x + (w - 1) / 2, cy = y + h / 2 - 1;
        if (w >= 8)
            Lily(c, cx, cy);
        else
        {
            c.Px(cx, cy - 1, GoldR.Light);
            c.Px(cx - 1, cy, GoldR.Light);
            c.Px(cx, cy, GoldR.Light);
            c.Px(cx + 1, cy, GoldR.Base);
            c.Px(cx, cy + 1, GoldR.Base);
        }
    }

    /// <summary>The realm's lily, 5 px wide, gilded, centred on (cx, cy).</summary>
    private static void Lily(PixelCanvas c, int cx, int cy)
    {
        string[] lily =
        {
            "..#..",
            ".###.",
            "#.#.#",
            "#####",
            "..#..",
            ".#.#.",
        };
        for (int j = 0; j < lily.Length; j++)
            for (int i = 0; i < 5; i++)
                if (lily[j][i] == '#')
                    c.Shade(cx - 2 + i, cy - 3 + j, GoldR, j < 2 ? 1.3f : i < 2 ? 0.6f : -0.1f);
    }

    /// <summary>
    /// A flag on a pole <paramref name="pole"/> px tall, the cloth in faction colours rippling in the wind: a wave runs
    /// out from the pole along the cloth over the four frames, the free end flapping most; folds turned from the light
    /// are darker.
    /// </summary>
    private static void Flag(PixelCanvas c, int x, int y, int frame, int length = 14, int pole = 20)
    {
        c.Stroke(x, y, x, y + pole, 1.6f, DarkWoodR, 0.4f);
        c.Ball(x, y - 0.5f, 1.3f, 1.3f, GoldR, 0.5f);
        for (int i = 0; i <= length; i++)
        {
            float amplitude = Mathf.Min(2f, i / 3f);
            float wave = Mathf.Sin(i * 0.6f - frame * Mathf.Pi / 2f);
            float slope = Mathf.Cos(i * 0.6f - frame * Mathf.Pi / 2f);
            int dy = (int)Mathf.Round((wave + 1f) / 2f * amplitude);
            float fold = slope * amplitude * 0.5f;
            int height = i == length ? 3 : 8 - (i > length - 3 ? (i - (length - 3)) : 0);
            for (int j = 0; j < height; j++)
                c.TeamLit(x + 1 + i, y + 1 + dy + j + (8 - height) / 2, (j == 0 ? 0.9f : j == height - 1 ? -0.9f : 0.2f) + fold);
        }
    }

    /// <summary>A torch on a wall bracket whose flame flickers through the frames.</summary>
    private static void Torch(PixelCanvas c, int x, int y, int frame)
    {
        c.Stroke(x, y + 4, x, y + 9, 1.6f, DarkWoodR);
        c.Stroke(x - 2, y + 9, x, y + 7, 1f, IronR);
        int sway = frame switch { 1 => -1, 3 => 1, _ => 0 };
        c.Ball(x, y + 2.5f, 1.8f, 2.2f, Ramp.Of(Fire), 0.3f);
        c.Px(x + sway, y, FireLight);
        c.Px(x, y + 1, FireLight);
        c.Px(x + sway, y - 1, Fire);
        c.LitSoft(x - 3, y - 3, 7, 8, FireGlow);
    }

    /// <summary>
    /// A fire in a brazier or beacon: three tongues of flame licking up from a bed of embers, each tapering to a point,
    /// a bright core in the middle one; through the frames the tongues rise, fall and lean.
    /// </summary>
    private static void Flames(PixelCanvas c, float x, float y, int frame, float size = 1f)
    {
        var flame = Ramp.Of(Fire);
        var core = Ramp.Of(FireLight);
        c.Ball(x, y - 1f, 5.5f * size, 2f * size, flame, -0.2f); // embers
        // Each tongue: its foot's offset, height by frame, lean by frame.
        var tongues = new (float Dx, float Width, int[] Tall, int[] Lean)[]
        {
            (-3f, 3.2f, new[] { 7, 5, 8, 6 }, new[] { -1, -2, 0, -1 }),
            (3f, 3.2f, new[] { 6, 8, 5, 7 }, new[] { 1, 0, 2, 1 }),
            (0f, 4.4f, new[] { 11, 9, 12, 10 }, new[] { 0, 1, -1, 0 }),
        };
        foreach (var (dx, width, tall, lean) in tongues)
            c.Taper(x + dx * size, y - 1f, x + (dx + lean[frame]) * size, y - tall[frame] * size, width * size, 0.6f, flame, 0.7f);
        c.Taper(x, y - 1f, x + tongues[2].Lean[frame] * 0.5f * size, y - tongues[2].Tall[frame] * 0.6f * size, 2.2f * size, 0.5f, core, 1.2f);
        c.LitSoft((int)(x - 7 * size), (int)(y - 13 * size), (int)(14 * size), (int)(15 * size), FireGlow);
    }

    /// <summary>A barrel standing at (x, y) (its foot's centre): staves, iron hoops, a lit lid.</summary>
    private static void Barrel(PixelCanvas c, float x, float y)
    {
        c.GroundShadow(x + 2, y, 4.5f, 1.6f, 0.35f);
        c.Column((int)x - 3, (int)y - 8, 7, 7, WoodR, -0.1f);
        for (int i = -3; i <= 3; i++)
        {
            c.Shade((int)x + i, (int)y - 7, IronR, CylinderShade(i / 3.5f));
            c.Shade((int)x + i, (int)y - 3, IronR, CylinderShade(i / 3.5f) - 0.2f);
        }
        c.Ball(x + 0.5f, y - 8.5f, 3.5f, 1.4f, DarkWoodR, 0.8f, 1f);
    }

    /// <summary>A rack of spears and a shield leaning on it, the rack's foot at (x, y).</summary>
    private static void WeaponRack(PixelCanvas c, int x, int y)
    {
        c.GroundShadow(x + 6, y, 7f, 1.6f, 0.35f);
        c.Stroke(x, y, x, y - 10, 1.4f, DarkWoodR);
        c.Stroke(x + 9, y, x + 9, y - 10, 1.4f, DarkWoodR, -0.4f);
        c.Stroke(x - 1, y - 7, x + 10, y - 7, 1.2f, DarkWoodR, 0.2f);
        for (int i = 0; i < 4; i++)
        {
            int sx = x + 1 + i * 2;
            c.Stroke(sx, y - 1, sx + 1, y - 15, 1f, WoodR, 0.3f);
            c.Px(sx + 1, y - 16, SteelR.Light);
            c.Px(sx + 1, y - 17, SteelR.Base);
        }
        // A round shield in faction colours with an iron boss.
        c.TeamBall(x + 10, y - 4, 3.2f, 3.6f, -0.2f);
        c.Px(x + 10, y - 4, SteelR.Light);
    }

    // ---- The buildings -----------------------------------------------------------------------------------------

    /// <summary>
    /// 96×144 frames, bottom-centre origin; the footprint is 3×3 tiles. A curtain wall with round towers at its four
    /// corners under slate cones stained in the owner's colours; behind it the square keep, its battlements and the flag;
    /// the gate with a portcullis between torches, banners on the towers and the keep.
    /// </summary>
    private static PixelCanvas Castle() => Sheet(96, 144, (c, frame) =>
    {
        // The towers at the back corners and the wall between them, its walk and parapet.
        foreach (int tx in new[] { 13, 83 })
        {
            var top = RoundTower(c, tx, 94, 11, 48, WallR, salt: tx, grime: false, bias: -0.2f);
            ConeRoof(c, tx, top.Y - 1, 13.5f, 26, salt: tx);
        }
        Ashlar(c, 22, 52, 52, 40, WallR, salt: 2, bias: -0.3f, grime: false);
        Flagstones(c, 22, 47, 52, 5, FlagstoneR, 3);
        Battlement(c, 22, 47, 52, WallR);
        Flagstones(c, 20, 92, 56, 12, CourtR, 5); // the courtyard
        // The keep, rising from the yard.
        Flag(c, 48, 0, frame, pole: 26);
        Flagstones(c, 30, 20, 36, 14, FlagstoneR, 1);
        Battlement(c, 30, 20, 36, WallLightR);
        Ashlar(c, 30, 34, 36, 70, WallLightR, salt: 1, grime: false);
        Battlement(c, 30, 34, 36, WallLightR);
        ArchWindow(c, 35, 44, 4, 8);
        ArchWindow(c, 57, 44, 4, 8);
        ArchWindow(c, 46, 76, 4, 9);
        ArchWindow(c, 36, 82, 3, 6);
        ArchWindow(c, 57, 82, 3, 6);
        Banner(c, 41, 38, 14, 24);
        // The front wall and its gate.
        Flagstones(c, 18, 102, 60, 6, FlagstoneR, 4);
        Ashlar(c, 18, 108, 60, 34, WallR, salt: 4);
        Battlement(c, 18, 108, 60, WallR);
        Gate(c, 38, 117, 20, 25);
        Torch(c, 31, 118, frame);
        Torch(c, 65, 118, (frame + 2) % 4);
        Slit(c, 27, 124, 5);
        Slit(c, 69, 124, 5);
        // The towers at the front corners.
        foreach (int tx in new[] { 13, 83 })
        {
            var top = RoundTower(c, tx, 142, 13, 58, WallLightR, salt: tx + 7);
            RingBattlement(c, tx, top.Y, 14.5f, WallLightR, inside: () => ConeRoof(c, tx, top.Y, 12f, 31, salt: tx + 1));
            Slit(c, tx - 1, 89, 6);
            Banner(c, tx - 5, 104, 11, 18);
        }
    });

    /// <summary>
    /// 128×184 frames, bottom-centre origin; the footprint is 4×4 tiles. The Defense mode's citadel: a high curtain wall
    /// with four great towers, a tall keep crowned in gold behind, a double gate between torches, and banners and flags
    /// in the garrison's colours.
    /// </summary>
    private static PixelCanvas Citadel() => Sheet(128, 184, (c, frame) =>
    {
        foreach (int tx in new[] { 16, 112 })
        {
            var top = RoundTower(c, tx, 124, 13, 60, WallR, salt: tx, grime: false, bias: -0.2f);
            Flag(c, tx, (int)top.Y - 42, (frame + tx / 16) % 4, 10, pole: 16);
            ConeRoof(c, tx, top.Y - 1, 15.5f, 30, salt: tx);
        }
        Ashlar(c, 26, 74, 76, 48, WallR, salt: 4, bias: -0.3f, grime: false);
        Flagstones(c, 26, 68, 76, 6, FlagstoneR, 5);
        Battlement(c, 26, 68, 76, WallR);
        Flagstones(c, 24, 122, 80, 14, CourtR, 6); // the courtyard
        // The keep, crowned in gold.
        Flag(c, 64, 0, frame, pole: 30);
        Flagstones(c, 40, 26, 48, 18, FlagstoneR, 2);
        Battlement(c, 40, 26, 48, WallLightR, GoldR);
        Ashlar(c, 40, 44, 48, 96, WallLightR, salt: 3, grime: false);
        Battlement(c, 40, 44, 48, WallLightR, GoldR);
        for (int x = 40; x < 88; x++)
        {
            c.Shade(x, 45, GoldR, x < 46 ? 1.2f : x > 84 ? -0.6f : 0.6f);
            c.Shade(x, 46, GoldR, -0.8f);
        }
        foreach (int x in new[] { 45, 61, 77 })
            ArchWindow(c, x, 54, 5, 10);
        ArchWindow(c, 47, 100, 4, 8);
        ArchWindow(c, 77, 100, 4, 8);
        Banner(c, 56, 70, 16, 30);
        // The front wall and the double gate.
        Flagstones(c, 26, 135, 76, 6, FlagstoneR, 7);
        Ashlar(c, 26, 141, 76, 41, WallR, salt: 8);
        Battlement(c, 26, 141, 76, WallR);
        Gate(c, 49, 151, 30, 31, gilded: true);
        c.Rect(63, 151 + 15, 2, 16, IronR.Shade);
        Torch(c, 42, 152, frame);
        Torch(c, 85, 152, (frame + 2) % 4);
        Banner(c, 30, 145, 10, 16);
        Banner(c, 88, 145, 10, 16);
        foreach (int tx in new[] { 16, 112 })
        {
            var top = RoundTower(c, tx, 182, 16, 70, WallLightR, salt: tx + 9);
            IronBand(c, tx, top.Y + 8 + 22, 16f);
            RingBattlement(c, tx, top.Y, 17.5f, WallLightR, GoldR, () => ConeRoof(c, tx, top.Y, 14.5f, 38, gilded: true, salt: tx + 2));
            Slit(c, tx - 1, 120, 7);
            Banner(c, tx - 6, 138, 13, 24);
        }
    });

    /// <summary>
    /// 64×84 frames, bottom-centre origin; footprint 2×2 tiles. A long timber-framed hall on a stone footing under a
    /// pitched roof of slates stained in the owner's colours, a dormer in its front plane, a smoking chimney, a flag on
    /// the ridge; a broad door under a shield in the owner's colours, between banners and shuttered windows; a rack of
    /// spears and a barrel before it. The roof is kept low and the walls high, so the hall reads as a hall, not a roof.
    /// </summary>
    private static PixelCanvas Barracks() => Sheet(64, 84, (c, frame) =>
    {
        // The chimney, rising through the back of the roof.
        Ashlar(c, 45, 12, 7, 22, WallR, salt: 7, grime: false);
        c.Block(44, 9, 9, 3, WallLightR, top: 1);
        // The roof: the back plane over the ridge, catching the sun, then the front one; dark barge boards at the ends.
        SlateRoof(c, 2, 62, 31, 41, 0.5f, 1);
        SlateRoof(c, 2, 62, 41, 56, -0.1f, 2);
        for (int x = 2; x < 62; x++)
            c.Shade(x, 41, DarkWoodR, x % 6 == 0 ? -0.6f : 0.6f); // the ridge
        c.Stroke(2, 31, 2, 55, 1.6f, DarkWoodR, 0.3f);
        c.Stroke(61, 31, 61, 55, 1.6f, DarkWoodR, -0.6f);
        Flag(c, 9, 20, frame, 11, pole: 21);
        // A dormer in the front plane: a little gable of plaster with a window, roofed like the hall.
        c.Block(27, 47, 11, 8, PlasterR);
        ShutteredWindow(c, 31, 49, 3, 3);
        c.Polygon(new[] { new Vector2(24, 48), new Vector2(32.5f, 41), new Vector2(41, 48) }, null, (x, y) =>
            (x < 32 ? 0.5f : -0.4f) + ((y - 41) % 3 == 2 ? -0.6f : 0f) - RoofStain);
        c.Line(24, 48, 32, 41, DarkWoodR.Base); // barge boards along the dormer's gable
        c.Line(33, 41, 41, 48, DarkWoodR.Shade);
        // Timber-framed walls: dark posts and rails with braces, plaster between, darkened under the eaves.
        for (int y = 56; y < 79; y++)
            for (int x = 6; x < 58; x++)
            {
                int bay = (x - 6) % 13;
                bool post = bay == 0 || x == 57;
                bool rail = y == 56 || y == 66 || y == 78;
                bool brace = !post && (bay == y - 56 || bay == 12 - (y - 56)) && y < 66;
                float shadow = y < 59 ? -0.7f : 0f;
                if (post || rail || brace)
                    c.Shade(x, y, DarkWoodR, (post && bay == 0 ? 0.3f : rail && y == 78 ? -0.5f : 0f) + shadow);
                else
                    c.Shade(x, y, PlasterR, shadow - (x > 54 ? 0.6f : 0f) + (x < 9 ? 0.3f : 0f));
            }
        Ashlar(c, 5, 79, 54, 4, WallR, salt: 8);
        ArchDoor(c, 26, 63, 12, 16);
        c.TeamBall(32, 59.5f, 3.2f, 3.4f, 0.2f); // the shield over the door
        c.Px(32, 59, SteelR.Light);
        ShutteredWindow(c, 11, 61, 5, 5);
        ShutteredWindow(c, 48, 61, 5, 5);
        Banner(c, 20, 58, 4, 12);
        Banner(c, 40, 58, 4, 12);
        // Before the hall.
        WeaponRack(c, 2, 83);
        Barrel(c, 58, 83);
    });

    /// <summary>
    /// 64×128 frames, bottom-centre origin; footprint 2×2 tiles. A square bastion with a gate, on it a round stone tower,
    /// an archers' gallery under a cone roof in the owner's colours, the flag on its peak. The tower grows with each
    /// level, so the level shows in its silhouette. Level 2: taller, a stone gallery, iron bands, bigger banners;
    /// level 3: taller still, gilded eaves, finial and gate, merlons capped in gold, a great banner on the tower.
    /// </summary>
    private static PixelCanvas Tower(int tier = 1) => Sheet(64, 128, (c, frame) =>
    {
        int height = tier switch { 1 => 34, 2 => 42, _ => 46 }, roof = tier switch { 1 => 26, 2 => 28, _ => 30 };
        var caps = tier >= 3 ? GoldR : (Ramp?)null;
        // The bastion's top, round the tower's foot.
        Flagstones(c, 6, 80, 52, 28, FlagstoneR, tier);
        Battlement(c, 6, 80, 52, WallR, caps);
        // The tower, bound in iron from the second level; the flag on the roof's peak, drawn first so the roof hides its foot.
        var top = RoundTower(c, 32, 101, 14, height, WallLightR, salt: tier + 20);
        int apex = (int)top.Y - 9 - roof;
        Flag(c, 32, apex - 8, frame, pole: 10);
        int up = (int)top.Y; // the tower's top, under the gallery
        if (tier >= 2)
        {
            IronBand(c, 32, up + 14, 14f);
            IronBand(c, 32, 86, 14f);
        }
        Slit(c, 25, up + 18, 6);
        Slit(c, 38, up + 18, 6);
        if (tier >= 3)
        {
            c.Rect(24, up + 21, 16, 26, GoldR.Shade);
            Banner(c, 25, up + 21, 14, 24);
        }
        else
        {
            ArchWindow(c, 30, up + 22, 4, 8);
        }
        // The archers' gallery: timber hoarding, then stone with machicolations, its loopholes between.
        float ry = 8.5f, galleryFoot = top.Y + ry + 1f;
        for (int x = 15; x <= 49; x++)
        {
            float u = (x + 0.5f - 32f) / 17f;
            if (Mathf.Abs(u) > 1f)
                continue;
            int bottom = (int)Mathf.Round(galleryFoot - ry + ry * Mathf.Sqrt(1f - u * u));
            for (int y = bottom - 10; y < bottom; y++)
            {
                int j = y - (bottom - 10);
                float light = CylinderShade(u) + (j == 0 ? 0.6f : j == 9 ? -1.1f : 0f);
                bool loophole = j >= 3 && j <= 6 && (x - 15) % 8 == 4;
                if (loophole)
                    c.Px(x, y, GlassR.Deep);
                else if (tier == 1)
                    c.Shade(x, y, (x - 15) % 8 == 0 ? DarkWoodR : WoodR, light - ((x - 15) % 8 == 0 ? 0.2f : 0f) + ((x + j) % 3 == 0 ? -0.3f : 0f));
                else
                    c.Shade(x, y, WallR, light + MasonryLight(x, 9 - j, 33, out _) * 0.6f);
            }
            // Corbels under the gallery.
            if ((x - 15) % 5 == 0)
                c.Shade(x, bottom, tier == 1 ? DarkWoodR : WallR, CylinderShade(u) - 0.6f);
        }
        ConeRoof(c, 32, top.Y - 9, 20f, roof, gilded: tier >= 3, salt: tier);
        // The bastion's front and its gate.
        Ashlar(c, 6, 108, 52, 18, WallR, salt: 10);
        Battlement(c, 6, 108, 52, WallR, caps);
        if (tier >= 3)
            ArchDoor(c, 26, 112, 12, 14, gilded: true);
        else
            Gate(c, 26, 112, 12, 14);
        if (tier >= 2)
        {
            Banner(c, 9, 109, 10, 14);
            Banner(c, 45, 109, 10, 14);
        }
        else
        {
            Banner(c, 11, 110, 7, 11);
            Banner(c, 46, 110, 7, 11);
        }
    });

    /// <summary>
    /// 96×176 frames, bottom-centre origin; footprint 3×3 tiles. A bastion with a gate and on it a slim square tower
    /// turned corner-on to the viewer, so two of its faces show — the left one in the sun, the right one in shade, the
    /// courses of masonry running back along each — the way the round archer tower shows its curve. At the top an open
    /// lookout of timber, its floor a diamond seen from above with railings along its edges, a beacon fire (flickering
    /// through the frames) in the middle, under a hipped pyramid roof in the owner's colours whose two near slopes show.
    /// </summary>
    private static PixelCanvas Watchtower() => Sheet(96, 176, (c, frame) =>
    {
        const float cx = 48f;
        // The bastion's top.
        Flagstones(c, 10, 136, 76, 22, FlagstoneR, 12);
        Battlement(c, 10, 136, 76, WallR);
        // The tower, corner-on: its near corner's foot at (cx, foot), its side corners half a face back.
        // A plinth a little wider than the tower carries it, and the tower's shadow falls on the flagstones to its right.
        const float half = 16f, foot = 150f;
        c.GroundShadow(cx + 12f, foot - 3f, 20f, 5f, 0.4f);
        int Bottom(float u, float nearFoot) => (int)Mathf.Round(nearFoot - Mathf.Abs(u) / 2f); // the foot runs back from the near corner
        for (int x = (int)(cx - half - 2); x < (int)(cx + half + 2); x++)
        {
            float u = x + 0.5f - cx;
            int bottom = Bottom(u, foot + 1f);
            for (int y = bottom - 4; y < bottom; y++)
                c.Shade(x, y, WallR, (u < 0f ? 0.4f : -0.8f) + (y == bottom - 4 ? 0.7f : y == bottom - 1 ? -0.5f : 0f));
        }
        for (int x = (int)(cx - half); x < (int)(cx + half); x++)
        {
            float u = x + 0.5f - cx;
            bool left = u < 0f;
            int bottom = Bottom(u, foot - 4f);
            int along = left ? x - (int)(cx - half) : x - (int)cx;
            for (int y = 60; y < bottom; y++)
            {
                int up = bottom - 1 - y;
                float light = MasonryLight(along, up, 11, out _) + (left ? 0.35f : -0.9f) + (x == (int)cx - 1 ? 0.5f : 0f)
                              - (up < 2 ? 0.3f : 0f);
                c.Shade(x, y, left ? WallLightR : SlateR, light + (left ? 0f : 0.6f));
            }
        }
        // Narrow windows, one in each face, lit at night.
        foreach (var (x, y) in new[] { (39, 100), (55, 112) })
        {
            c.Rect(x, y, 2, 7, GlassR.Deep);
            c.Rect(x, y + 7, 2, 1, WallLightR.Light);
            c.Lit(x, y, 2, 7, WindowGlow);
        }
        // The lookout's floor: a diamond seen from above, its near corner at the front.
        const float floorY = 70f, reach = 27f, depth = 13.5f;
        float Edge(float x) => depth * (1f - Mathf.Abs(x - cx) / reach); // the floor's half-height above and below floorY at x
        // Struts from the tower's faces out to the floor's corners.
        c.Stroke(35, 96, 23, 72, 2f, DarkWoodR, -0.2f);
        c.Stroke(61, 96, 73, 72, 2f, DarkWoodR, -0.7f);
        // The far posts and railing first, the floor in front of them.
        c.Stroke(cx - reach + 1, floorY, cx - reach - 1, 40, 2.4f, DarkWoodR);
        c.Stroke(cx + reach - 1, floorY, cx + reach + 1, 40, 2.4f, DarkWoodR, -0.6f);
        c.Stroke(cx, floorY - depth, cx, 24, 2.4f, DarkWoodR, -0.3f);
        Railing(c, cx, floorY, reach, depth, far: true);
        for (int y = (int)(floorY - depth); y <= (int)(floorY + depth); y++)
            for (int x = (int)(cx - reach); x <= (int)(cx + reach); x++)
            {
                if (Mathf.Abs(y + 0.5f - floorY) > Edge(x + 0.5f))
                    continue;
                // Planks run from the left corner towards the near one.
                float plank = (y - floorY + (x - cx) * 0.5f + 64f) % 4f;
                c.Shade(x, y, WoodR, plank >= 3f ? -0.9f : 0.1f);
            }
        // The beacon in the middle of the floor.
        c.Ball(cx, floorY + 1, 7f, 3f, IronR, -0.2f);
        c.Rect((int)cx - 6, (int)floorY + 2, 13, 3, IronR.Shade);
        Flames(c, cx, floorY, frame, 1.1f);
        // The floor's front edge (a fascia of boards), the near railing and posts.
        for (int x = (int)(cx - reach); x <= (int)(cx + reach); x++)
        {
            int edge = (int)Mathf.Round(floorY + Edge(x + 0.5f));
            for (int j = 0; j < 4; j++)
                c.Shade(x, edge + j, DarkWoodR, (x < cx ? 0.4f : -0.6f) + (j == 0 ? 0.6f : 0f));
        }
        Railing(c, cx, floorY, reach, depth, far: false);
        c.Stroke(cx, floorY + depth, cx, 55, 2.4f, WoodR, -0.1f);
        // The roof: a hipped pyramid over the lookout, its two near slopes showing, slates in rows along the eaves.
        const float eaves = 38f, roofReach = 32f, roofDepth = 16f, apex = 12f;
        var l = new Vector2(cx - roofReach, eaves);
        var r = new Vector2(cx + roofReach, eaves);
        var near = new Vector2(cx, eaves + roofDepth);
        var top = new Vector2(cx, apex);
        Flag(c, (int)cx, (int)apex - 8, frame, pole: 10);
        float Slope(int x, int y, bool sunny)
        {
            float eave = eaves + (sunny ? x - l.X : r.X - x) * roofDepth / roofReach; // the eave line under this column
            float fromEave = eave - y;
            if (fromEave < 1.5f)
                return -1.2f - RoofStain + 0.4f; // the eave's edge
            return (sunny ? 0.75f : -0.25f) + (fromEave % 3f >= 2f ? -0.7f : 0f) - RoofStain;
        }

        c.Polygon(new[] { top, l, near }, null, (x, y) => Slope(x, y, true));
        c.Polygon(new[] { top, near, r }, null, (x, y) => Slope(x, y, false));
        c.Line((int)cx, (int)apex + 2, (int)cx, (int)near.Y - 1, SlateR.Deep); // the hip between them
        // The bastion's front.
        Ashlar(c, 10, 158, 76, 16, WallR, salt: 12);
        Battlement(c, 10, 158, 76, WallR);
        Gate(c, 40, 160, 16, 14);
        Torch(c, 34, 159, frame);
        Torch(c, 62, 159, (frame + 2) % 4);
        Banner(c, 15, 159, 10, 13);
        Banner(c, 71, 159, 10, 13);
    });

    /// <summary>
    /// The railing along two edges of the watchtower's diamond floor (centre cx, floorY, half-width
    /// <paramref name="reach"/>, half-depth <paramref name="depth"/>): the far edges or the near ones, a rail 7 px up on
    /// balusters.
    /// </summary>
    private static void Railing(PixelCanvas c, float cx, float floorY, float reach, float depth, bool far)
    {
        float sign = far ? -1f : 1f;
        for (int x = (int)(cx - reach); x <= (int)(cx + reach); x++)
        {
            float edge = floorY + sign * depth * (1f - Mathf.Abs(x + 0.5f - cx) / reach);
            var ramp = far ? DarkWoodR : WoodR;
            float light = (x < cx ? 0.3f : -0.6f) - (far ? 0.4f : 0f);
            c.Shade(x, (int)Mathf.Round(edge - 7f), ramp, light + 0.5f);
            c.Shade(x, (int)Mathf.Round(edge - 6f), ramp, light - 0.3f);
            if ((x - (int)(cx - reach)) % 5 == 0)
                for (int y = (int)Mathf.Round(edge - 5f); y < (int)Mathf.Round(edge); y++)
                    c.Shade(x, y, ramp, light - 0.2f);
        }
    }

    /// <summary>
    /// 64×92 frames, bottom-centre origin; footprint 2×2 tiles. A platform with a catapult on it: a timber frame, the
    /// throwing arm loaded with a stone, a box of stones hung from its short end as the counterweight, a pile of shot; a
    /// banner in faction colours. Level 1 stands on a deck behind a palisade of logs; level 2 on stone with merlons,
    /// the counterweight bound in iron; level 3: gilded trim and banners at the sides.
    /// </summary>
    private static PixelCanvas Catapult(int tier = 1) => Sheet(64, 92, (c, frame) =>
    {
        var timber = tier >= 3 ? Ramp.Of(new Color("8f6a40"), mood: BuildingMood) : WoodR;
        var caps = tier >= 3 ? GoldR : (Ramp?)null;
        if (tier >= 2)
        {
            Flagstones(c, 4, 50, 56, 24, FlagstoneR, tier + 30);
            Battlement(c, 4, 50, 56, WallR, caps);
        }
        else
        {
            // A deck of planks running back from the palisade.
            for (int y = 50; y < 74; y++)
                for (int x = 4; x < 60; x++)
                    c.Shade(x, y, WoodR, ((x - 4) % 7 == 6 ? -0.9f : 0.2f) - (x > 57 ? 0.6f : 0f), solid: false);
        }
        Flag(c, 8, 26, frame, 10, pole: 26);
        // The engine's bed of beams, the far upright, the near one, the crossbar.
        c.Stroke(12, 59, 52, 59, 2.4f, DarkWoodR, -0.4f);
        c.Stroke(44, 66, 44, 36, 3.4f, DarkWoodR);
        c.Stroke(19, 66, 19, 36, 3.6f, timber);
        c.Stroke(16, 35, 48, 35, 3f, DarkWoodR, 0.3f);
        if (tier >= 2)
            foreach (int x in new[] { 19, 44 })
                c.Rect(x - 2, 44, 4, 2, IronR.Base);
        // The throwing arm, raised, a counterweight at its foot and a stone in the cup at its head.
        c.Stroke(14, 55, 53, 17, 3f, timber, 0.3f);
        // The counterweight: a box of stones bound in timber (iron from the second level), hung from the arm's short end.
        c.Stroke(14, 55, 14, 58, 1f, IronR);
        c.Block(8, 58, 13, 10, tier >= 2 ? IronR : DarkWoodR, top: 2);
        for (int x = 10; x < 19; x += 4)
            c.Ball(x + 1, 59.5f, 1.6f, 1.2f, StoneR, 0.4f);
        c.Ball(53f, 15f, 5f, 3f, DarkWoodR, -0.2f);
        c.Ball(53f, 11f, 3.4f, 3.4f, StoneR, 0.4f);
        c.Ball(32f, 37f, 2.4f, 2.4f, tier >= 3 ? GoldR : IronR, 0.6f);
        if (tier >= 3)
            c.Rect(52, 18, 3, 2, GoldR.Light);
        c.Stroke(10, 68, 54, 68, 3f, DarkWoodR, 0.2f);
        // A pile of shot.
        foreach (var (x, y) in new[] { (50f, 68f), (55f, 68f), (52.5f, 64.5f) })
            c.Ball(x, y, 2.6f, 2.4f, StoneR, 0.2f);
        // The platform's front: a palisade of logs, then masonry.
        if (tier >= 2)
            Ashlar(c, 4, 74, 56, 16, WallR, salt: 13);
        else
            for (int x = 4; x < 60; x += 4)
            {
                c.Column(x, 74, 4, 16, x % 8 == 0 ? WoodR : DarkWoodR, x > 54 ? -0.5f : 0f);
                c.Shade(x + 1, 73, WoodR, 0.8f);
                c.Shade(x + 2, 73, WoodR, 0.2f);
            }
        if (tier >= 2)
            Battlement(c, 4, 74, 56, WallR, caps);
        if (tier >= 3)
            for (int x = 4; x < 60; x++)
                c.Shade(x, 74, GoldR, x < 10 ? 1f : 0.4f);
        Banner(c, 26, 75, 12, 12);
        if (tier >= 3)
        {
            Banner(c, 8, 75, 8, 11);
            Banner(c, 48, 75, 8, 11);
        }
    });

    /// <summary>
    /// 64×80 frames, bottom-centre origin; footprint 2×2 tiles on a gold deposit. The mine is dug into the deposit's own
    /// outcrop (<see cref="GoldOutcrop"/>, the same rocks in the same places, so the vein visibly becomes the mine): a
    /// timbered adit cut into its face with rails running out of it, a headframe on the rock over the shaft whose wheel
    /// turns through the frames, a cart of gold (the view holds the first frame while the mine stands idle). The game
    /// stops drawing the deposit under a finished mine. Level 2: the adit framed in stone, a lantern, a braced
    /// headframe, a second cart; level 3: a steel headframe with a gilded wheel and a heap of gold at the adit.
    /// </summary>
    private static PixelCanvas Mine(int tier = 1) => Sheet(64, 80, (c, frame) =>
    {
        const int foot = 16; // the footprint's top edge in the frame
        var glint = new Color("fff6c0");
        GoldOutcrop(c, foot, mined: true);
        // The headframe on the big rock, over the shaft: timber, then steel. Two splayed legs and a crossbeam.
        var post = tier >= 3 ? SteelR : WoodR;
        var far = tier >= 3 ? IronR : DarkWoodR;
        c.Ball(32, foot + 18, 4f, 1.6f, GlassR, -1.6f); // the shaft's mouth
        c.Stroke(32, 15, 32, foot + 18, 1f, RopeR, -0.3f);
        c.Stroke(21, foot + 21, 28, 11, 2.6f, post);
        c.Stroke(43, foot + 21, 36, 11, 2.6f, far);
        c.Stroke(23, 11, 41, 11, 2.6f, far, 0.4f);
        if (tier >= 2)
            c.Stroke(24, foot + 14, 40, foot + 6, 1.6f, post, -0.3f); // a brace
        var wheel = tier >= 3 ? GoldR : IronR;
        c.Ball(32, 15, 4.6f, 4.6f, wheel, 0.2f);
        c.Ball(32, 15, 3f, 3f, tier >= 3 ? GoldR : DarkWoodR, -0.6f);
        float angle = frame * Mathf.Pi / 4f;
        for (int s = 0; s < 2; s++)
        {
            float a = angle + s * Mathf.Pi / 2f;
            c.Stroke(32 - Mathf.Cos(a) * 3f, 15 - Mathf.Sin(a) * 3f, 32 + Mathf.Cos(a) * 3f, 15 + Mathf.Sin(a) * 3f, 1f, tier >= 3 ? GoldR : WoodR, 0.5f);
        }
        c.Ball(32, 15, 1.1f, 1.1f, SteelR, 0.8f);
        Flag(c, 41, 0, frame, 9, pole: 11);
        // The winding house by the left rock, roofed in the owner's colours, its rope running up to the wheel.
        c.Block(3, 51, 17, 10, WoodR);
        for (int x = 6; x < 18; x += 3)
            c.Rect(x, 52, 1, 8, DarkWoodR.Shade);
        ShutteredWindow(c, 9, 54, 4, 3);
        SlateRoof(c, 1, 22, 39, 45, 0.5f, 5);
        SlateRoof(c, 1, 22, 45, 52, -0.1f, 6);
        c.Line(21, 44, 29, 17, RopeR.Base);
        // The adit cut into the front rock: a dark mouth, a frame of timber (then stone) round it, lamplight at night.
        const int left = 27, right = 38, top = foot + 35, bottom = foot + 49;
        for (int y = top; y < bottom; y++)
            for (int x = left; x < right; x++)
                c.Shade(x, y, GlassR, y < top + 2 ? -2f : -1.3f);
        c.LitSoft(left, top + 2, right - left, bottom - top - 1, new Color("b8641e"));
        var frameR = tier >= 2 ? WallLightR : WoodR;
        c.Stroke(left - 1, top, left - 1, bottom, 2.4f, frameR);
        c.Stroke(right, top, right, bottom, 2.4f, frameR, -0.7f);
        c.Stroke(left - 3, top - 1, right + 2, top - 1, 2.6f, tier >= 3 ? GoldR : frameR, 0.4f);
        // Rails out of the adit, sleepers across them.
        for (int y = bottom - 4; y < 80; y += 3)
            c.Rect(left + 1, y, right - left - 2, 1, DarkWoodR.Shade);
        c.Stroke(left + 2, bottom - 6, left + 2, 79, 1f, IronR, 0.6f);
        c.Stroke(right - 3, bottom - 6, right - 3, 79, 1f, IronR, 0.2f);
        if (tier >= 2)
        {
            c.Stroke(right + 4, top + 1, right + 4, top + 6, 1f, IronR);
            c.Ball(right + 4, top + 7, 1.6f, 2f, Ramp.Of(new Color("ffd25a"), mood: FigureMood), 0.8f); // a lantern by the adit
            c.LitSoft(right + 1, top + 4, 7, 7, WindowGlow);
        }
        // Carts of gold beside the rails: one, then two; at the top level a heap of gold at the adit's mouth.
        Cart(c, 44, 63, frame, glint, 0);
        if (tier >= 2)
            Cart(c, 3, 63, frame, glint, 2);
        if (tier >= 3)
        {
            c.Ball(32f, 75f, 7f, 3f, GoldR, 0.4f);
            c.Ball(31f, 73.5f, 3.5f, 2f, GoldR, 1.1f);
            if (frame == 1)
                c.Px(30, 72, glint);
        }
    });

    /// <summary>A mine cart heaped with gold at (x, y), its load glinting on the frame given by <paramref name="shine"/>.</summary>
    private static void Cart(PixelCanvas c, int x, int y, int frame, Color glint, int shine)
    {
        c.GroundShadow(x + 9, y + 13, 9f, 2f, 0.35f);
        c.Block(x, y + 2, 16, 8, DarkWoodR, top: 1);
        c.Stroke(x, y + 5, x + 15, y + 5, 1f, IronR);
        c.Ball(x + 8f, y + 1.5f, 7f, 3.5f, GoldR, 0.6f);
        c.Ball(x + 4f, y + 11f, 2.2f, 2.2f, IronR, 0f);
        c.Ball(x + 12f, y + 11f, 2.2f, 2.2f, IronR, 0f);
        if (frame == shine || frame == shine + 1)
            c.Px(x + 4 + frame * 3, y - 1, glint);
    }

    /// <summary>
    /// 64×64 ore patch drawn on the 2×2 deposit footprint (top-left origin): one outcrop of rock (<see cref="GoldOutcrop"/>)
    /// with gold veins across it and nuggets at its foot.
    /// </summary>
    private static PixelCanvas Deposit()
    {
        var c = new PixelCanvas(64, 64);
        GoldOutcrop(c, 0, mined: false);
        c.SelOut(SilhouetteRim);
        return c;
    }

    /// <summary>
    /// The outcrop of a gold deposit on its 2×2 footprint, whose top edge is <paramref name="top"/> px down the canvas:
    /// a big rock at the back, two at the sides and a broad face in front, gathered into one mound on a patch of
    /// shadow, gold veins across them. The mine (<paramref name="mined"/>) is dug into the same rocks; its nuggets are
    /// carted off.
    /// </summary>
    private static void GoldOutcrop(PixelCanvas c, int top, bool mined)
    {
        c.GroundShadow(34, top + 44, 28f, 9f, 0.3f);
        var rocks = new (float X, float Y, float W, float H, int Seed)[]
        {
            (32, 22, 14, 11, 1), (15, 34, 10, 9, 2), (49, 33, 10, 9, 4), (32, 40, 13, 11, 5), (8, 51, 3, 2, 6), (57, 50, 3, 2, 7),
        };
        foreach (var (x, y, w, h, seed) in rocks)
            Outcrop(c, x, top + y, w, h, seed, veins: w > 5);
        if (mined)
            return;
        foreach (var (x, y) in new[] { (18, 50), (43, 53), (29, 56) })
        {
            c.Ball(x, top + y, 2.2f, 1.6f, OreR, 0.8f);
            c.Px(x - 1, top + y - 1, new Color("fff6c0"));
        }
    }

    /// <summary>
    /// A rock of the outcrop at (cx, cy), w × h: an angular block, an irregular polygon whose base sits flat on the
    /// ground, cut into flat facets that meet at a ridge up and left of its middle. Each facet takes one flat tone by the
    /// way it faces — those towards the sun (up-left) light, those away in shade, a top face round the ridge brightest —
    /// so the rock reads as hewn stone, not as a lumpy ball. A seam of gold runs across it in the tone of the facet it
    /// crosses, so it sits in the rock; a few chips of ore glint in its lit faces.
    /// </summary>
    private static void Outcrop(PixelCanvas c, float cx, float cy, float w, float h, int seed, bool veins = true)
    {
        const int n = 6;
        var corners = new Vector2[n];
        float turn = Hash(seed, 2, 35) % 100 / 100f * Mathf.Tau / n;
        for (int k = 0; k < n; k++)
        {
            float a = turn + (k + (Hash(seed, k, 36) % 30 - 15) / 100f) * Mathf.Tau / n;
            float r = 0.78f + Hash(k, seed, 37) % 30 * 0.01f;
            var p = new Vector2(cx + Mathf.Cos(a) * w * r, cy + Mathf.Sin(a) * h * r);
            if (p.Y > cy + h * 0.55f)
                p.Y = cy + h * 0.55f + (p.Y - cy - h * 0.55f) * 0.25f; // the base sits flat
            corners[k] = p;
        }
        var ridge = new Vector2(cx - w * 0.18f, cy - h * 0.3f);
        float Cross(Vector2 a, Vector2 b, Vector2 p) => (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
        float FacetLight(int k)
        {
            // The facet's outward normal (perpendicular to its outer edge), the sun from the upper left.
            var a = corners[k];
            var b = corners[(k + 1) % n];
            var normal = new Vector2(b.Y - a.Y, a.X - b.X).Normalized();
            return Mathf.Round(Mathf.Clamp(-normal.X * 1.2f - normal.Y * 0.6f, -2f, 0f));
        }

        int Side(Vector2 p)
        {
            // The side facet whose triangle (ridge and its outer edge) holds p, or -1 outside the rock.
            for (int k = 0; k < n; k++)
            {
                var a = corners[k];
                var b = corners[(k + 1) % n];
                float s0 = Cross(ridge, a, p), s1 = Cross(a, b, p), s2 = Cross(b, ridge, p);
                if (s0 >= 0f && s1 >= 0f && s2 >= 0f || s0 <= 0f && s1 <= 0f && s2 <= 0f)
                    return k;
            }
            return -1;
        }

        int Facet(float px, float py)
        {
            // The facet holding (px, py): -1 outside the rock, n on the top face — the rock's outline shrunk round the ridge.
            var p = new Vector2(px, py);
            int side = Side(p);
            if (side < 0)
                return -1;
            return Side(ridge + (p - ridge) / 0.55f) >= 0 ? n : side;
        }

        float LightAt(int facet, int y) => facet == n ? 1f : FacetLight(facet) - (y > cy + h * 0.35f ? 1f : 0f);
        for (int y = (int)Mathf.Floor(cy - h); y <= (int)Mathf.Ceil(cy + h); y++)
            for (int x = (int)Mathf.Floor(cx - w); x <= (int)Mathf.Ceil(cx + w); x++)
            {
                int facet = Facet(x + 0.5f, y + 0.5f);
                if (facet >= 0)
                    c.Shade(x, y, RockR, LightAt(facet, y));
            }
        if (!veins)
            return;
        // The seam: two pixels deep, gently bowed, its tone following the facets it crosses.
        float vy = cy - h * 0.05f + (seed % 3 - 1) * 2f, slope = seed % 2 == 0 ? 0.3f : -0.25f;
        for (int i = -(int)w; i <= (int)w; i++)
        {
            int px = (int)cx + i, py = (int)Mathf.Round(vy + i * slope + Mathf.Sin(i * 0.35f + seed) * 1.2f);
            for (int dy = 0; dy < 2; dy++)
            {
                int facet = Facet(px + 0.5f, py + dy + 0.5f);
                if (facet >= 0 && Facet(px + 0.5f, py + dy + 2.5f) >= 0)
                    c.Shade(px, py + dy, OreR, LightAt(facet, py + dy) + (dy == 0 ? 1f : 0f));
            }
        }
        // Chips of ore in the lit faces.
        for (int k = 0; k < 3; k++)
        {
            int px = (int)(cx - w * 0.6f + Hash(seed, k, 38) % (int)(w * 1.2f)), py = (int)(cy - h * 0.5f + Hash(k, seed, 39) % (int)(h * 0.9f));
            int facet = Facet(px + 0.5f, py + 0.5f);
            if (facet < 0 || LightAt(facet, py) < 0f)
                continue;
            c.Shade(px, py, OreR, 2f);
            c.Shade(px + 1, py, OreR, 1f);
            c.Shade(px, py + 1, OreR, 0f);
        }
    }
}

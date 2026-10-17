using System;
using Godot;
using Warcrown.World;

namespace Warcrown.Dev;

/// <summary>The buildings, every level, with faction-colour masks and night lights; and the gold ore.</summary>
public static partial class ArtGenerator
{
    /// <summary>Warm light of a lit window at night.</summary>
    private static readonly Color WindowGlow = new("ffc24a");
    /// <summary>Light of an open fire (beacon, torch) at night.</summary>
    private static readonly Color FireGlow = new("ff8a2a");
    private static readonly Color Fire = new("ff7a1a");
    private static readonly Color FireLight = new("ffd25a");

    private static readonly Ramp WallR = Ramp.Of(new Color("8e8983"), mood: FigureMood);
    private static readonly Ramp WallLightR = Ramp.Of(new Color("a8a39b"), mood: FigureMood);
    private static readonly Ramp GlassR = Ramp.Of(new Color("2c2630"), mood: FigureMood);
    private static readonly Ramp DoorR = Ramp.Of(new Color("5a3a22"), mood: FigureMood);
    private static readonly Ramp ThatchR = Ramp.Of(new Color("b99a5a"), mood: FigureMood);
    private static readonly Ramp RockR = Ramp.Of(new Color("7a736b"), mood: FigureMood);
    private static readonly Ramp OreR = Ramp.Of(new Color("f2c440"), mood: FigureMood);

    /// <summary>
    /// A building's sheet: <see cref="ArtLayout.BuildingFrames"/> frames of <paramref name="w"/>×<paramref name="h"/>
    /// side by side (flags waving, wheels turning, fires flickering), each drawn with local coordinates and outlined.
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
            c.RimLight(new Rect2I(cell, new Vector2I(w, h)), SkyRim, 0.4f);
            c.SelOut(SilhouetteRim, new Rect2I(cell, new Vector2I(w, h)));
        }
        return c;
    }

    /// <summary>
    /// Stone masonry: staggered blocks 7 wide and 4 high with mortar joints, each block its own shade, lit along its top
    /// and shaded along its bottom; a <paramref name="round"/> wall (a tower) is lit like a cylinder, a flat one evenly,
    /// and the last rows darken where the wall meets the ground.
    /// </summary>
    private static void Masonry(PixelCanvas c, int x, int y, int w, int h, Ramp r, bool round = false, int salt = 0, float bias = 0f)
    {
        for (int j = 0; j < h; j++)
        {
            int course = j / 4, inCourse = j % 4;
            int offset = course % 2 * 4;
            for (int i = 0; i < w; i++)
            {
                int brick = (i + offset) / 7;
                bool joint = inCourse == 3 || (i + offset) % 7 == 6;
                float across = (i + 0.5f) / w;
                float wall = round ? 1.1f - 3.4f * Mathf.Abs(across - 0.3f) : (i == 0 ? 0.5f : i >= w - 2 ? -0.7f : 0f);
                float ground = Mathf.Max(0f, j - (h - 6)) * 0.18f;
                float light = joint ? -1.2f + wall * 0.4f
                    : wall + (Hash(brick + x, course + y, 11 + salt) % 7 - 3) * 0.12f + (inCourse == 0 ? 0.55f : inCourse == 2 ? -0.35f : 0f);
                c.Shade(x + i, y + j, r, light - ground + bias);
            }
        }
    }

    /// <summary>A parapet of merlons 4 wide and 5 high with gaps of 3, along the top of a wall from x to x + w.</summary>
    private static void Merlons(PixelCanvas c, int x, int y, int w, Ramp r, bool round = false)
    {
        for (int i = 0; i < w; i += 7)
        {
            int mw = Math.Min(4, w - i);
            for (int j = 0; j < 5; j++)
                for (int k = 0; k < mw; k++)
                {
                    float across = (i + k + 0.5f) / w;
                    float wall = round ? 1f - 3f * Mathf.Abs(across - 0.3f) : 0f;
                    c.Shade(x + i + k, y + j, r, (j == 0 ? 1.4f : k == mw - 1 ? -0.6f : 0.2f) + wall * 0.6f);
                }
        }
        // The wall-walk's lip below the merlons.
        for (int k = 0; k < w; k++)
            c.Shade(x + k, y + 5, r, -0.6f);
    }

    /// <summary>
    /// A conical roof in faction colours from the apex (cx, top) to the eaves at <paramref name="baseY"/>, half as wide as
    /// <paramref name="half"/>: rows of shingles, each row lit on its upper edge and shaded under the next; lit on the
    /// left. The eaves overhang a pixel and cast a shadow line.
    /// </summary>
    private static void ConeRoof(PixelCanvas c, float cx, int top, int baseY, float half, bool gilded = false)
    {
        int height = baseY - top;
        for (int y = top; y <= baseY; y++)
        {
            float w = half * (y - top + 1) / height;
            int row = (y - top) % 4;
            for (int x = (int)Mathf.Floor(cx - w); x <= (int)Mathf.Ceil(cx + w); x++)
            {
                float across = (x + 0.5f - (cx - w)) / Mathf.Max(1f, 2f * w);
                if (across < 0f || across > 1f)
                    continue;
                // Shingles: staggered tabs whose lower corners are darker.
                int tab = (int)Mathf.Floor((x + ((y - top) / 4 % 2) * 2) / 4f);
                float light = 1.2f - 3f * Mathf.Abs(across - 0.28f) + (row == 0 ? 0.6f : row == 3 ? -0.7f : 0f)
                              + (Hash(tab, (y - top) / 4, 5) % 3 - 1) * 0.15f;
                if (y == baseY)
                {
                    if (gilded)
                        c.Shade(x, y, GoldR, light);
                    else
                        c.TeamLit(x, y, -1.5f);
                }
                else
                {
                    c.TeamLit(x, y, light);
                }
            }
        }
    }

    /// <summary>A pitched roof in faction colours over a hall from x0 to x1: ridge at <paramref name="top"/>, eaves at <paramref name="baseY"/>; the gable end at the right in shade.</summary>
    private static void HallRoof(PixelCanvas c, int x0, int x1, int top, int baseY)
    {
        int height = baseY - top;
        for (int y = top; y <= baseY; y++)
        {
            int inset = (baseY - y) * 2 / 3;
            int row = (y - top) % 4;
            for (int x = x0 + inset / 2; x <= x1 - inset / 3; x++)
            {
                int tab = (x + (y - top) / 4 % 2 * 3) / 6;
                float light = (row == 0 ? 0.9f : row == 3 ? -0.8f : 0.1f) + 0.5f - (y - top) / (float)height * 0.6f
                              + (Hash(tab, (y - top) / 4, 9) % 3 - 1) * 0.15f - (x > x1 - inset / 3 - 4 ? 0.8f : 0f);
                c.TeamLit(x, y, y == baseY ? -1.6f : light);
            }
        }
    }

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
                    c.Shade(x + i, y + j, WallLightR, j == h ? 1.4f : -0.3f);
                else
                    c.Shade(x + i, y + j, GlassR, i == 0 || j == 0 ? -1f : (i + j) % 5 == 0 ? 0.6f : 0f);
            }
        if (w >= 4)
            c.Rect(x + w / 2, y + 1, 1, h - 1, WallR.Shade);
        c.Lit(x, y, w, h, WindowGlow);
    }

    /// <summary>An arched door of planks with iron bands under a stone arch.</summary>
    private static void ArchDoor(PixelCanvas c, int x, int y, int w, int h, bool gilded = false)
    {
        float cx = x + w / 2f;
        for (int j = -2; j < h; j++)
            for (int i = -2; i < w + 2; i++)
            {
                float dx = (i - (w - 1) / 2f), dy = (j - w / 2f);
                bool inArch = j >= w / 2 || dx * dx + dy * dy <= (w / 2f) * (w / 2f);
                bool inRim = j >= w / 2 - 1 || dx * dx + dy * dy <= (w / 2f + 2f) * (w / 2f + 2f);
                if (!inRim)
                    continue;
                if (!inArch || i < 0 || i >= w)
                {
                    if (j < h)
                        c.Shade(x + i, y + j, gilded ? GoldR : WallLightR, (i < 0 || dx < 0 ? 1f : -0.4f) + (j < 0 ? 0.6f : 0f));
                    continue;
                }
                bool band = j == h / 3 || j == h * 2 / 3;
                float light = band ? 0.3f : (i % 4 == 3 ? -1.2f : 0f) + (i < 2 ? -0.8f : 0f) + (j < w / 2 + 1 ? -0.6f : 0f);
                c.Shade(x + i, y + j, band ? IronR : DoorR, light);
            }
        c.Px((int)cx + 2, y + h / 2, GoldR.Light); // a ring for a handle
    }

    /// <summary>A banner in faction colours hanging from a pole, a swallowtail at its foot.</summary>
    private static void Banner(PixelCanvas c, int x, int y, int w, int h)
    {
        c.Stroke(x - 1, y, x + w + 1, y, 1.6f, DarkWoodR);
        for (int j = 1; j <= h; j++)
            for (int i = 0; i < w; i++)
            {
                if (j > h - 3 && Mathf.Abs(i - (w - 1) / 2f) < (j - (h - 3)) * 0.8f)
                    continue; // the swallowtail notch
                float light = (i == 0 ? 0.8f : i == w - 1 ? -1f : 0f) + (j == 1 ? 1f : 0f) - j * 0.06f;
                c.TeamLit(x + i, y + j, light);
            }
        // A device on it: a pale chevron.
        for (int i = 0; i < w - 2; i++)
            c.Team(x + 1 + i, y + h / 2 - Math.Abs(i - (w - 3) / 2) / 2, 1f);
    }

    /// <summary>
    /// A flag on a pole, the cloth in faction colours rippling in the wind: a wave runs out from the pole along the
    /// cloth over the four frames, the free end flapping most; folds turned from the light are darker.
    /// </summary>
    private static void Flag(PixelCanvas c, int x, int y, int frame, int length = 14)
    {
        c.Stroke(x, y, x, y + 20, 1.6f, DarkWoodR, 0.4f);
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
        c.Lit(x - 2, y - 2, 5, 6, FireGlow);
    }

    // ---- The buildings -----------------------------------------------------------------------------------------

    /// <summary>
    /// 96×120 frames, bottom-centre origin; the footprint is 3×3 tiles. A curtain wall between two round towers with
    /// conical roofs in the owner's colours, a tall keep behind flying the flag, a gate between torches.
    /// </summary>
    private static PixelCanvas Castle() => Sheet(96, 120, (c, frame) =>
    {
        // The keep at the back.
        Masonry(c, 28, 26, 40, 40, WallLightR, salt: 1);
        Merlons(c, 28, 20, 40, WallLightR);
        foreach (int x in new[] { 34, 46, 58 })
            ArchWindow(c, x, 32, 4, 8);
        Flag(c, 47, 0, frame);
        // The curtain wall.
        Masonry(c, 14, 62, 68, 54, WallR, salt: 2);
        Merlons(c, 14, 56, 68, WallR);
        ArchWindow(c, 28, 72, 3, 6);
        ArchWindow(c, 65, 72, 3, 6);
        // The towers at the corners, round, with conical roofs.
        foreach (int tx in new[] { 0, 70 })
        {
            Masonry(c, tx, 38, 26, 78, WallLightR, round: true, salt: tx);
            Merlons(c, tx, 32, 26, WallLightR, round: true);
            ConeRoof(c, tx + 12.5f, 8, 34, 12.5f);
            ArchWindow(c, tx + 10, 50, 4, 8);
            ArchWindow(c, tx + 10, 74, 3, 6);
            Banner(c, tx + 7, 86, 10, 16);
        }
        c.Rect(0, 115, 96, 1, WallR.Shade);
        // The gate under its arch, a torch on either side.
        ArchDoor(c, 38, 82, 20, 34);
        c.Rect(46, 78, 4, 3, WallLightR.Glint); // keystone
        Torch(c, 33, 86, frame);
        Torch(c, 63, 86, (frame + 2) % 4);
        Banner(c, 43, 64, 10, 12);
    });

    /// <summary>
    /// 128×160 frames, bottom-centre origin; the footprint is 4×4 tiles. The Defense mode's citadel: a curtain wall with
    /// four towers, a tall keep with a gilded crown behind, a double gate between torches, and banners and flags in the
    /// garrison's colours.
    /// </summary>
    private static PixelCanvas Citadel() => Sheet(128, 160, (c, frame) =>
    {
        // The keep at the back, rising over everything, crowned in gold.
        Masonry(c, 44, 26, 40, 64, WallLightR, salt: 3);
        Merlons(c, 44, 20, 40, WallLightR);
        for (int i = 0; i < 24; i++)
            c.Shade(52 + i, 14 + (i % 6 == 0 ? -2 : 0), GoldR, 0.8f);
        c.Rect(52, 15, 24, 4, GoldR.Base);
        c.Rect(52, 18, 24, 1, GoldR.Shade);
        foreach (int x in new[] { 52, 62, 72 })
            ArchWindow(c, x, 36, 4, 9);
        ArchWindow(c, 62, 56, 4, 8);
        Flag(c, 63, 0, frame);
        // The back towers peeping over the wall.
        foreach (int tx in new[] { 12, 96 })
        {
            Masonry(c, tx, 40, 20, 40, WallR, round: true, salt: tx);
            Merlons(c, tx, 34, 20, WallR, round: true);
            Flag(c, tx + 9, 14, (frame + tx / 12) % 4, 10);
        }
        // The curtain wall.
        Masonry(c, 12, 80, 104, 76, WallR, salt: 4);
        Merlons(c, 16, 74, 96, WallR);
        // The front towers at the corners.
        foreach (int tx in new[] { 0, 100 })
        {
            Masonry(c, tx, 64, 28, 92, WallLightR, round: true, salt: tx + 5);
            Merlons(c, tx, 58, 28, WallLightR, round: true);
            ConeRoof(c, tx + 13.5f, 30, 60, 13.5f);
            ArchWindow(c, tx + 11, 76, 4, 8);
            ArchWindow(c, tx + 11, 100, 4, 8);
            Banner(c, tx + 8, 116, 12, 22);
        }
        c.Rect(0, 152, 128, 4, WallR.Deep); // the plinth
        // The double gate under an arch.
        ArchDoor(c, 48, 112, 32, 40);
        c.Rect(63, 112, 2, 40, IronR.Shade);
        Torch(c, 42, 116, frame);
        Torch(c, 86, 116, (frame + 2) % 4);
        foreach (int x in new[] { 30, 92 })
            ArchWindow(c, x, 92, 4, 8);
        Banner(c, 57, 86, 14, 18);
    });

    /// <summary>
    /// 64×72 frames, bottom-centre origin; footprint 2×2 tiles. A long timber hall on a stone footing, a pitched roof in
    /// faction colours, a smoking chimney, a door and windows, a weapon rack by the door.
    /// </summary>
    private static PixelCanvas Barracks() => Sheet(64, 72, (c, frame) =>
    {
        Flag(c, 31, 0, (frame + 1) % 4);
        // The chimney, behind the roof.
        Masonry(c, 48, 12, 7, 16, WallR, salt: 7);
        // Timber walls: planks between posts.
        for (int y = 40; y < 66; y++)
            for (int x = 4; x < 60; x++)
            {
                bool post = x % 14 == 4 || x == 59;
                float light = post ? (x % 14 == 4 ? 0.6f : -0.3f) : (x % 3 == 0 ? -0.9f : 0f) + (y == 40 ? -1.2f : 0f) - (x > 54 ? 0.8f : 0f);
                c.Shade(x, y, post ? DarkWoodR : WoodR, light);
            }
        Masonry(c, 2, 64, 60, 6, WallR, salt: 8);
        HallRoof(c, 0, 63, 18, 42);
        ArchDoor(c, 27, 48, 10, 17);
        ArchWindow(c, 11, 47, 6, 7);
        ArchWindow(c, 47, 47, 6, 7);
        // A rack of spears by the door.
        for (int i = 0; i < 3; i++)
        {
            c.Stroke(19 + i * 2, 49, 19 + i * 2, 64, 1f, WoodR, 0.4f);
            c.Px(19 + i * 2, 48, SteelR.Light);
        }
        c.Stroke(17, 56, 25, 56, 1f, DarkWoodR);
    });

    /// <summary>
    /// 64×116 frames, bottom-centre origin; footprint 2×2 tiles. A walled plinth with a gate, a round stone keep behind
    /// it, an archers' gallery and a conical roof in faction colours. Level 2: stone gallery, iron bands, bigger banners;
    /// level 3: gilded eaves, finial and gate, a great banner on the keep.
    /// </summary>
    private static PixelCanvas Tower(int tier = 1) => Sheet(64, 116, (c, frame) =>
    {
        Flag(c, 31, 0, frame);
        // The keep, drawn first: the plinth wall stands in front of its foot. Higher levels are bound with iron.
        Masonry(c, 14, 46, 36, 42, WallLightR, round: true, salt: tier);
        ArchWindow(c, 30, 56, 4, 10);
        ArchWindow(c, 20, 62, 2, 7);
        ArchWindow(c, 42, 62, 2, 7);
        if (tier >= 2)
            foreach (int y in new[] { 52, 72 })
                for (int x = 14; x < 50; x++)
                    c.Shade(x, y, IronR, 1f - 3f * Mathf.Abs((x - 14 + 0.5f) / 36f - 0.3f));
        // Archers' gallery under the roof, with loopholes between the posts: timber, then stone.
        if (tier == 1)
        {
            for (int y = 38; y < 48; y++)
                for (int x = 6; x < 58; x++)
                {
                    bool post = x % 8 == 6;
                    c.Shade(x, y, post ? DarkWoodR : WoodR, post ? 0.4f : (y == 47 ? -1.2f : y == 38 ? 0.8f : 0f) - (x > 53 ? 0.8f : 0f));
                }
        }
        else
        {
            Masonry(c, 6, 38, 52, 10, WallR, salt: 9);
        }
        for (int x = 11; x < 56; x += 16)
            ArchWindow(c, x, 40, 3, 5);
        ConeRoof(c, 31.5f, 10, 38, 28f, gilded: tier >= 3);
        if (tier >= 3)
        {
            c.Ball(31.5f, 9f, 2.4f, 2.4f, GoldR, 0.6f); // finial
            // A great banner on the keep, edged in gold, and gilded merlon caps on the gallery.
            c.Rect(23, 49, 18, 28, GoldR.Shade);
            Banner(c, 25, 50, 14, 24);
            for (int x = 6; x < 58; x += 8)
                c.Rect(x, 36, 4, 2, GoldR.Light);
        }
        // Plinth: a low wall with merlons, banners and a gate.
        Merlons(c, 4, 78, 56, WallR);
        Masonry(c, 4, 84, 56, 30, WallR, salt: 10);
        c.Rect(4, 112, 56, 2, WallR.Deep);
        ArchDoor(c, 25, 92, 14, 20, gilded: tier >= 3);
        if (tier >= 2)
        {
            Banner(c, 7, 86, 10, 16);
            Banner(c, 47, 86, 10, 16);
        }
        else
        {
            Banner(c, 9, 88, 8, 12);
            Banner(c, 47, 88, 8, 12);
        }
    });

    /// <summary>
    /// 96×152 frames, bottom-centre origin; footprint 3×3 tiles. A walled base, a slim stone shaft and an open lookout
    /// with a beacon fire (flickering through the frames) under a pointed roof in faction colours.
    /// </summary>
    private static PixelCanvas Watchtower() => Sheet(96, 152, (c, frame) =>
    {
        Flag(c, 47, 0, frame);
        ConeRoof(c, 47.5f, 10, 36, 26f);
        // Open lookout: corner posts, a beacon in a brazier, a railing and the floor.
        c.Stroke(24, 38, 24, 62, 3f, WoodR);
        c.Stroke(71, 38, 71, 62, 3f, DarkWoodR);
        c.Ball(48, 56, 9, 4, IronR, -0.3f);
        c.Rect(40, 52, 17, 4, IronR.Shade);
        var flame = Ramp.Of(Fire);
        switch (frame)
        {
            case 0: c.Ball(48, 47, 5, 7, flame, 0.6f); c.Ball(47, 46, 2, 4, Ramp.Of(FireLight), 1f); break;
            case 1: c.Ball(46, 48, 5, 6, flame, 0.6f); c.Ball(50, 45, 2, 5, flame, 1f); c.Ball(46, 48, 2, 3, Ramp.Of(FireLight), 1f); break;
            case 2: c.Ball(49, 46, 4, 8, flame, 0.6f); c.Ball(49, 47, 2, 4, Ramp.Of(FireLight), 1f); break;
            default: c.Ball(48, 48, 6, 6, flame, 0.6f); c.Ball(45, 45, 1.5f, 3, flame, 1f); c.Ball(48, 48, 2.5f, 3, Ramp.Of(FireLight), 1f); break;
        }
        c.Lit(36, 36, 24, 22, FireGlow);
        for (int x = 18; x < 78; x++)
        {
            c.Shade(x, 60, WoodR, 0.9f);
            c.Shade(x, 61, WoodR, x % 6 == 0 ? -1f : 0f);
            c.Shade(x, 62, WoodR, x % 6 == 0 ? -1f : -0.3f);
        }
        for (int x = 16; x < 80; x++)
            for (int y = 64; y < 70; y++)
                c.Shade(x, y, DarkWoodR, y == 64 ? 0.8f : x % 5 == 0 ? -1f : -0.2f);
        // Shaft with lookout windows.
        Masonry(c, 30, 70, 36, 44, WallLightR, round: true, salt: 11);
        ArchWindow(c, 46, 78, 4, 10);
        ArchWindow(c, 46, 96, 4, 8);
        // Walled base with a gate and banners.
        Merlons(c, 6, 106, 84, WallR);
        Masonry(c, 6, 112, 84, 38, WallR, salt: 12);
        c.Rect(6, 148, 84, 2, WallR.Deep);
        ArchDoor(c, 38, 122, 20, 26);
        Banner(c, 14, 118, 10, 16);
        Banner(c, 72, 118, 10, 16);
    });

    /// <summary>
    /// 64×80 frames, bottom-centre origin; footprint 2×2 tiles. A stone platform with a timber frame, a throwing arm loaded
    /// with a stone, a counterweight and a banner in faction colours. Level 2: merlons and iron fittings;
    /// level 3: gilded trim and banners at the sides.
    /// </summary>
    private static PixelCanvas Catapult(int tier = 1) => Sheet(64, 80, (c, frame) =>
    {
        Flag(c, 6, 22, frame, 10);
        var timber = tier >= 3 ? Ramp.Of(new Color("b08250"), mood: FigureMood) : WoodR;
        // Frame: two uprights (the far one darker), a crossbar and the axle the arm swings on.
        c.Stroke(44, 54, 44, 30, 3.4f, DarkWoodR);
        c.Stroke(19, 54, 19, 30, 3.6f, timber);
        c.Stroke(16, 29, 48, 29, 3f, DarkWoodR, 0.3f);
        if (tier >= 2)
            foreach (int x in new[] { 19, 44 })
                c.Rect(x - 2, 38, 4, 2, IronR.Base);
        // Throwing arm, raised, with the counterweight low at one end and a stone in the cup at the other.
        c.Stroke(14, 48, 53, 14, 3f, timber, 0.3f);
        c.Rect(6, 42, 13, 10, tier >= 2 ? IronR.Base : WallR.Shade);
        c.Ball(12f, 46.5f, 6.5f, 5f, tier >= 2 ? IronR : WallR, 0.2f);
        c.Ball(53f, 12f, 5f, 3f, DarkWoodR, -0.2f);
        c.Ball(53f, 8f, 3.4f, 3.4f, StoneR, 0.4f);
        c.Ball(32f, 31f, 2.4f, 2.4f, tier >= 3 ? GoldR : IronR, 0.6f);
        if (tier >= 3)
            c.Rect(52, 15, 3, 2, GoldR.Light);
        // Platform.
        if (tier >= 2)
            Merlons(c, 4, 48, 56, WallR);
        Masonry(c, 4, 54, 56, 22, WallR, salt: 13);
        c.Rect(4, 76, 56, 2, WallR.Deep);
        if (tier >= 3)
            for (int x = 4; x < 60; x++)
                c.Shade(x, 54, GoldR, 0.8f);
        Banner(c, 26, 57, 12, 13);
        if (tier >= 3)
        {
            Banner(c, 8, 57, 8, 12);
            Banner(c, 48, 57, 8, 12);
        }
    });

    /// <summary>
    /// 64×72 frames, bottom-centre origin; footprint 2×2 tiles on a gold deposit. Through the frames the headframe's
    /// wheel turns and the gold in the cart glints (the view holds the first frame while the mine stands idle).
    /// Level 2: stone-framed tunnel, lantern, braced headframe, a second cart; level 3: steel headframe with a gilded
    /// wheel, a retaining wall, a heap of gold.
    /// </summary>
    private static PixelCanvas Mine(int tier = 1) => Sheet(64, 72, (c, frame) =>
    {
        var glint = new Color("fff6c0");
        // A rock mound with a tunnel mouth: timbered, then framed in stone; lamplight inside at night.
        for (int y = 42; y < 70; y++)
        {
            float half = 12f + (y - 42) * 19f / 27f;
            for (int x = (int)(32 - half); x < (int)(32 + half); x++)
            {
                float across = (x - (32 - half)) / (2f * half);
                float light = 1f - 3f * Mathf.Abs(across - 0.3f) + (Hash(x / 3, y / 3, 21) % 5 - 2) * 0.25f - (y - 42) * 0.02f;
                c.Shade(x, y, RockR, light);
            }
        }
        if (tier >= 3)
            Masonry(c, 4, 62, 56, 8, WallR, salt: 14); // retaining wall
        // The tunnel.
        for (int y = 52; y < 70; y++)
            for (int x = 23; x < 41; x++)
                c.Shade(x, y, GlassR, y < 55 ? -1.6f : -1f + (x - 23) * 0.04f);
        c.Lit(25, 58, 14, 12, new Color("b8641e"));
        var frameR = tier >= 2 ? WallLightR : WoodR;
        c.Stroke(22, 51, 22, 69, 3f, frameR);
        c.Stroke(41, 51, 41, 69, 3f, frameR, -0.6f);
        c.Stroke(20, 50, 43, 50, 3f, frameR, 0.4f);
        if (tier >= 2)
        {
            c.Stroke(16, 54, 16, 60, 1f, IronR);
            c.Ball(16, 56, 1.8f, 2.2f, Ramp.Of(new Color("ffd25a"), mood: FigureMood), 0.8f); // lantern by the entrance
            c.Lit(13, 53, 7, 7, WindowGlow);
        }
        // Headframe with a pulley wheel: timber, then steel; the top level's wheel is gilded.
        var post = tier >= 3 ? SteelR : WoodR;
        c.Stroke(11, 14, 11, 50, 3f, post);
        c.Stroke(52, 14, 52, 50, 3f, tier >= 3 ? IronR : DarkWoodR);
        c.Stroke(8, 13, 55, 13, 3f, tier >= 3 ? SteelR : DarkWoodR, 0.4f);
        c.Stroke(12, 18, 30, 32, 2f, post, -0.2f);
        c.Stroke(51, 18, 34, 32, 2f, post, -0.6f);
        if (tier >= 2)
            c.Stroke(12, 40, 51, 24, 2f, post, -0.3f); // cross brace
        var wheel = tier >= 3 ? GoldR : IronR;
        c.Ball(32, 20, 6.5f, 6.5f, wheel, 0.2f);
        c.Ball(32, 20, 4.5f, 4.5f, tier >= 3 ? GoldR : DarkWoodR, -0.5f);
        float angle = frame * Mathf.Pi / 4f;
        for (int s = 0; s < 2; s++)
        {
            float a = angle + s * Mathf.Pi / 2f;
            c.Stroke(32 - Mathf.Cos(a) * 4.5f, 20 - Mathf.Sin(a) * 4.5f, 32 + Mathf.Cos(a) * 4.5f, 20 + Mathf.Sin(a) * 4.5f, 1.2f, tier >= 3 ? GoldR : WoodR, 0.5f);
        }
        c.Ball(32, 20, 1.4f, 1.4f, SteelR, 0.8f);
        c.Stroke(32, 26, 32, 46, 1f, RopeR, -0.3f); // the rope down the shaft
        // Carts full of gold: one, then two, then a heap beside them.
        Cart(c, 44, 58, frame, glint, 0);
        if (tier >= 2)
            Cart(c, 4, 58, frame, glint, 2);
        if (tier >= 3)
        {
            c.Ball(32f, 46f, 8f, 4f, GoldR, 0.4f);
            c.Ball(30f, 44f, 4f, 2.5f, GoldR, 1f);
            if (frame == 1)
                c.Px(29, 42, glint);
        }
        Flag(c, 6, 0, frame, 11);
    });

    /// <summary>A mine cart heaped with gold at (x, y), its load glinting on the frame given by <paramref name="shine"/>.</summary>
    private static void Cart(PixelCanvas c, int x, int y, int frame, Color glint, int shine)
    {
        c.Block(x, y + 2, 16, 7, DarkWoodR, top: 1);
        c.Stroke(x, y + 5, x + 15, y + 5, 1f, IronR);
        c.Ball(x + 8f, y + 1.5f, 7f, 3.5f, GoldR, 0.6f);
        c.Ball(x + 4f, y + 11f, 2.2f, 2.2f, IronR, 0f);
        c.Ball(x + 12f, y + 11f, 2.2f, 2.2f, IronR, 0f);
        if (frame == shine || frame == shine + 1)
            c.Px(x + 4 + frame * 3, y - 1, glint);
    }

    /// <summary>64×64 ore patch drawn on the 2×2 deposit footprint (top-left origin): boulders veined with gold, nuggets about.</summary>
    private static PixelCanvas Deposit()
    {
        var c = new PixelCanvas(64, 64);
        var boulders = new (float X, float Y, float W, float H)[]
        {
            (16, 18, 10, 8), (40, 15, 11, 9), (21, 40, 12, 10), (46, 38, 9, 9), (32, 54, 7, 5), (8, 30, 5, 4), (56, 54, 4, 3),
        };
        foreach (var (x, y, w, h) in boulders)
        {
            c.GroundShadow(x + 2, y + h * 0.7f, w * 1.05f, h * 0.45f, 0.3f);
            c.Ball(x, y, w, h, RockR, 0.1f, 0.25f);
            // Gold veins running across the face.
            for (int v = 0; v < 2; v++)
            {
                float vx = x - w * 0.6f + Hash((int)x, v, 31) % (int)w * 0.6f, vy = y - h * 0.3f + v * h * 0.4f;
                for (int i = 0; i < (int)w; i++)
                {
                    int px = (int)(vx + i), py = (int)(vy + Mathf.Sin(i * 0.8f + v) * 1.5f);
                    if (c.IsSolid(px, py) && c.Get(px, py) != Colors.Transparent)
                        c.Shade(px, py, OreR, (i % 3 == 0 ? 1f : 0f) - (px - x) * 0.05f);
                }
            }
        }
        // Loose nuggets.
        foreach (var (x, y) in new[] { (30, 30), (34, 33), (12, 48), (52, 26), (40, 50) })
            c.Ball(x, y, 1.6f, 1.3f, OreR, 0.8f);
        c.SelOut(SilhouetteRim);
        return c;
    }
}

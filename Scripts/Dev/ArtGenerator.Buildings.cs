using System;
using Godot;
using Lordsio.World;

namespace Lordsio.Dev;

/// <summary>The buildings, every level, with faction-colour masks and night lights; and the gold ore.</summary>
public static partial class ArtGenerator
{
    /// <summary>Warm light of a lit window at night.</summary>
    private static readonly Color WindowGlow = new("ffc24a");
    /// <summary>Light of an open fire (beacon, torch) at night.</summary>
    private static readonly Color FireGlow = new("ff8a2a");
    private static readonly Color Fire = new("ff7a1a");
    private static readonly Color FireLight = new("ffd25a");

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
            draw(c, frame);
            c.Origin = Vector2I.Zero;
            c.Outline(Outline, new Rect2I(cell, new Vector2I(w, h)));
        }
        return c;
    }

    /// <summary>Stone wall texture: staggered bricks with mortar lines and a lit top edge.</summary>
    private static void Bricks(PixelCanvas c, int x, int y, int w, int h, Color mid)
    {
        c.Rect(x, y, w, h, mid);
        for (int j = 0; j < h; j++)
        {
            for (int i = 0; i < w; i++)
            {
                bool mortarRow = j % 4 == 3;
                bool mortarCol = (i + (j / 4 % 2) * 3) % 6 == 5;
                if (mortarRow || mortarCol)
                    c.Px(x + i, y + j, Mortar);
                else if (Hash(x + i, y + j, 11) < 60)
                    c.Px(x + i, y + j, StoneLight);
            }
        }
        c.Rect(x, y, w, 1, StoneLight);
    }

    private static void Crenellations(PixelCanvas c, int x, int y, int w, Color color)
    {
        for (int i = 0; i < w; i += 4)
            c.Rect(x + i, y, Math.Min(2, w - i), 3, color);
    }

    private static void Banner(PixelCanvas c, int x, int y, int w, int h)
    {
        c.TeamRect(x, y, w, h);
        // Swallowtail bottom.
        for (int i = 0; i < w; i++)
            if (i != w / 2 && i != w / 2 - 1)
                c.Team(x + i, y + h, 0.62f);
    }

    /// <summary>A dark window that shines at night.</summary>
    private static void LitWindow(PixelCanvas c, int x, int y, int w, int h)
    {
        c.Rect(x, y, w, h, Window);
        c.Lit(x, y, w, h, WindowGlow);
    }

    /// <summary>
    /// A flag on a pole, the cloth in faction colours rippling in the wind: a wave runs out from the pole along the
    /// cloth over the four frames, the free end flapping most; slopes turned away from the light are darker.
    /// </summary>
    private static void Flag(PixelCanvas c, int x, int y, int frame)
    {
        c.Rect(x, y, 1, 9, new Color("4a3a2a"));
        int previous = 0;
        for (int i = 0; i <= 6; i++)
        {
            float amplitude = Mathf.Min(1f, i / 2.5f);
            int dy = (int)Mathf.Round((Mathf.Sin(i * 1.2f - frame * Mathf.Pi / 2f) + 1f) / 2f * amplitude);
            float turn = dy > previous ? 0.78f : 1f;
            previous = dy;
            if (i == 6)
            {
                c.Team(x + 1 + i, y + 1 + dy, 0.82f * turn); // the frayed tip
                break;
            }
            for (int j = 0; j < 4; j++)
                c.Team(x + 1 + i, y + dy + j, (j == 0 ? 1f : j == 3 ? 0.62f : 0.82f) * turn);
        }
    }

    /// <summary>A torch on a wall bracket whose flame flickers through the frames.</summary>
    private static void Torch(PixelCanvas c, int x, int y, int frame)
    {
        c.Rect(x, y + 2, 1, 3, WoodDark);
        c.Px(x, y + 1, Fire);
        c.Px(x + (frame % 2 == 0 ? 0 : frame == 1 ? -1 : 1), y, FireLight);
        c.Lit(x - 1, y - 1, 3, 3, FireGlow);
    }

    /// <summary>48×60 frames, bottom-centre origin; the footprint is 3×3 tiles.</summary>
    private static PixelCanvas Castle() => Sheet(48, 60, (c, frame) =>
    {
        Bricks(c, 4, 28, 40, 30, Stone);
        Bricks(c, 14, 10, 20, 20, StoneLight);
        Crenellations(c, 14, 7, 20, StoneLight);
        Bricks(c, 0, 18, 12, 40, StoneLight);
        Bricks(c, 36, 18, 12, 40, StoneLight);
        Crenellations(c, 0, 15, 12, StoneLight);
        Crenellations(c, 36, 15, 12, StoneLight);
        Crenellations(c, 12, 25, 24, Stone);
        // Gate with an arch and planks.
        c.Rect(19, 44, 10, 14, Door);
        c.Rect(20, 42, 8, 2, Door);
        for (int x = 21; x < 28; x += 2)
            c.Rect(x, 45, 1, 13, WoodDark);
        c.Rect(18, 41, 12, 1, StoneDark);
        Torch(c, 16, 43, frame);
        Torch(c, 31, 43, (frame + 2) % 4);
        foreach (int x in new[] { 18, 23, 28 })
            LitWindow(c, x, 15, 2, 3);
        LitWindow(c, 5, 30, 2, 3);
        LitWindow(c, 41, 30, 2, 3);
        LitWindow(c, 5, 22, 2, 2);
        LitWindow(c, 41, 22, 2, 2);
        Banner(c, 3, 34, 6, 10);
        Banner(c, 39, 34, 6, 10);
        Flag(c, 23, 0, frame);
    });

    /// <summary>32×36 frames, bottom-centre origin; footprint 2×2 tiles. The roof is in faction colours.</summary>
    private static PixelCanvas Barracks() => Sheet(32, 36, (c, frame) =>
    {
        Flag(c, 15, 0, (frame + 1) % 4);
        c.Rect(2, 20, 28, 15, Wood);
        for (int x = 2; x < 30; x += 4)
            c.Rect(x, 20, 1, 15, WoodDark);
        c.Rect(2, 33, 28, 2, StoneDark);
        c.Rect(13, 26, 6, 8, Door);
        c.Rect(15, 26, 1, 8, WoodDark);
        LitWindow(c, 5, 24, 3, 3);
        LitWindow(c, 24, 24, 3, 3);
        c.Rect(24, 7, 3, 6, StoneDark); // chimney
        for (int row = 0; row < 12; row++)
        {
            int inset = 11 - row;
            for (int x = inset; x < 32 - inset; x++)
                c.Team(x, 8 + row, row == 0 ? 1f : row == 11 ? 0.55f : (x - inset) % 4 == 0 ? 0.7f : 0.85f);
        }
    });

    /// <summary>
    /// 32×58 frames, bottom-centre origin; footprint 2×2 tiles. A walled plinth with a gate, a stone keep behind it,
    /// an archers' gallery and a conical roof in faction colours. Level 2: stone gallery, iron bands, bigger banners;
    /// level 3: gilded eaves, finial and gate.
    /// </summary>
    private static PixelCanvas Tower(int tier = 1) => Sheet(32, 58, (c, frame) =>
    {
        Flag(c, 15, 0, frame);
        // Keep, drawn first: the plinth wall stands in front of its foot. Higher levels are bound with iron.
        Bricks(c, 7, 24, 18, 20, StoneLight);
        LitWindow(c, 15, 28, 2, 5); // arrow slits
        LitWindow(c, 10, 31, 1, 4);
        LitWindow(c, 21, 31, 1, 4);
        if (tier >= 2)
        {
            c.Rect(7, 27, 18, 1, SteelDark);
            c.Rect(7, 36, 18, 1, SteelDark);
        }
        // Archers' gallery under the roof, with loopholes between the posts: timber, then stone.
        if (tier == 1)
        {
            c.Rect(3, 19, 26, 5, Wood);
            c.Rect(3, 23, 26, 1, WoodDark);
            for (int x = 4; x < 29; x += 4)
                c.Rect(x, 19, 1, 4, WoodDark);
        }
        else
        {
            Bricks(c, 3, 19, 26, 5, Stone);
            c.Rect(3, 23, 26, 1, StoneDark);
        }
        for (int x = 6; x < 27; x += 8)
            LitWindow(c, x, 20, 2, 2);
        for (int row = 0; row < 13; row++)
        {
            int half = 2 + row * 12 / 12;
            for (int x = 16 - half; x < 16 + half; x++)
            {
                if (tier >= 3 && row == 12)
                    c.Px(x, 6 + row, x % 2 == 0 ? Gold : GoldDark); // gilded eaves
                else
                    c.Team(x, 6 + row, row == 12 ? 0.55f : x < 16 ? 0.95f : 0.65f);
            }
        }
        if (tier >= 3)
        {
            c.Rect(15, 5, 2, 2, Gold); // finial
            // A great banner on the keep, edged in gold, and gilded merlons on the gallery.
            c.Rect(12, 25, 8, 13, GoldDark);
            Banner(c, 13, 26, 6, 10);
            for (int x = 3; x < 29; x += 4)
                c.Rect(x, 18, 2, 1, Gold);
        }
        // Plinth: a low wall with corner merlons, banners and a gate.
        Crenellations(c, 2, 39, 28, Stone);
        Bricks(c, 2, 42, 28, 15, Stone);
        c.Rect(2, 55, 28, 2, StoneDark);
        c.Rect(13, 47, 6, 9, Door);
        c.Rect(14, 46, 4, 1, Door);
        c.Rect(15, 48, 1, 8, WoodDark);
        c.Rect(12, 45, 8, 1, tier >= 3 ? Gold : StoneDark);
        if (tier >= 2)
        {
            c.Rect(13, 50, 6, 1, SteelDark); // iron-bound gate
            Banner(c, 3, 43, 5, 8);
            Banner(c, 24, 43, 5, 8);
        }
        else
        {
            Banner(c, 4, 44, 4, 6);
            Banner(c, 24, 44, 4, 6);
        }
    });

    /// <summary>
    /// 48×76 frames, bottom-centre origin; footprint 3×3 tiles. A walled base, a slim stone shaft and an open lookout
    /// with a beacon fire (flickering through the frames) under a pointed roof in faction colours.
    /// </summary>
    private static PixelCanvas Watchtower() => Sheet(48, 76, (c, frame) =>
    {
        Flag(c, 23, 0, frame);
        for (int row = 0; row < 12; row++)
        {
            int half = 2 + row;
            for (int x = 24 - half; x < 24 + half; x++)
                c.Team(x, 6 + row, row == 11 ? 0.55f : x < 24 ? 0.95f : 0.65f);
        }
        // Open lookout: corner posts, a beacon in a brazier, a railing and the floor.
        c.Rect(11, 18, 26, 2, WoodDark);
        c.Rect(12, 20, 2, 11, Wood);
        c.Rect(34, 20, 2, 11, Wood);
        c.Rect(20, 27, 8, 3, SteelDark);
        c.Rect(21, 24, 6, 3, Fire);
        switch (frame)
        {
            case 0: c.Rect(23, 21, 2, 3, FireLight); c.Px(22, 23, Fire); break;
            case 1: c.Rect(22, 22, 2, 2, FireLight); c.Rect(25, 21, 1, 3, Fire); c.Px(24, 20, FireLight); break;
            case 2: c.Rect(24, 20, 1, 4, FireLight); c.Rect(22, 22, 1, 2, Fire); c.Px(25, 22, FireLight); break;
            default: c.Rect(23, 22, 3, 2, FireLight); c.Px(22, 21, Fire); c.Px(25, 21, Fire); break;
        }
        c.Lit(19, 19, 10, 8, FireGlow);
        c.Rect(9, 30, 30, 3, Wood);
        for (int x = 10; x < 38; x += 3)
            c.Rect(x, 31, 1, 2, WoodDark);
        c.Rect(8, 33, 32, 2, WoodDark);
        // Shaft with lookout windows.
        Bricks(c, 15, 35, 18, 22, StoneLight);
        LitWindow(c, 23, 39, 2, 5);
        LitWindow(c, 23, 48, 2, 4);
        // Walled base with a gate and banners.
        Crenellations(c, 3, 53, 42, Stone);
        Bricks(c, 3, 56, 42, 19, Stone);
        c.Rect(3, 73, 42, 2, StoneDark);
        c.Rect(20, 63, 8, 11, Door);
        c.Rect(21, 61, 6, 2, Door);
        for (int x = 22; x < 27; x += 2)
            c.Rect(x, 64, 1, 10, WoodDark);
        c.Rect(19, 60, 10, 1, StoneDark);
        Banner(c, 7, 59, 5, 8);
        Banner(c, 36, 59, 5, 8);
    });

    /// <summary>
    /// 32×40 frames, bottom-centre origin; footprint 2×2 tiles. A stone platform with a timber frame, a throwing arm loaded
    /// with a stone, a counterweight and a banner in faction colours. Level 2: merlons and iron fittings;
    /// level 3: gilded trim and banners at the sides.
    /// </summary>
    private static PixelCanvas Catapult(int tier = 1) => Sheet(32, 40, (c, frame) =>
    {
        Flag(c, 3, 12, frame);
        // Frame: two uprights, a crossbar and the axle the arm swings on.
        var timber = tier >= 3 ? WoodLight : Wood;
        c.Rect(9, 15, 2, 12, timber);
        c.Rect(21, 15, 2, 12, timber);
        c.Rect(8, 14, 16, 2, WoodDark);
        if (tier >= 2)
        {
            c.Rect(9, 19, 2, 1, SteelDark);
            c.Rect(21, 19, 2, 1, SteelDark);
        }
        // Throwing arm, raised, with the counterweight low at one end and a stone in the cup at the other.
        c.Line(7, 24, 26, 7, WoodDark);
        c.Line(8, 24, 27, 7, timber);
        c.Rect(3, 21, 6, 5, tier >= 2 ? SteelDark : StoneDark);
        c.Rect(24, 4, 5, 3, WoodDark);
        c.Disc(26, 3, 1.6f, RockLight);
        c.Disc(16, 16, 1.5f, tier >= 3 ? GoldDark : SteelDark);
        if (tier >= 3)
            c.Px(27, 7, Gold);
        // Platform.
        if (tier >= 2)
            Crenellations(c, 2, 24, 28, Stone);
        Bricks(c, 2, 27, 28, 11, Stone);
        c.Rect(2, 37, 28, 2, StoneDark);
        if (tier >= 3)
            c.Rect(2, 27, 28, 1, Gold);
        Banner(c, 13, 29, 6, 6);
        if (tier >= 3)
        {
            Banner(c, 4, 29, 4, 6);
            Banner(c, 24, 29, 4, 6);
        }
    });

    /// <summary>
    /// 32×36 frames, bottom-centre origin; footprint 2×2 tiles on a gold deposit. Through the frames the headframe's
    /// wheel turns and the gold in the cart glints (the view holds the first frame while the mine stands idle).
    /// Level 2: stone-framed tunnel, lantern, braced headframe, a second cart; level 3: steel headframe with a gilded
    /// wheel, a retaining wall, a heap of gold.
    /// </summary>
    private static PixelCanvas Mine(int tier = 1) => Sheet(32, 36, (c, frame) =>
    {
        var lamp = new Color("ffd25a");
        var glint = new Color("fff6c0");
        // Rock mound with a tunnel mouth: timbered, then framed in stone; lamplight inside at night.
        for (int y = 22; y < 35; y++)
        {
            int half = 6 + (y - 22) * 9 / 12;
            for (int x = 16 - half; x < 16 + half; x++)
                c.Px(x, y, Hash(x, y, 21) < 150 ? RockLight : Rock);
        }
        if (tier >= 3)
            Bricks(c, 2, 31, 28, 4, Stone); // retaining wall
        c.Rect(11, 26, 9, 9, Door);
        c.Lit(12, 29, 7, 6, new Color("b8641e"));
        var frameColor = tier >= 2 ? StoneLight : Wood;
        c.Rect(10, 25, 1, 10, frameColor);
        c.Rect(20, 25, 1, 10, frameColor);
        c.Rect(10, 25, 11, 1, tier >= 2 ? Stone : WoodLight);
        if (tier >= 2)
        {
            c.Rect(9, 24, 13, 1, StoneDark);
            c.Px(8, 27, lamp); // lantern by the entrance
            c.Px(8, 28, SteelDark);
            c.Lit(7, 26, 3, 3, WindowGlow);
        }
        // Headframe with a pulley wheel: timber, then steel; the top level's wheel is gilded.
        var post = tier >= 3 ? SteelDark : Wood;
        c.Rect(5, 7, 2, 18, post);
        c.Rect(25, 7, 2, 18, post);
        c.Rect(4, 6, 24, 2, tier >= 3 ? Steel : WoodDark);
        c.Line(6, 9, 15, 16, post);
        c.Line(26, 9, 17, 16, post);
        if (tier >= 2)
            c.Line(6, 20, 26, 12, post); // cross brace
        c.Disc(16, 10, 3f, tier >= 3 ? GoldDark : StoneDark);
        c.Px(16, 10, tier >= 3 ? Gold : Steel);
        var spoke = frame switch { 0 => new Vector2I(0, -2), 1 => new Vector2I(2, 0), 2 => new Vector2I(0, 2), _ => new Vector2I(-2, 0) };
        c.Px(16 + spoke.X, 10 + spoke.Y, tier >= 3 ? Gold : WoodLight);
        c.Px(16 - spoke.X, 10 - spoke.Y, tier >= 3 ? Gold : WoodLight);
        c.Line(16, 13, 16, 22, Leather); // the rope down the shaft
        // Carts full of gold: one, then two, then a heap beside them.
        c.Rect(22, 29, 8, 4, WoodDark);
        c.Rect(23, 27, 6, 2, Gold);
        c.Px(25, 26, GoldDark);
        c.Rect(23, 33, 2, 2, Outline);
        c.Rect(27, 33, 2, 2, Outline);
        if (frame < 3)
            c.Px(23 + frame * 2, 27, glint);
        if (tier >= 2)
        {
            c.Rect(2, 29, 7, 4, WoodDark);
            c.Rect(3, 27, 5, 2, Gold);
            c.Rect(3, 33, 2, 2, Outline);
            c.Rect(6, 33, 2, 2, Outline);
            if (frame == 2)
                c.Px(5, 27, glint);
        }
        if (tier >= 3)
        {
            for (int row = 0; row < 3; row++)
                c.Rect(12 + row, 22 - row, 7 - 2 * row, 1, row == 2 ? Gold : GoldDark);
            if (frame == 1)
                c.Px(15, 20, glint);
        }
        Flag(c, 3, 0, frame);
    });

    /// <summary>32×32 ore patch drawn on the 2×2 deposit footprint (top-left origin).</summary>
    private static PixelCanvas Deposit()
    {
        var c = new PixelCanvas(32, 32);
        var boulders = new (int X, int Y, int W, int H)[] { (3, 6, 9, 7), (15, 3, 10, 8), (6, 17, 11, 9), (20, 15, 8, 8), (13, 25, 6, 4) };
        foreach (var (x, y, w, h) in boulders)
        {
            c.Shaded(x, y, w, h, Rock, RockLight, RockDark);
            // Gold veins.
            for (int i = 0; i < w * h / 6; i++)
            {
                int px = x + 1 + Hash(i, x, 31) % Math.Max(1, w - 2), py = y + 1 + Hash(i, y, 37) % Math.Max(1, h - 2);
                c.Px(px, py, Hash(px, py, 41) < 300 ? GoldDark : Gold);
            }
        }
        c.Outline(Outline);
        return c;
    }
}

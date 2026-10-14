using System;
using Godot;

namespace Lordsio.Dev;

/// <summary>
/// Generates the game's pixel art into Assets/ (run with --export-art). The PNGs are committed and
/// loaded like any hand-drawn asset, so an artist can replace them file by file. Layout contracts
/// (cell sizes, rows, frames) are documented on each method and mirrored in <see cref="ArtLayout"/>.
/// </summary>
public static class ArtGenerator
{
    // Palette.
    private static readonly Color Outline = new("1c1a17");
    private static readonly Color Grass = new("5a9a3c");
    private static readonly Color GrassDark = new("4b8634");
    private static readonly Color GrassLight = new("6db04a");
    private static readonly Color Leaf = new("2e6a2c");
    private static readonly Color LeafLight = new("3f8836");
    private static readonly Color LeafHighlight = new("5aa04a");
    private static readonly Color LeafDark = new("21501f");
    private static readonly Color TreeOutline = new("163a15");
    private static readonly Color Bark = new("5b3a1f");
    private static readonly Color Dirt = new("a8864f");
    private static readonly Color DirtDark = new("8d6d3c");
    private static readonly Color DirtLight = new("bf9d64");
    private static readonly Color Water = new("2f6db0");
    private static readonly Color WaterLight = new("4f8fd2");
    private static readonly Color WaterDark = new("285d98");
    private static readonly Color Sand = new("d8c48a");
    private static readonly Color SandDark = new("b9a46c");
    private static readonly Color Foam = new("dceeff");
    private static readonly Color Rock = new("6e6862");
    private static readonly Color RockLight = new("9d968e");
    private static readonly Color RockDark = new("4a4642");
    private static readonly Color Snow = new("eef2f4");
    private static readonly Color Stone = new("8c8781");
    private static readonly Color StoneLight = new("aaa59e");
    private static readonly Color StoneDark = new("6b6762");
    private static readonly Color Mortar = new("75716b");
    private static readonly Color Wood = new("8a6239");
    private static readonly Color WoodDark = new("5e4026");
    private static readonly Color WoodLight = new("a87a4a");
    private static readonly Color Door = new("3e2716");
    private static readonly Color Window = new("2a2420");
    private static readonly Color Skin = new("e8b98a");
    private static readonly Color SkinDark = new("c99467");
    private static readonly Color Steel = new("b8bcc4");
    private static readonly Color SteelLight = new("dfe2e7");
    private static readonly Color SteelDark = new("7d828c");
    private static readonly Color Boots = new("3b2f2a");
    private static readonly Color Hood = new("4d6b34");
    private static readonly Color HoodDark = new("3a5227");
    private static readonly Color Leather = new("7a5530");
    private static readonly Color Gold = new("ffd44a");
    private static readonly Color GoldDark = new("c79a22");
    private static readonly Color String = new("e8e2d0");

    public static void ExportAll(string projectDir, string previewDir)
    {
        string assets = System.IO.Path.Combine(projectDir, "Assets");
        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(assets, "Tilesets"));
        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(assets, "Sprites"));

        var terrain = Terrain();
        terrain.Save(System.IO.Path.Combine(assets, "Tilesets", "terrain.png"));

        var units = Units();
        units.Save(System.IO.Path.Combine(assets, "Sprites", "units.png"), System.IO.Path.Combine(assets, "Sprites", "units_team.png"));

        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(assets, "UI"));
        AppIcon(Castle(), System.IO.Path.Combine(assets, "UI"));

        foreach (var (name, canvas) in new (string, PixelCanvas)[]
                 {
                     ("castle", Castle()), ("barracks", Barracks()), ("tower", Tower()), ("tower_t2", Tower(2)), ("tower_t3", Tower(3)), ("watchtower", Watchtower()),
                     ("mine", Mine()), ("mine_t2", Mine(2)), ("mine_t3", Mine(3)),
                     ("catapult", Catapult()), ("catapult_t2", Catapult(2)), ("catapult_t3", Catapult(3)),
                 })
        {
            canvas.Save(System.IO.Path.Combine(assets, "Sprites", name + ".png"),
                System.IO.Path.Combine(assets, "Sprites", name + "_team.png"));
            SavePreview(canvas, System.IO.Path.Combine(previewDir, name + ".png"));
        }
        var gold = Deposit();
        gold.Save(System.IO.Path.Combine(assets, "Sprites", "gold.png"));

        SavePreview(terrain, System.IO.Path.Combine(previewDir, "terrain.png"));
        SavePreview(units, System.IO.Path.Combine(previewDir, "units.png"));
        SavePreview(gold, System.IO.Path.Combine(previewDir, "gold.png"));
    }

    /// <summary>
    /// The application icon (a blue-bannered castle on a framed dark plate): icon.png (256 px) for the window
    /// and project, icon.ico (16–256 px) for the Windows executable, and splash.png for the boot screen.
    /// </summary>
    private static void AppIcon(PixelCanvas castle, string uiDir)
    {
        const int size = 80;
        var plate = new Color("2b2219");
        var plateLight = new Color("3a2e22");
        var gold = new Color("c9a050");
        var goldDark = new Color("7a5a2a");
        var team = new Color("3c6fd8");
        var icon = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // Rounded square plate with a two-tone gold frame.
                int dx = Math.Max(Math.Max(10 - x, x - (size - 11)), 0), dy = Math.Max(Math.Max(10 - y, y - (size - 11)), 0);
                float corner = MathF.Sqrt(dx * dx + dy * dy);
                if (corner > 8f || x < 2 || y < 2 || x > size - 3 || y > size - 3)
                    continue;
                bool edge = corner > 6f || x < 4 || y < 4 || x > size - 5 || y > size - 5;
                bool inner = !edge && (corner > 5f || x < 5 || y < 5 || x > size - 6 || y > size - 6);
                icon.SetPixel(x, y, edge ? gold : inner ? goldDark : y < size / 2 ? plateLight : plate);
            }
        }
        int ox = (size - castle.Width) / 2, oy = size - castle.Height - 8;
        for (int y = 0; y < castle.Height; y++)
        {
            for (int x = 0; x < castle.Width; x++)
            {
                var m = castle.Mask.GetPixel(x, y);
                var c = m.A > 0 ? new Color(team.R * m.R, team.G * m.G, team.B * m.B) : castle.Base.GetPixel(x, y);
                if (c.A > 0)
                    icon.SetPixel(ox + x, oy + y, c);
            }
        }

        Image Scaled(int pixels)
        {
            // Whole-pixel enlargement, centred on a transparent square of the requested size.
            var image = (Image)icon.Duplicate();
            int factor = Math.Max(1, pixels / size);
            if (pixels >= size)
                image.Resize(size * factor, size * factor, Image.Interpolation.Nearest);
            else
                image.Resize(pixels, pixels, Image.Interpolation.Cubic);
            if (image.GetWidth() == pixels)
                return image;
            var square = Image.CreateEmpty(pixels, pixels, false, Image.Format.Rgba8);
            int offset = (pixels - image.GetWidth()) / 2;
            square.BlitRect(image, new Rect2I(0, 0, image.GetWidth(), image.GetHeight()), new Vector2I(offset, offset));
            return square;
        }

        Scaled(256).SavePng(System.IO.Path.Combine(uiDir, "icon.png"));
        Scaled(400).SavePng(System.IO.Path.Combine(uiDir, "splash.png"));

        // .ico with PNG-compressed entries (supported since Windows Vista).
        int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
        var images = Array.ConvertAll(sizes, s => Scaled(s).SavePngToBuffer());
        using var file = System.IO.File.Create(System.IO.Path.Combine(uiDir, "icon.ico"));
        using var w = new System.IO.BinaryWriter(file);
        w.Write((ushort)0);
        w.Write((ushort)1);
        w.Write((ushort)sizes.Length);
        int dataOffset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write((ushort)1);
            w.Write((ushort)32);
            w.Write(images[i].Length);
            w.Write(dataOffset);
            dataOffset += images[i].Length;
        }
        foreach (var data in images)
            w.Write(data);
    }

    /// <summary>4× enlarged image with the team mask tinted blue, for reviewing the art.</summary>
    private static void SavePreview(PixelCanvas canvas, string path)
    {
        var image = Image.CreateEmpty(canvas.Width, canvas.Height, false, Image.Format.Rgba8);
        var team = new Color("3c6fd8");
        for (int y = 0; y < canvas.Height; y++)
        {
            for (int x = 0; x < canvas.Width; x++)
            {
                var m = canvas.Mask.GetPixel(x, y);
                image.SetPixel(x, y, m.A > 0 ? new Color(team.R * m.R, team.G * m.G, team.B * m.B) : canvas.Base.GetPixel(x, y));
            }
        }
        image.Resize(canvas.Width * 4, canvas.Height * 4, Image.Interpolation.Nearest);
        image.SavePng(path);
    }

    private static int Hash(int x, int y, int salt) =>
        (int)((uint)(x * 73856093 ^ y * 19349663 ^ salt * 83492791) % 1000);

    // ------------------------------------------------------------------ terrain

    /// <summary>
    /// 16×16 tiles, 16 columns. Row 0 grass, 1 forest, 2 water, 3 mountain (4 variants each);
    /// row 4 roads and row 5 shore overlays, both indexed by a 4-bit neighbour mask N=1, E=2, S=4, W=8;
    /// row 6: bridge running east-west, bridge running north-south.
    /// </summary>
    private static PixelCanvas Terrain()
    {
        var c = new PixelCanvas(ArtLayout.Tile * 16, ArtLayout.Tile * 7);
        for (int v = 0; v < ArtLayout.TerrainVariants; v++)
        {
            Cell(c, v, 0); GrassTile(c, v);
            Cell(c, v, 1); GrassTile(c, v + 10); TreeTile(c, v);
            Cell(c, v, 2); WaterTile(c, v);
            Cell(c, v, 3); MountainTile(c, v);
        }
        for (int mask = 0; mask < 16; mask++)
        {
            Cell(c, mask, 4); GrassTile(c, mask + 20); RoadTile(c, mask);
            Cell(c, mask, 5); ShoreTile(c, mask);
        }
        Cell(c, 0, 6); WaterTile(c, 1); BridgeTile(c, horizontal: true);
        Cell(c, 1, 6); WaterTile(c, 2); BridgeTile(c, horizontal: false);
        return c;

        static void Cell(PixelCanvas canvas, int col, int row) =>
            canvas.Origin = new Vector2I(col * ArtLayout.Tile, row * ArtLayout.Tile);
    }

    private static void GrassTile(PixelCanvas c, int v)
    {
        c.Ground(0, 0, 16, 16, Grass);
        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                int h = Hash(x, y, v);
                if (h < 70) c.Px(x, y, GrassDark, solid: false);
                else if (h < 115) c.Px(x, y, GrassLight, solid: false);
            }
        }
        // A couple of grass tufts: a light pixel over a dark one.
        for (int t = 0; t < 3; t++)
        {
            int x = Hash(t, v, 7) % 14 + 1, y = Hash(v, t, 9) % 13 + 2;
            c.Px(x, y, GrassLight, solid: false);
            c.Px(x - 1, y + 1, GrassDark, solid: false);
            c.Px(x + 1, y + 1, GrassDark, solid: false);
        }
        if (v == 3)
        {
            c.Px(4, 11, Snow, solid: false);
            c.Px(11, 4, Gold, solid: false);
        }
    }

    private static void TreeTile(PixelCanvas c, int v)
    {
        int cx = 8 + (v % 2 == 0 ? -1 : 1), cy = 6 + v % 2;
        float r = v == 2 ? 4.5f : 5.5f;
        c.Rect(cx - 1, cy + 4, 2, 4, Bark);
        c.Disc(cx, cy, r, Leaf);
        // Light from the top-left: lighter crescent there, shadow at the bottom-right.
        for (int y = -6; y <= 6; y++)
        {
            for (int x = -6; x <= 6; x++)
            {
                if (x * x + y * y > r * r)
                    continue;
                if (x + y < -4) c.Px(cx + x, cy + y, LeafLight);
                else if (x + y > 4) c.Px(cx + x, cy + y, LeafDark);
            }
        }
        c.Px(cx - 2, cy - 3, LeafHighlight);
        c.Px(cx - 3, cy - 1, LeafHighlight);
        c.Outline(TreeOutline, new Rect2I(c.Origin, new Vector2I(16, 16)));
    }

    private static void WaterTile(PixelCanvas c, int v)
    {
        c.Ground(0, 0, 16, 16, Water);
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
                if (Hash(x, y, v + 40) < 60)
                    c.Px(x, y, WaterDark, solid: false);
        for (int w = 0; w < 3; w++)
        {
            int x = (Hash(w, v, 3) % 11) + 1, y = (Hash(v, w, 5) % 13) + 1;
            c.Ground(x, y, 4, 1, WaterLight);
            c.Px(x + 1, y - 1, WaterLight, solid: false);
        }
    }

    private static void MountainTile(PixelCanvas c, int v)
    {
        c.Ground(0, 0, 16, 16, RockDark);
        int peak = 6 + v % 3, top = 1 + v % 2;
        for (int y = top; y < 16; y++)
        {
            int half = (y - top) * 8 / (15 - top);
            for (int x = peak - half; x <= peak + half; x++)
            {
                if (x < 0 || x > 15)
                    continue;
                var color = y < top + 3 ? Snow : x < peak ? RockLight : Rock;
                c.Px(x, y, color);
            }
        }
        c.Outline(Outline, new Rect2I(c.Origin, new Vector2I(16, 16)));
    }

    private static void RoadTile(PixelCanvas c, int mask)
    {
        // Dirt band 8 px wide through the centre, extended towards connected neighbours.
        int from = 4, to = 12;
        void Dirt(int x0, int y0, int x1, int y1)
        {
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                    c.Px(x, y, ArtGenerator.Dirt, solid: false);
        }
        Dirt(from, from, to, to);
        if ((mask & 1) != 0) Dirt(from, 0, to, from);
        if ((mask & 2) != 0) Dirt(to, from, 16, to);
        if ((mask & 4) != 0) Dirt(from, to, to, 16);
        if ((mask & 8) != 0) Dirt(0, from, from, to);

        // Darker rim where dirt meets grass, pebbles inside.
        var dirt = new bool[16, 16];
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
                dirt[x, y] = c.Base.GetPixel(c.Origin.X + x, c.Origin.Y + y) == ArtGenerator.Dirt;
        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                if (!dirt[x, y])
                    continue;
                bool rim = (x > 0 && !dirt[x - 1, y]) || (x < 15 && !dirt[x + 1, y]) || (y > 0 && !dirt[x, y - 1]) || (y < 15 && !dirt[x, y + 1]);
                if (rim) c.Px(x, y, DirtDark, solid: false);
                else if (Hash(x, y, mask + 60) < 70) c.Px(x, y, DirtLight, solid: false);
                else if (Hash(x, y, mask + 61) < 50) c.Px(x, y, DirtDark, solid: false);
            }
        }
    }

    /// <summary>Wooden deck over water: planks across the direction of travel, darker rails along it.</summary>
    private static void BridgeTile(PixelCanvas c, bool horizontal)
    {
        for (int a = 0; a < 16; a++)
        {
            for (int b = 3; b < 13; b++)
            {
                int x = horizontal ? a : b, y = horizontal ? b : a;
                var color = b is 3 or 12 ? WoodDark : a % 4 == 3 ? WoodDark : (a + b) % 7 == 0 ? WoodLight : Wood;
                c.Px(x, y, color, solid: false);
            }
        }
    }

    /// <summary>Transparent tile with sand and foam along the sides where land is.</summary>
    private static void ShoreTile(PixelCanvas c, int mask)
    {
        for (int i = 0; i < 16; i++)
        {
            if ((mask & 1) != 0) { c.Px(i, 0, Sand, false); c.Px(i, 1, SandDark, false); c.Px(i, 2, Foam, false); }
            if ((mask & 4) != 0) { c.Px(i, 15, Sand, false); c.Px(i, 14, SandDark, false); c.Px(i, 13, Foam, false); }
            if ((mask & 8) != 0) { c.Px(0, i, Sand, false); c.Px(1, i, SandDark, false); c.Px(2, i, Foam, false); }
            if ((mask & 2) != 0) { c.Px(15, i, Sand, false); c.Px(14, i, SandDark, false); c.Px(13, i, Foam, false); }
        }
    }

    // ------------------------------------------------------------------ units

    /// <summary>
    /// Cells of 16×20, feet at (8, 19), facing right. Rows: infantry, archer, heavy.
    /// Columns: idle, walk 1, walk 2, attack. units_team.png holds the faction-colour mask.
    /// </summary>
    private static PixelCanvas Units()
    {
        var c = new PixelCanvas(ArtLayout.UnitCellWidth * ArtLayout.UnitFrames, ArtLayout.UnitCellHeight * ArtLayout.UnitRows);
        for (int frame = 0; frame < ArtLayout.UnitFrames; frame++)
        {
            for (int row = 0; row < ArtLayout.UnitRows; row++)
            {
                c.Origin = new Vector2I(frame * ArtLayout.UnitCellWidth, row * ArtLayout.UnitCellHeight);
                switch (row)
                {
                    case 0: Infantry(c, frame); break;
                    case 1: Archer(c, frame); break;
                    case 2: Heavy(c, frame); break;
                    default: Ram(c, frame); break;
                }
                c.Outline(Outline, new Rect2I(c.Origin, new Vector2I(ArtLayout.UnitCellWidth, ArtLayout.UnitCellHeight)));
            }
        }
        return c;
    }

    /// <summary>Two legs whose stance depends on the frame: 0 idle, 1–2 walking, 3 lunging.</summary>
    private static void Legs(PixelCanvas c, int frame, int width, Color color)
    {
        (int back, int front, int lift) = frame switch
        {
            1 => (4, 10, 0),
            2 => (6, 8, 1),
            3 => (4, 10, 0),
            _ => (5, 9, 0),
        };
        c.Rect(back, 16, width, 3, color);
        c.Rect(front, 16 - lift, width, 3, color);
        c.Rect(back, 18, width, 1, Boots);
        c.Rect(front, 18 - lift, width, 1, Boots);
    }

    private static void Infantry(PixelCanvas c, int frame)
    {
        int lean = frame == 3 ? 1 : 0;
        Legs(c, frame, 2, new Color("4a3a30"));
        c.TeamRect(5 + lean, 10, 6, 6);
        c.Rect(5 + lean, 14, 6, 1, Bark);
        c.Shaded(6 + lean, 6, 4, 4, Skin, Skin, SkinDark);
        c.Rect(5 + lean, 5, 6, 2, Steel);
        c.Rect(6 + lean, 4, 4, 1, SteelLight);
        c.Px(9 + lean, 7, Outline); // eye
        if (frame == 3)
        {
            // Spear thrust forward.
            c.Line(6, 13, 14, 9, Leather);
            c.Px(15, 8, SteelLight);
            c.Px(14, 8, Steel);
            c.Rect(11, 11, 1, 2, Skin);
        }
        else
        {
            c.Rect(12, 3, 1, 15, Leather);
            c.Rect(12, 1, 1, 2, SteelLight);
            c.Px(11, 0, Steel);
            c.Rect(11, 11, 1, 2, Skin);
        }
    }

    private static void Archer(PixelCanvas c, int frame)
    {
        Legs(c, frame, 2, new Color("4a3a30"));
        c.Rect(3, 9, 2, 6, Leather); // quiver
        c.Px(3, 8, String);
        c.Px(4, 7, String);
        c.TeamRect(5, 10, 6, 6);
        c.Rect(5, 14, 6, 1, Bark);
        c.Shaded(6, 6, 4, 4, Skin, Skin, SkinDark);
        c.Rect(5, 5, 5, 2, Hood);
        c.Rect(5, 7, 1, 3, HoodDark);
        c.Px(9, 7, Outline); // eye
        if (frame == 3)
        {
            // Bow drawn: string pulled back to the cheek, arrow on the string.
            c.Line(12, 6, 13, 9, Wood);
            c.Line(13, 10, 12, 14, Wood);
            c.Line(12, 6, 9, 10, String);
            c.Line(9, 10, 12, 14, String);
            c.Rect(9, 10, 6, 1, WoodLight);
            c.Px(15, 10, SteelLight);
        }
        else
        {
            c.Line(12, 6, 13, 8, Wood);
            c.Rect(13, 9, 1, 3, Wood);
            c.Line(13, 12, 12, 14, Wood);
            c.Rect(11, 7, 1, 7, String);
        }
        c.Rect(11, 11, 1, 1, Skin);
    }

    /// <summary>
    /// A covered battering ram on two wheels: the roof is in faction colours, the wheels turn while it rolls,
    /// and on the attack frame the iron-headed log is thrust out of the front.
    /// </summary>
    private static void Ram(PixelCanvas c, int frame)
    {
        // Roof of hides over a timber frame.
        for (int row = 0; row < 5; row++)
        {
            int inset = 4 - row;
            for (int x = 1 + inset; x < 13 - inset; x++)
                c.Team(x, 6 + row, row == 4 ? 0.6f : x < 7 ? 0.95f : 0.75f);
        }
        c.Rect(1, 11, 12, 4, Wood);
        for (int x = 2; x < 13; x += 3)
            c.Rect(x, 11, 1, 4, WoodDark);
        // The log runs through the frame and out of the front; its iron head strikes on the attack frame.
        int thrust = frame == 3 ? 2 : 0;
        c.Rect(6 + thrust, 12, 7, 2, Bark);
        c.Rect(13 + thrust, 11, 1, 4, SteelDark);
        // Wheels whose spoke turns as it rolls.
        foreach (int wx in new[] { 4, 11 })
        {
            c.Disc(wx, 16, 2.4f, WoodDark);
            c.Px(wx, 16, Steel);
            var spoke = (frame % 2 == 0) ? new Vector2I(wx, 14) : new Vector2I(wx + 2, 16);
            c.Px(spoke.X, spoke.Y, WoodLight);
        }
    }

    private static void Heavy(PixelCanvas c, int frame)
    {
        int lean = frame == 3 ? 1 : 0;
        Legs(c, frame, 3, SteelDark);
        c.Shaded(4 + lean, 9, 8, 7, Steel, SteelLight, SteelDark);
        c.Team(7 + lean, 9); c.Team(8 + lean, 9);
        for (int y = 10; y < 16; y++) { c.Team(7 + lean, y, 0.85f); c.Team(8 + lean, y, 0.7f); }
        c.Shaded(5 + lean, 3, 6, 6, Steel, SteelLight, SteelDark);
        c.Rect(7 + lean, 5, 4, 1, Outline); // visor
        if (frame == 3)
        {
            // Sword swung forward over the shield.
            c.Line(10, 8, 15, 2, SteelLight);
            c.Rect(9, 8, 2, 1, Leather);
        }
        else
        {
            c.Rect(3, 3, 1, 8, SteelLight);
            c.Rect(2, 11, 3, 1, Leather);
        }
        // Shield in faction colours with a steel rim.
        c.TeamRect(11, 9, 4, 7);
        c.Rect(11, 9, 4, 1, SteelLight);
        c.Rect(11, 15, 4, 1, SteelDark);
    }

    // ------------------------------------------------------------------ buildings

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

    private static void Flag(PixelCanvas c, int x, int y)
    {
        c.Rect(x, y, 1, 9, new Color("4a3a2a"));
        c.TeamRect(x + 1, y, 6, 4);
        c.Team(x + 7, y + 1, 0.82f);
    }

    /// <summary>48×60, bottom-centre origin; the footprint is 3×3 tiles.</summary>
    private static PixelCanvas Castle()
    {
        var c = new PixelCanvas(48, 60);
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
        // Windows.
        foreach (int x in new[] { 18, 23, 28 })
            c.Rect(x, 15, 2, 3, Window);
        c.Rect(5, 30, 2, 3, Window);
        c.Rect(41, 30, 2, 3, Window);
        Banner(c, 3, 22, 6, 10);
        Banner(c, 39, 22, 6, 10);
        Flag(c, 23, 0);
        c.Outline(Outline);
        return c;
    }

    /// <summary>32×36, bottom-centre origin; footprint 2×2 tiles. The roof is in faction colours.</summary>
    private static PixelCanvas Barracks()
    {
        var c = new PixelCanvas(32, 36);
        c.Rect(2, 20, 28, 15, Wood);
        for (int x = 2; x < 30; x += 4)
            c.Rect(x, 20, 1, 15, WoodDark);
        c.Rect(2, 33, 28, 2, StoneDark);
        c.Rect(13, 26, 6, 8, Door);
        c.Rect(15, 26, 1, 8, WoodDark);
        c.Rect(5, 24, 3, 3, Window);
        c.Rect(24, 24, 3, 3, Window);
        c.Rect(24, 7, 3, 6, StoneDark); // chimney
        for (int row = 0; row < 12; row++)
        {
            int inset = 11 - row;
            for (int x = inset; x < 32 - inset; x++)
                c.Team(x, 8 + row, row == 0 ? 1f : row == 11 ? 0.55f : (x - inset) % 4 == 0 ? 0.7f : 0.85f);
        }
        c.Outline(Outline);
        return c;
    }

    /// <summary>
    /// 32×58, bottom-centre origin; footprint 2×2 tiles. A walled plinth with a gate, a stone keep behind it,
    /// an archers' gallery and a conical roof in faction colours. Level 2: stone gallery, iron bands, bigger banners;
    /// level 3: gilded eaves, finial and gate.
    /// </summary>
    private static PixelCanvas Tower(int tier = 1)
    {
        var c = new PixelCanvas(32, 58);
        Flag(c, 15, 0);
        // Keep, drawn first: the plinth wall stands in front of its foot. Higher levels are bound with iron.
        Bricks(c, 7, 24, 18, 20, StoneLight);
        c.Rect(15, 28, 2, 5, Window); // arrow slits
        c.Rect(10, 31, 1, 4, Window);
        c.Rect(21, 31, 1, 4, Window);
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
            c.Rect(x, 20, 2, 2, Window);
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
        c.Outline(Outline);
        return c;
    }

    /// <summary>
    /// 48×76, bottom-centre origin; footprint 3×3 tiles. A walled base, a slim stone shaft and an open lookout
    /// with a beacon fire under a pointed roof in faction colours. It does not shoot, so no arrow gallery.
    /// </summary>
    private static PixelCanvas Watchtower()
    {
        var fire = new Color("ff7a1a");
        var fireLight = new Color("ffd25a");
        var c = new PixelCanvas(48, 76);
        Flag(c, 23, 0);
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
        c.Rect(21, 23, 6, 4, fire);
        c.Rect(23, 21, 2, 4, fireLight);
        c.Rect(9, 30, 30, 3, Wood);
        for (int x = 10; x < 38; x += 3)
            c.Rect(x, 31, 1, 2, WoodDark);
        c.Rect(8, 33, 32, 2, WoodDark);
        // Shaft with lookout windows.
        Bricks(c, 15, 35, 18, 22, StoneLight);
        c.Rect(23, 39, 2, 5, Window);
        c.Rect(23, 48, 2, 4, Window);
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
        c.Outline(Outline);
        return c;
    }

    /// <summary>
    /// 32×40, bottom-centre origin; footprint 2×2 tiles. A stone platform with a timber frame, a throwing arm loaded
    /// with a stone, a counterweight and a banner in faction colours. Level 2: merlons and iron fittings;
    /// level 3: gilded trim and banners at the sides.
    /// </summary>
    private static PixelCanvas Catapult(int tier = 1)
    {
        var c = new PixelCanvas(32, 40);
        Flag(c, 3, 12);
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
        c.Outline(Outline);
        return c;
    }

    /// <summary>
    /// 32×36, bottom-centre origin; footprint 2×2 tiles on a gold deposit. Level 2: stone-framed tunnel, lantern,
    /// braced headframe, a second cart; level 3: steel headframe with a gilded wheel, a retaining wall, a heap of gold.
    /// </summary>
    private static PixelCanvas Mine(int tier = 1)
    {
        var lamp = new Color("ffd25a");
        var c = new PixelCanvas(32, 36);
        // Rock mound with a tunnel mouth: timbered, then framed in stone.
        for (int y = 22; y < 35; y++)
        {
            int half = 6 + (y - 22) * 9 / 12;
            for (int x = 16 - half; x < 16 + half; x++)
                c.Px(x, y, Hash(x, y, 21) < 150 ? RockLight : Rock);
        }
        if (tier >= 3)
        {
            Bricks(c, 2, 31, 28, 4, Stone); // retaining wall
        }
        c.Rect(11, 26, 9, 9, Door);
        var frame = tier >= 2 ? StoneLight : Wood;
        c.Rect(10, 25, 1, 10, frame);
        c.Rect(20, 25, 1, 10, frame);
        c.Rect(10, 25, 11, 1, tier >= 2 ? Stone : WoodLight);
        if (tier >= 2)
        {
            c.Rect(9, 24, 13, 1, StoneDark);
            c.Px(8, 27, lamp); // lantern by the entrance
            c.Px(8, 28, SteelDark);
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
        // Carts full of gold: one, then two, then a heap beside them.
        c.Rect(22, 29, 8, 4, WoodDark);
        c.Rect(23, 27, 6, 2, Gold);
        c.Px(25, 26, GoldDark);
        c.Rect(23, 33, 2, 2, Outline);
        c.Rect(27, 33, 2, 2, Outline);
        if (tier >= 2)
        {
            c.Rect(2, 29, 7, 4, WoodDark);
            c.Rect(3, 27, 5, 2, Gold);
            c.Rect(3, 33, 2, 2, Outline);
            c.Rect(6, 33, 2, 2, Outline);
        }
        if (tier >= 3)
        {
            for (int row = 0; row < 3; row++)
                c.Rect(12 + row, 22 - row, 7 - 2 * row, 1, row == 2 ? Gold : GoldDark);
        }
        Flag(c, 3, 0);
        c.Outline(Outline);
        return c;
    }

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

/// <summary>Sheet layout shared by the art generator and the views that cut frames out of the sheets.</summary>
public static class ArtLayout
{
    public const int Tile = 16;
    public const int TerrainVariants = 4;
    public const int GrassRow = 0, ForestRow = 1, WaterRow = 2, MountainRow = 3, RoadRow = 4, ShoreRow = 5, BridgeRow = 6;

    public const int UnitCellWidth = 16;
    public const int UnitCellHeight = 20;
    public const int UnitFrames = 4;
    public const int UnitRows = 4;
    public const int FrameIdle = 0, FrameWalk1 = 1, FrameWalk2 = 2, FrameAttack = 3;
    /// <summary>Feet position inside a unit cell.</summary>
    public static readonly Vector2I UnitFeet = new(8, 19);

    public static int UnitRow(string unitTypeId) => unitTypeId switch
    {
        "archer" => 1,
        "heavy" => 2,
        "ram" => 3,
        _ => 0,
    };
}

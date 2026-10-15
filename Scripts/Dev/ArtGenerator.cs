using System;
using Godot;
using Lordsio.World;

namespace Lordsio.Dev;

/// <summary>
/// Generates the game's pixel art into Assets/ (run with --export-art). The PNGs are committed and
/// loaded like any hand-drawn asset, so an artist can replace them file by file. Layout contracts
/// (cell sizes, rows, frames) are documented on each method and mirrored in <see cref="ArtLayout"/>.
/// </summary>
public static partial class ArtGenerator
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
    private static readonly Color BarkDark = new("432a15");
    private static readonly Color ForestShadow = new("426f2c");
    private static readonly Color SnowShade = new("c9d3dc");
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
        Recolor(terrain.Base, WinterPalette).SavePng(System.IO.Path.Combine(assets, "Tilesets", "terrain_winter.png"));
        Recolor(terrain.Base, DesertPalette).SavePng(System.IO.Path.Combine(assets, "Tilesets", "terrain_desert.png"));

        var units = Units();
        units.Save(System.IO.Path.Combine(assets, "Sprites", "units.png"), System.IO.Path.Combine(assets, "Sprites", "units_team.png"));

        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(assets, "UI"));
        AppIcon(Castle(), System.IO.Path.Combine(assets, "UI"));
        Interface(System.IO.Path.Combine(assets, "UI"));

        foreach (var (name, canvas) in new (string, PixelCanvas)[]
                 {
                     ("castle", Castle()), ("barracks", Barracks()), ("tower", Tower()), ("tower_t2", Tower(2)), ("tower_t3", Tower(3)), ("watchtower", Watchtower()),
                     ("mine", Mine()), ("mine_t2", Mine(2)), ("mine_t3", Mine(3)),
                     ("catapult", Catapult()), ("catapult_t2", Catapult(2)), ("catapult_t3", Catapult(3)),
                 })
        {
            canvas.Save(System.IO.Path.Combine(assets, "Sprites", name + ".png"),
                System.IO.Path.Combine(assets, "Sprites", name + "_team.png"), System.IO.Path.Combine(assets, "Sprites", name + "_lights.png"));
            SavePreview(canvas, System.IO.Path.Combine(previewDir, name + ".png"));
        }
        var gold = Deposit();
        gold.Save(System.IO.Path.Combine(assets, "Sprites", "gold.png"));

        SavePreview(terrain, System.IO.Path.Combine(previewDir, "terrain.png"));
        SavePreview(units, System.IO.Path.Combine(previewDir, "units.png"));
        SavePreview(gold, System.IO.Path.Combine(previewDir, "gold.png"));
    }

    /// <summary>Meadow colours of the terrain and what they become in the snow.</summary>
    private static readonly (Color From, Color To)[] WinterPalette =
    {
        (Grass, new Color("e3eaee")), (GrassDark, new Color("cdd8df")), (GrassLight, new Color("f4f8fa")),
        (Leaf, new Color("1f4a3a")), (LeafLight, new Color("2c5e48")), (LeafHighlight, new Color("e8f0f2")),
        (LeafDark, new Color("163a2c")), (TreeOutline, new Color("10291f")), (Bark, new Color("4a3424")),
        (ForestShadow, new Color("aebccb")), (BarkDark, new Color("33231a")),
        (Sand, new Color("c9d6de")), (SandDark, new Color("aab8c2")),
        (Rock, new Color("8a8c90")), (RockLight, new Color("e4eaee")), (RockDark, new Color("55585c")),
        (Dirt, new Color("9c8f80")), (DirtDark, new Color("7f7466")), (DirtLight, new Color("b5aa9c")),
    };

    /// <summary>Meadow colours of the terrain and what they become in the desert (palms and oases, red rocks).</summary>
    private static readonly (Color From, Color To)[] DesertPalette =
    {
        (Grass, new Color("d9b77a")), (GrassDark, new Color("c6a165")), (GrassLight, new Color("e8cb92")),
        (Leaf, new Color("4f7a32")), (LeafLight, new Color("6a9440")), (LeafHighlight, new Color("8db35a")),
        (LeafDark, new Color("3a5c24")), (TreeOutline, new Color("2a4219")), (Bark, new Color("7a5430")),
        (ForestShadow, new Color("b8955c")), (SnowShade, new Color("cfae7e")),
        (Sand, new Color("f0dca0")), (SandDark, new Color("d6bd7e")),
        (Rock, new Color("a5683f")), (RockLight, new Color("c98a5a")), (RockDark, new Color("7a4a2c")),
        (Snow, new Color("e8c89a")),
        (Dirt, new Color("b08850")), (DirtDark, new Color("94703e")), (DirtLight, new Color("c49c62")),
    };

    /// <summary>
    /// A copy of <paramref name="source"/> with each colour moved by its nearest palette entry's shift, so shades
    /// between palette colours follow too; colours far from every entry (wood, outlines) stay as they are.
    /// </summary>
    private static Image Recolor(Image source, (Color From, Color To)[] palette)
    {
        var image = (Image)source.Duplicate();
        for (int y = 0; y < image.GetHeight(); y++)
        {
            if (y / ArtLayout.Tile == ArtLayout.BridgeRow)
                continue; // bridges are timber in every biome (their wood is close enough to dirt to be taken for it)
            for (int x = 0; x < image.GetWidth(); x++)
            {
                var c = image.GetPixel(x, y);
                if (c.A <= 0f)
                    continue;
                float best = float.MaxValue;
                (Color From, Color To) match = default;
                foreach (var entry in palette)
                {
                    float d = (c.R - entry.From.R) * (c.R - entry.From.R) + (c.G - entry.From.G) * (c.G - entry.From.G)
                              + (c.B - entry.From.B) * (c.B - entry.From.B);
                    if (d < best)
                    {
                        best = d;
                        match = entry;
                    }
                }
                if (best > 0.012f)
                    continue;
                var moved = new Color(
                    Mathf.Clamp(c.R + match.To.R - match.From.R, 0f, 1f),
                    Mathf.Clamp(c.G + match.To.G - match.From.G, 0f, 1f),
                    Mathf.Clamp(c.B + match.To.B - match.From.B, 0f, 1f), c.A);
                image.SetPixel(x, y, moved);
            }
        }
        return image;
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
        int castleWidth = castle.Width / ArtLayout.BuildingFrames; // the sheet's first frame
        int ox = (size - castleWidth) / 2, oy = size - castle.Height - 8;
        for (int y = 0; y < castle.Height; y++)
        {
            for (int x = 0; x < castleWidth; x++)
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
}

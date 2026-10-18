using System;
using Godot;
using Warcrown.World;

namespace Warcrown.Dev;

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

        foreach (var land in new[] { MeadowLand, WinterLand, DesertLand })
        {
            var sheet = Terrain(land);
            string suffix = land == MeadowLand ? "" : "_" + land.Name;
            sheet.Save(System.IO.Path.Combine(assets, "Tilesets", "terrain" + suffix + ".png"));
            SavePreview(sheet, System.IO.Path.Combine(previewDir, "terrain" + suffix + ".png"));
        }
        Land = MeadowLand;

        var (units, bigUnits) = Units();
        units.Save(System.IO.Path.Combine(assets, "Sprites", "units.png"), System.IO.Path.Combine(assets, "Sprites", "units_team.png"));
        bigUnits.Save(System.IO.Path.Combine(assets, "Sprites", "units_big.png"), System.IO.Path.Combine(assets, "Sprites", "units_big_team.png"));

        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(assets, "UI"));
        AppIcon(System.IO.Path.Combine(assets, "UI"));
        Interface(System.IO.Path.Combine(assets, "UI"));

        foreach (var (name, canvas) in new (string, PixelCanvas)[]
                 {
                     ("castle", Castle()), ("citadel", Citadel()), ("barracks", Barracks()), ("tower", Tower()), ("tower_t2", Tower(2)), ("tower_t3", Tower(3)), ("watchtower", Watchtower()),
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

        SavePreview(units, System.IO.Path.Combine(previewDir, "units.png"));
        SavePreview(bigUnits, System.IO.Path.Combine(previewDir, "units_big.png"));
        SavePreview(gold, System.IO.Path.Combine(previewDir, "gold.png"));
    }

    /// <summary>
    /// The application icon: the realm's arms (<see cref="Emblem"/>) in the faction blue, 64 px square, enlarged by
    /// whole pixels: icon.png (256 px) for the window and project, icon.ico (16–256 px) for the Windows executable, and
    /// splash.png for the boot screen.
    /// </summary>
    private static void AppIcon(string uiDir)
    {
        const int size = 64;
        var team = new Color("3c6fd8");
        var icon = Emblem().Composite(team);

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

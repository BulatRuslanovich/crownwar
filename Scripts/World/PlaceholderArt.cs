using System;
using Godot;
using Lordsio.Core;
using Lordsio.Sim.World;

namespace Lordsio.World;

/// <summary>
/// Temporary pixel-art terrain generated in code, until a real tileset is drawn.
/// Atlas layout: one row per <see cref="Terrain"/> value, <see cref="Variants"/> columns of variations.
/// </summary>
public static class PlaceholderArt
{
    public const int Variants = 3;

    private static readonly Color Grass = new("5a9a3c");
    private static readonly Color GrassDark = new("4b8634");
    private static readonly Color GrassLight = new("6db04a");
    private static readonly Color Leaf = new("2e6a2c");
    private static readonly Color LeafLight = new("3f8836");
    private static readonly Color LeafDark = new("21501f");
    private static readonly Color Trunk = new("5b3a1f");
    private static readonly Color Dirt = new("a8864f");
    private static readonly Color DirtDark = new("8d6d3c");
    private static readonly Color DirtLight = new("bf9d64");
    private static readonly Color Water = new("2f6db0");
    private static readonly Color WaterLight = new("4f8fd2");
    private static readonly Color WaterDark = new("285d98");
    private static readonly Color Rock = new("6e6862");
    private static readonly Color RockLight = new("9d968e");
    private static readonly Color RockDark = new("514c47");
    private static readonly Color Snow = new("eef2f4");

    public static TileSet CreateTerrainTileSet()
    {
        int s = SimConvert.TileSize;
        int rows = Enum.GetValues<Terrain>().Length;
        var image = Image.CreateEmpty(s * Variants, s * rows, false, Image.Format.Rgba8);

        foreach (Terrain terrain in Enum.GetValues<Terrain>())
        {
            for (int v = 0; v < Variants; v++)
            {
                var rng = new Random((int)terrain * 101 + v * 7 + 1);
                var origin = new Vector2I(v * s, (int)terrain * s);
                switch (terrain)
                {
                    case Terrain.Grass: DrawGrass(image, origin, rng); break;
                    case Terrain.Forest: DrawForest(image, origin, rng); break;
                    case Terrain.Road: DrawRoad(image, origin, rng); break;
                    case Terrain.Water: DrawWater(image, origin, rng); break;
                    case Terrain.Mountain: DrawMountain(image, origin, rng); break;
                }
            }
        }

        var source = new TileSetAtlasSource
        {
            Texture = ImageTexture.CreateFromImage(image),
            TextureRegionSize = new Vector2I(s, s),
        };
        for (int row = 0; row < rows; row++)
            for (int v = 0; v < Variants; v++)
                source.CreateTile(new Vector2I(v, row));

        var tileSet = new TileSet { TileSize = new Vector2I(s, s) };
        tileSet.AddSource(source, 0);
        return tileSet;
    }

    private static void DrawGrass(Image img, Vector2I o, Random rng)
    {
        Fill(img, o, Grass);
        Speckle(img, o, rng, GrassDark, 10);
        Speckle(img, o, rng, GrassLight, 6);
    }

    private static void DrawForest(Image img, Vector2I o, Random rng)
    {
        Fill(img, o, Grass);
        Speckle(img, o, rng, GrassDark, 8);
        int cx = 7 + rng.Next(0, 2), cy = 6 + rng.Next(0, 2);
        img.FillRect(new Rect2I(o.X + cx, o.Y + cy + 4, 2, 4), Trunk);
        for (int y = -5; y <= 5; y++)
        {
            for (int x = -5; x <= 5; x++)
            {
                if (x * x + y * y > 26)
                    continue;
                var c = x + y < -3 ? LeafLight : x + y > 4 ? LeafDark : Leaf;
                img.SetPixel(o.X + cx + x, o.Y + cy + y, c);
            }
        }
    }

    private static void DrawRoad(Image img, Vector2I o, Random rng)
    {
        Fill(img, o, Dirt);
        Speckle(img, o, rng, DirtDark, 9);
        Speckle(img, o, rng, DirtLight, 6);
    }

    private static void DrawWater(Image img, Vector2I o, Random rng)
    {
        Fill(img, o, Water);
        Speckle(img, o, rng, WaterDark, 6);
        for (int i = 0; i < 3; i++)
        {
            int x = rng.Next(0, 12), y = rng.Next(1, 15);
            img.FillRect(new Rect2I(o.X + x, o.Y + y, 4, 1), WaterLight);
        }
    }

    private static void DrawMountain(Image img, Vector2I o, Random rng)
    {
        Fill(img, o, RockDark);
        int peak = 6 + rng.Next(0, 4);
        for (int y = 2; y < 16; y++)
        {
            int half = (y - 2) * 8 / 13;
            for (int x = peak - half; x <= peak + half; x++)
            {
                if (x < 0 || x > 15)
                    continue;
                var c = y < 5 ? Snow : x < peak ? RockLight : Rock;
                img.SetPixel(o.X + x, o.Y + y, c);
            }
        }
    }

    private static void Fill(Image img, Vector2I o, Color c) =>
        img.FillRect(new Rect2I(o.X, o.Y, SimConvert.TileSize, SimConvert.TileSize), c);

    private static void Speckle(Image img, Vector2I o, Random rng, Color c, int count)
    {
        for (int i = 0; i < count; i++)
            img.SetPixel(o.X + rng.Next(0, SimConvert.TileSize), o.Y + rng.Next(0, SimConvert.TileSize), c);
    }
}

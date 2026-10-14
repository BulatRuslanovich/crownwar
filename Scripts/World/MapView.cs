using Godot;
using Lordsio.Core;
using Lordsio.Dev;
using Lordsio.Sim.World;

namespace Lordsio.World;

/// <summary>
/// Draws the terrain of a <see cref="TileGrid"/>, each cell from its biome's tileset (Assets/Tilesets/terrain*.png,
/// one atlas source per biome, so meadow, snow and sand mix on one map): random variants for open ground, roads
/// shaped by their road neighbours, bridges over water, and a second layer with shores where water meets land.
/// </summary>
public partial class MapView : TileMapLayer
{

    private TileMapLayer _shore = null!;

    /// <param name="biomes">The biome of every cell, row by row; null for an all-meadow map.</param>
    public void Build(TileGrid map, System.Collections.Generic.IReadOnlyList<Biome>? biomes = null)
    {
        TileSet = CreateTileSet();
        _shore = new TileMapLayer { Name = "Shore", TileSet = TileSet };
        AddChild(_shore);

        Clear();
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                var cell = new Cell(x, y);
                var coords = new Vector2I(x, y);
                int source = (int)(biomes?[y * map.Width + x] ?? Biome.Meadow); // the atlas source of the cell's biome
                SetCell(coords, source, AtlasFor(map, cell));
                if (map.GetTerrain(cell) == Terrain.Water)
                {
                    int land = NeighbourMask(map, cell, IsLand);
                    if (land != 0)
                        _shore.SetCell(coords, source, new Vector2I(land, ArtLayout.ShoreRow));
                }
            }
        }
    }

    /// <summary>One atlas source per biome, its id the biome's number.</summary>
    private static TileSet CreateTileSet()
    {
        var tileSet = new TileSet { TileSize = new Vector2I(ArtLayout.Tile, ArtLayout.Tile) };
        foreach (var biome in System.Enum.GetValues<Biome>())
            tileSet.AddSource(CreateSource(Biomes.TilesetPath(biome)), (int)biome);
        return tileSet;
    }

    private static TileSetAtlasSource CreateSource(string texturePath)
    {
        var texture = GD.Load<Texture2D>(texturePath);
        var source = new TileSetAtlasSource
        {
            Texture = texture,
            TextureRegionSize = new Vector2I(ArtLayout.Tile, ArtLayout.Tile),
        };
        int columns = texture.GetWidth() / ArtLayout.Tile, rows = texture.GetHeight() / ArtLayout.Tile;
        for (int row = 0; row < rows; row++)
            for (int col = 0; col < columns; col++)
                source.CreateTile(new Vector2I(col, row));

        return source;
    }

    private static Vector2I AtlasFor(TileGrid map, Cell cell)
    {
        // Cheap positional hash so variants look scattered but stay stable between runs.
        int variant = (int)((uint)(cell.X * 73856093 ^ cell.Y * 19349663) % ArtLayout.TerrainVariants);
        switch (map.GetTerrain(cell))
        {
            // Woods and ranges join their neighbours into one canopy, one massif; the row follows the cell's position
            // modulo 2, so the pattern repeats only every two tiles.
            case Terrain.Forest:
                return new Vector2I(NeighbourMask(map, cell, (m, x, y) => !m.InBounds(x, y) || m.GetTerrain(new Cell(x, y)) == Terrain.Forest),
                    ArtLayout.ForestRangeRow + cell.X % 2 + cell.Y % 2 * 2);
            case Terrain.Water: return new Vector2I(variant, ArtLayout.WaterRow);
            case Terrain.Mountain:
                return new Vector2I(NeighbourMask(map, cell, (m, x, y) => !m.InBounds(x, y) || m.GetTerrain(new Cell(x, y)) == Terrain.Mountain),
                    ArtLayout.MountainRangeRow + cell.X % 2 + cell.Y % 2 * 2);
            case Terrain.Road:
                if (IsBridge(map, cell))
                    return new Vector2I(IsWater(map, cell.X, cell.Y - 1) || IsWater(map, cell.X, cell.Y + 1) ? 0 : 1, ArtLayout.BridgeRow);
                return new Vector2I(NeighbourMask(map, cell, (m, x, y) => m.InBounds(x, y) && m.GetTerrain(new Cell(x, y)) == Terrain.Road), ArtLayout.RoadRow);
            default: return new Vector2I(variant, ArtLayout.GrassRow);
        }
    }

    /// <summary>4-bit mask of orthogonal neighbours matching <paramref name="test"/>: N=1, E=2, S=4, W=8.</summary>
    private static int NeighbourMask(TileGrid map, Cell c, System.Func<TileGrid, int, int, bool> test) =>
        (test(map, c.X, c.Y - 1) ? 1 : 0) | (test(map, c.X + 1, c.Y) ? 2 : 0)
        | (test(map, c.X, c.Y + 1) ? 4 : 0) | (test(map, c.X - 1, c.Y) ? 8 : 0);

    private static bool IsWater(TileGrid map, int x, int y) =>
        map.InBounds(x, y) && map.GetTerrain(new Cell(x, y)) == Terrain.Water;

    /// <summary>A road tile with water on two opposite sides is a bridge.</summary>
    private static bool IsBridge(TileGrid map, Cell c) =>
        (IsWater(map, c.X, c.Y - 1) || IsWater(map, c.X, c.Y + 1)) && !IsWater(map, c.X - 1, c.Y) && !IsWater(map, c.X + 1, c.Y)
        || (IsWater(map, c.X - 1, c.Y) || IsWater(map, c.X + 1, c.Y)) && !IsWater(map, c.X, c.Y - 1) && !IsWater(map, c.X, c.Y + 1);

    private static bool IsLand(TileGrid map, int x, int y)
    {
        if (!map.InBounds(x, y))
            return false;
        var terrain = map.GetTerrain(new Cell(x, y));
        return terrain is Terrain.Grass or Terrain.Forest
               || (terrain == Terrain.Road && !IsBridge(map, new Cell(x, y)));
    }
}

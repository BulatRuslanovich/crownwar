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
    /// <summary>A second shore layer, for water bordering the land of two biomes.</summary>
    private TileMapLayer _shore2 = null!;
    /// <summary>Bridge decks, over the water and its shores.</summary>
    private TileMapLayer _bridges = null!;
    /// <summary>Fringes of neighbouring biomes over a cell's ground (two layers: a cell can border two other biomes).</summary>
    private TileMapLayer _edges = null!;
    private TileMapLayer _edges2 = null!;

    /// <param name="biomes">The biome of every cell, row by row; null for an all-meadow map.</param>
    public void Build(TileGrid map, System.Collections.Generic.IReadOnlyList<Biome>? biomes = null)
    {
        TileSet = CreateTileSet();
        _edges = new TileMapLayer { Name = "BiomeEdges", TileSet = TileSet };
        _edges2 = new TileMapLayer { Name = "BiomeEdges2", TileSet = TileSet };
        AddChild(_edges);
        AddChild(_edges2);
        _shore = new TileMapLayer { Name = "Shore", TileSet = TileSet };
        AddChild(_shore);
        _shore2 = new TileMapLayer { Name = "Shore2", TileSet = TileSet };
        AddChild(_shore2);
        _bridges = new TileMapLayer { Name = "Bridges", TileSet = TileSet };
        AddChild(_bridges);

        Clear();
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                var cell = new Cell(x, y);
                var coords = new Vector2I(x, y);
                int source = (int)(biomes?[y * map.Width + x] ?? Biome.Meadow); // the atlas source of the cell's biome
                SetCell(coords, source, AtlasFor(map, cell));
                if (biomes != null && map.GetTerrain(cell) == Terrain.Grass)
                    AddBiomeEdges(map, biomes, cell, (Biome)source);
                if (map.GetTerrain(cell) == Terrain.Bridge)
                    _bridges.SetCell(coords, source, new Vector2I(NeighbourMask(map, cell, IsWay), ArtLayout.BridgeRow));
                if (map.GetTerrain(cell) is Terrain.Water or Terrain.Bridge)
                {
                    // Each side's shore in the colours of the land it lies against (white snow, sand, meadow sand),
                    // so a lake across a biome border changes its shore with the land, not with the water's own cell.
                    var layer = _shore;
                    foreach (var biome in System.Enum.GetValues<Biome>())
                    {
                        int land = NeighbourMask(map, cell, (m, nx, ny) =>
                            IsLand(m, nx, ny) && (biomes?[ny * m.Width + nx] ?? Biome.Meadow) == biome);
                        if (land == 0)
                            continue;
                        layer.SetCell(coords, (int)biome, new Vector2I(land, ArtLayout.ShoreRow));
                        layer = _shore2;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Open ground next to land of another biome gets that biome's ground over the part of the cell where it prevails
    /// (by all eight neighbours), so snow, sand and meadow meet along a smooth curve rather than the tile grid.
    /// </summary>
    private void AddBiomeEdges(TileGrid map, System.Collections.Generic.IReadOnlyList<Biome> biomes, Cell cell, Biome own)
    {
        var layer = _edges;
        foreach (var other in System.Enum.GetValues<Biome>())
        {
            if (other == own)
                continue;
            int mask = 0;
            for (int i = 0; i < 8; i++)
            {
                var (dx, dy) = ArtGenerator.BlendNeighbours[i];
                int x = cell.X + dx, y = cell.Y + dy;
                if (map.InBounds(x, y) && biomes[y * map.Width + x] == other
                    && map.GetTerrain(new Cell(x, y)) is Terrain.Grass or Terrain.Forest or Terrain.Road)
                    mask |= 1 << i;
            }
            if (mask == 0)
                continue;
            layer.SetCell(new Vector2I(cell.X, cell.Y), (int)other, new Vector2I(mask % 16, ArtLayout.BiomeBlendRow + mask / 16));
            layer = _edges2;
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
            case Terrain.Water:
            case Terrain.Bridge: // the deck is a layer of its own, over the water
                return new Vector2I(variant, ArtLayout.WaterRow);
            case Terrain.Mountain:
                return new Vector2I(NeighbourMask(map, cell, (m, x, y) => !m.InBounds(x, y) || m.GetTerrain(new Cell(x, y)) == Terrain.Mountain),
                    ArtLayout.MountainRangeRow + cell.X % ArtLayout.MountainPeriod + cell.Y % ArtLayout.MountainPeriod * ArtLayout.MountainPeriod);
            case Terrain.Road:
                return new Vector2I(NeighbourMask(map, cell, IsWay), ArtLayout.RoadRow);
            default: return new Vector2I(variant, ArtLayout.GrassRow);
        }
    }

    /// <summary>4-bit mask of orthogonal neighbours matching <paramref name="test"/>: N=1, E=2, S=4, W=8.</summary>
    private static int NeighbourMask(TileGrid map, Cell c, System.Func<TileGrid, int, int, bool> test) =>
        (test(map, c.X, c.Y - 1) ? 1 : 0) | (test(map, c.X + 1, c.Y) ? 2 : 0)
        | (test(map, c.X, c.Y + 1) ? 4 : 0) | (test(map, c.X - 1, c.Y) ? 8 : 0);

    /// <summary>A road or a bridge: roads and decks join up with either.</summary>
    private static bool IsWay(TileGrid map, int x, int y) =>
        map.InBounds(x, y) && map.GetTerrain(new Cell(x, y)) is Terrain.Road or Terrain.Bridge;

    private static bool IsLand(TileGrid map, int x, int y)
    {
        if (!map.InBounds(x, y))
            return false;
        var terrain = map.GetTerrain(new Cell(x, y));
        return terrain is Terrain.Grass or Terrain.Forest or Terrain.Road;
    }
}

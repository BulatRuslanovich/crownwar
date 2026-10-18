using Godot;
using Warcrown.Core;
using Warcrown.Sim.World;

namespace Warcrown.World;

/// <summary>
/// Draws the terrain of a <see cref="TileGrid"/>, each cell from its biome's tileset (Assets/Tilesets/terrain*.png,
/// one atlas source per biome, so meadow, snow and sand mix on one map): random variants for open ground, roads
/// shaped by their road neighbours, bridges over water, and a second layer with shores where water meets land.
/// Water glitters, surf washes the shores and the woods sway (animated tiles), and open ground is strewn with decor
/// of its biome: flowers and stumps, snowdrifts and firs, cacti and bones.
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
    /// <summary>Small things lying on open ground; cleared under buildings once they are seen.</summary>
    private TileMapLayer _decor = null!;

    /// <summary>Share of open cells with decor: in a clump, and elsewhere.</summary>
    private const int ClumpDecorPerMille = 190, SparseDecorPerMille = 45;

    private TileGrid _map = null!;
    private System.Collections.Generic.IReadOnlyList<Biome>? _biomes;

    /// <param name="biomes">The biome of every cell, row by row; null for an all-meadow map.</param>
    public void Build(TileGrid map, System.Collections.Generic.IReadOnlyList<Biome>? biomes = null)
    {
        _map = map;
        _biomes = biomes;
        TileSet = CreateTileSet();
        _edges = new TileMapLayer { Name = "BiomeEdges", TileSet = TileSet };
        _edges2 = new TileMapLayer { Name = "BiomeEdges2", TileSet = TileSet };
        AddChild(_edges);
        AddChild(_edges2);
        _decor = new TileMapLayer { Name = "Decor", TileSet = TileSet };
        AddChild(_decor);
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
                bool edge = biomes != null && map.GetTerrain(cell) == Terrain.Grass && AddBiomeEdges(map, biomes, cell, (Biome)source);
                // Land by the water: the water bites into its corners and edges along a smooth coast.
                if (IsLand(map, x, y) && WaterMask(map, cell) is var water and not 0)
                {
                    _shore.SetCell(coords, source, ArtLayout.CoastTile(water));
                    edge = true; // no decor under the beach
                }
                if (!edge && map.GetTerrain(cell) == Terrain.Grass)
                    AddDecor(cell, (Biome)source);
                if (map.GetTerrain(cell) == Terrain.Bridge)
                    _bridges.SetCell(coords, source, new Vector2I(NeighbourMask(map, cell, IsWay), ArtLayout.BridgeRow));
                if (map.GetTerrain(cell) is Terrain.Water or Terrain.Bridge)
                {
                    // Each side's shore in the colours of the land it lies against (white snow, sand, meadow sand),
                    // so a lake across a biome border changes its shore with the land, not with the water's own cell.
                    // All eight neighbours count, so the coast rounds the corners of the land instead of following
                    // the grid.
                    var layer = _shore;
                    foreach (var biome in System.Enum.GetValues<Biome>())
                    {
                        int land = 0;
                        for (int i = 0; i < 8; i++)
                        {
                            var (dx, dy) = ArtLayout.BlendNeighbours[i];
                            int nx = x + dx, ny = y + dy;
                            if (IsLand(map, nx, ny) && (biomes?[ny * map.Width + nx] ?? Biome.Meadow) == biome)
                                land |= 1 << i;
                        }
                        if (land == 0)
                            continue;
                        layer.SetCell(coords, (int)biome, ArtLayout.ShoreTile(land));
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
    /// <returns>Whether the cell borders another biome (it then gets no decor: it would not match the ground around it).</returns>
    private bool AddBiomeEdges(TileGrid map, System.Collections.Generic.IReadOnlyList<Biome> biomes, Cell cell, Biome own)
    {
        bool any = false;
        var layer = _edges;
        foreach (var other in System.Enum.GetValues<Biome>())
        {
            if (other == own)
                continue;
            int mask = 0;
            for (int i = 0; i < 8; i++)
            {
                var (dx, dy) = ArtLayout.BlendNeighbours[i];
                int x = cell.X + dx, y = cell.Y + dy;
                if (map.InBounds(x, y) && biomes[y * map.Width + x] == other
                    && map.GetTerrain(new Cell(x, y)) is Terrain.Grass or Terrain.Forest or Terrain.Road)
                    mask |= 1 << i;
            }
            if (mask == 0)
                continue;
            layer.SetCell(new Vector2I(cell.X, cell.Y), (int)other, new Vector2I(mask % 16, ArtLayout.BiomeBlendRow + mask / 16));
            layer = _edges2;
            any = true;
        }
        return any;
    }

    /// <summary>
    /// Now and then something lies on open ground: more in clumps (a meadow of flowers, a boneyard) than elsewhere.
    /// The pick depends only on the cell, so every player sees the same.
    /// </summary>
    private void AddDecor(Cell cell, Biome biome)
    {
        uint clump = Hash(cell.X / 5, cell.Y / 5, 17) % 1000;
        int chance = clump < 280 ? ClumpDecorPerMille : SparseDecorPerMille;
        if (Hash(cell.X, cell.Y, 29) % 1000 >= chance)
            return;
        var pool = biome switch
        {
            Biome.Winter => ArtLayout.WinterDecor,
            Biome.Desert => ArtLayout.DesertDecor,
            _ => ArtLayout.MeadowDecor,
        };
        // Within a clump the same few kinds repeat; elsewhere anything goes.
        uint pick = clump < 280 ? (clump + Hash(cell.X, cell.Y, 31) % 3) : Hash(cell.X, cell.Y, 37);
        int kind = pool[pick % (uint)pool.Length];
        _decor.SetCell(new Vector2I(cell.X, cell.Y), (int)biome, new Vector2I(kind % 16, ArtLayout.DecorRow + kind / 16));
    }

    /// <summary>
    /// Shows the clearing under a building: the woods on its site were felled when it was founded (the map's cells are
    /// open ground now), so the footprint is drawn afresh as its biome's ground, with the fringes of neighbouring
    /// biomes, and so are the woods around it, whose edge has moved.
    /// </summary>
    public void ClearWoods(Cell anchor, int size)
    {
        for (int y = anchor.Y - 1; y <= anchor.Y + size; y++)
            for (int x = anchor.X - 1; x <= anchor.X + size; x++)
            {
                var cell = new Cell(x, y);
                if (!_map.InBounds(cell) || _map.GetTerrain(cell) is not (Terrain.Grass or Terrain.Forest))
                    continue;
                var coords = new Vector2I(x, y);
                var biome = _biomes?[y * _map.Width + x] ?? Biome.Meadow;
                SetCell(coords, (int)biome, AtlasFor(_map, cell));
                bool site = x >= anchor.X && y >= anchor.Y && x < anchor.X + size && y < anchor.Y + size;
                if (site && _biomes != null && _map.GetTerrain(cell) == Terrain.Grass)
                {
                    _edges.EraseCell(coords);
                    _edges2.EraseCell(coords);
                    AddBiomeEdges(_map, _biomes, cell, biome);
                }
            }
    }

    /// <summary>Takes the decor off a building's (or a deposit's) footprint.</summary>
    public void ClearDecor(Cell anchor, int size)
    {
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                _decor.EraseCell(new Vector2I(anchor.X + x, anchor.Y + y));
    }

    private static uint Hash(int x, int y, int salt) => (uint)(x * 73856093 ^ y * 19349663 ^ salt * 83492791);

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
        // The cells under an animated tile hold its later frames: no tiles of their own.
        var frames = new System.Collections.Generic.HashSet<Vector2I>();
        foreach (var group in ArtLayout.AnimatedTiles())
            for (int frame = 1; frame < ArtLayout.TileFrames; frame++)
                for (int col = 0; col < group.Columns; col++)
                    frames.Add(new Vector2I(col, group.Row + frame));
        for (int row = 0; row < rows; row++)
            for (int col = 0; col < columns; col++)
                if (!frames.Contains(new Vector2I(col, row)))
                    source.CreateTile(new Vector2I(col, row));

        foreach (var group in ArtLayout.AnimatedTiles())
        {
            for (int col = 0; col < group.Columns; col++)
            {
                var tile = new Vector2I(col, group.Row);
                source.SetTileAnimationColumns(tile, 1); // frames run down the sheet
                source.SetTileAnimationFramesCount(tile, ArtLayout.TileFrames);
                for (int frame = 0; frame < ArtLayout.TileFrames; frame++)
                    source.SetTileAnimationFrameDuration(tile, frame, group.Durations[frame]);
                if (group.RandomStart)
                    source.SetTileAnimationMode(tile, TileSetAtlasSource.TileAnimationMode.RandomStartTimes);
            }
        }
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
                    ArtLayout.ForestRangeRow + (cell.X % ArtLayout.ForestPeriod + cell.Y % ArtLayout.ForestPeriod * ArtLayout.ForestPeriod) * ArtLayout.TileFrames);
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

    /// <summary>8-bit mask of the neighbours (<see cref="ArtLayout.BlendNeighbours"/>) that are water (or a bridge over it).</summary>
    private static int WaterMask(TileGrid map, Cell c)
    {
        int mask = 0;
        for (int i = 0; i < 8; i++)
        {
            var (dx, dy) = ArtLayout.BlendNeighbours[i];
            int x = c.X + dx, y = c.Y + dy;
            if (map.InBounds(x, y) && map.GetTerrain(new Cell(x, y)) is Terrain.Water or Terrain.Bridge)
                mask |= 1 << i;
        }
        return mask;
    }

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

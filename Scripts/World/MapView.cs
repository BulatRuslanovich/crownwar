using Godot;
using Lordsio.Sim.World;

namespace Lordsio.World;

/// <summary>Draws the static terrain of a <see cref="TileGrid"/>.</summary>
public partial class MapView : TileMapLayer
{
    private const int AtlasSourceId = 0;

    public void Build(TileGrid map)
    {
        TileSet = PlaceholderArt.CreateTerrainTileSet();
        Clear();
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                var terrain = map.GetTerrain(new Cell(x, y));
                // Cheap positional hash so variants look scattered but stay stable between runs.
                int variant = (int)((uint)(x * 73856093 ^ y * 19349663) % PlaceholderArt.Variants);
                SetCell(new Vector2I(x, y), AtlasSourceId, new Vector2I(variant, (int)terrain));
            }
        }
    }
}

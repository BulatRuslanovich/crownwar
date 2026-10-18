using Godot;
using Warcrown.Sim.World;

namespace Warcrown.World;

/// <summary>How each <see cref="Biome"/> looks: its terrain tileset and its colours on the minimap.</summary>
public static class Biomes
{
    /// <summary>The terrain tileset of a biome (the winter and desert ones are recoloured copies of the meadow's).</summary>
    public static string TilesetPath(Biome biome) => biome switch
    {
        Biome.Winter => "res://Assets/Tilesets/terrain_winter.png",
        Biome.Desert => "res://Assets/Tilesets/terrain_desert.png",
        _ => "res://Assets/Tilesets/terrain.png",
    };

    /// <summary>A terrain's colour on the minimap.</summary>
    public static Color MinimapColor(Biome biome, Terrain terrain) => (biome, terrain) switch
    {
        // The same in every biome.
        (_, Terrain.Water) => new Color("2b5a84"),
        (_, Terrain.Bridge) => new Color("7a5536"),
        (Biome.Winter, Terrain.Forest) => new Color("28503f"),
        (Biome.Winter, Terrain.Mountain) => new Color("b9c0c6"),
        (Biome.Winter, Terrain.Road) => new Color("8a8178"),
        (Biome.Winter, _) => new Color("d3dce4"),
        (Biome.Desert, Terrain.Forest) => new Color("56783a"),
        (Biome.Desert, Terrain.Mountain) => new Color("9b6a45"),
        (Biome.Desert, Terrain.Road) => new Color("a5824f"),
        (Biome.Desert, _) => new Color("cba672"),
        (_, Terrain.Forest) => new Color("355f35"),
        (_, Terrain.Mountain) => new Color("76716b"),
        (_, Terrain.Road) => new Color("8b7150"),
        _ => new Color("5b7e3e"),
    };
}

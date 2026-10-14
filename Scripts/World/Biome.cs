using Godot;
using Lordsio.Sim.World;

namespace Lordsio.World;

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
        (Biome.Winter, Terrain.Forest) => new Color("2c5e48"),
        (Biome.Winter, Terrain.Mountain) => new Color("b9c0c6"),
        (Biome.Winter, Terrain.Road) => new Color("9c8f80"),
        (Biome.Winter, _) => new Color("dfe7ec"),
        (Biome.Desert, Terrain.Forest) => new Color("5a8236"),
        (Biome.Desert, Terrain.Mountain) => new Color("a5683f"),
        (Biome.Desert, Terrain.Road) => new Color("b08850"),
        (Biome.Desert, _) => new Color("d9b77a"),
        (_, Terrain.Forest) => new Color("2e6a2c"),
        (_, Terrain.Water) => new Color("2f6db0"),
        (_, Terrain.Mountain) => new Color("7a746c"),
        (_, Terrain.Road) => new Color("b39461"),
        _ => new Color("5a9a3c"),
    };
}

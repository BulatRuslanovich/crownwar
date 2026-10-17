using Godot;
using Warcrown.Sim.Buildings;

namespace Warcrown.World;

/// <summary>Where the sprite sheets are (Assets/Sprites, written by the art generator) and which one a building uses.</summary>
public static class Sprites
{
    /// <summary>The soldiers' sheet (or the big beasts', <paramref name="big"/>), one row a kind (see <see cref="ArtLayout.UnitArtOf"/>);
    /// <paramref name="layer"/> "_team" for its faction-colour mask.</summary>
    public static string Units(bool big, string layer = "") => Path(big ? "units_big" : "units", layer);

    /// <summary>A sheet by name; <paramref name="layer"/> is "" for the picture, "_team" for its colour mask, "_lights" for its night lights.</summary>
    public static string Path(string name, string layer = "") => $"res://Assets/Sprites/{name}{layer}.png";

    /// <summary>The sheet of a building's level: its own if the level has one (e.g. "tower_t2"), else the first level's.</summary>
    public static string BuildingName(BuildingType type)
    {
        string tiered = $"{type.Id}_t{type.Tier}";
        return type.Tier > 1 && ResourceLoader.Exists(Path(tiered)) ? tiered : type.Id;
    }
}

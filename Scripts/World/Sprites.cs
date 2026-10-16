using Godot;
using Warcrown.Sim.Buildings;

namespace Warcrown.World;

/// <summary>Where the sprite sheets are (Assets/Sprites, written by the art generator) and which one a building uses.</summary>
public static class Sprites
{
    /// <summary>Every unit kind, one row each (see <see cref="ArtLayout.UnitRow"/>).</summary>
    public const string Units = "res://Assets/Sprites/units.png";

    /// <summary>The faction-colour mask of <see cref="Units"/>.</summary>
    public const string UnitsTeam = "res://Assets/Sprites/units_team.png";

    /// <summary>A sheet by name; <paramref name="layer"/> is "" for the picture, "_team" for its colour mask, "_lights" for its night lights.</summary>
    public static string Path(string name, string layer = "") => $"res://Assets/Sprites/{name}{layer}.png";

    /// <summary>The sheet of a building's level: its own if the level has one (e.g. "tower_t2"), else the first level's.</summary>
    public static string BuildingName(BuildingType type)
    {
        string tiered = $"{type.Id}_t{type.Tier}";
        return type.Tier > 1 && ResourceLoader.Exists(Path(tiered)) ? tiered : type.Id;
    }
}

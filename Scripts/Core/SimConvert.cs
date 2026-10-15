using Godot;
using SimVector2 = System.Numerics.Vector2;

namespace Lordsio.Core;

/// <summary>Conversion between simulation tile units and Godot world pixels.</summary>
public static class SimConvert
{
    public const int TileSize = 16;

    public static Vector2 ToWorld(SimVector2 p) => new(p.X * TileSize, p.Y * TileSize);

    public static SimVector2 ToSim(Vector2 p) => new(p.X / TileSize, p.Y / TileSize);
}

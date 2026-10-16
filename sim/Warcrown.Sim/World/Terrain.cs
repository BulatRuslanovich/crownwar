namespace Warcrown.Sim.World;

public enum Terrain : byte
{
    Grass,
    Forest,
    Road,
    Water,
    Mountain,
    /// <summary>A road across water: walkable like a road, but nothing can be built on it.</summary>
    Bridge,
}

public static class TerrainInfo
{
    /// <summary>Buildings go on open ground only: roads and bridges must stay passable.</summary>
    public static bool IsBuildable(Terrain terrain) =>
        terrain is Terrain.Grass or Terrain.Forest;

    public static bool IsWalkable(Terrain terrain) =>
        terrain is Terrain.Grass or Terrain.Forest or Terrain.Road or Terrain.Bridge;

    /// <summary>Map file symbol for each terrain type.</summary>
    public static bool TryParse(char symbol, out Terrain terrain)
    {
        switch (symbol)
        {
            case '.': terrain = Terrain.Grass; return true;
            case 'f': terrain = Terrain.Forest; return true;
            case '=': terrain = Terrain.Road; return true;
            case '~': terrain = Terrain.Water; return true;
            case '^': terrain = Terrain.Mountain; return true;
            case '#': terrain = Terrain.Bridge; return true;
            default: terrain = default; return false;
        }
    }
}

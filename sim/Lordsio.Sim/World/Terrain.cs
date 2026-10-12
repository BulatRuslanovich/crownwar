namespace Lordsio.Sim.World;

public enum Terrain : byte
{
    Grass,
    Forest,
    Road,
    Water,
    Mountain,
}

public static class TerrainInfo
{
    /// <summary>Buildings go on open ground only: roads and bridges must stay passable.</summary>
    public static bool IsBuildable(Terrain terrain) =>
        terrain is Terrain.Grass or Terrain.Forest;

    public static bool IsWalkable(Terrain terrain) =>
        terrain is Terrain.Grass or Terrain.Forest or Terrain.Road;

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
            default: terrain = default; return false;
        }
    }
}

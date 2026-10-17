using System.Numerics;
using Warcrown.Sim.World;

namespace Warcrown.Sim.Units;

/// <summary>How flyers get about: straight lines over water, rock and roofs, kept inside the map.</summary>
public static class Flight
{
    /// <summary>How far inside the map edge a flyer keeps its body (it is drawn there and must stay clickable).</summary>
    private const float EdgeMargin = 0.5f;

    /// <summary>The route of a flyer to <paramref name="destination"/>: the one point itself.</summary>
    public static List<Vector2> PathTo(Vector2 destination) => new(1) { destination };

    /// <summary><paramref name="p"/> moved inside the map.</summary>
    public static Vector2 Clamp(TileGrid map, Vector2 p) =>
        new(Math.Clamp(p.X, EdgeMargin, map.Width - EdgeMargin), Math.Clamp(p.Y, EdgeMargin, map.Height - EdgeMargin));
}

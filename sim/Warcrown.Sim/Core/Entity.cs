using System.Numerics;

namespace Warcrown.Sim.Core;

/// <summary>
/// Anything that has an owner and health and can be attacked: units and buildings.
/// Ids are unique across both, so a command can name either as a target.
/// </summary>
public abstract class Entity
{
    protected Entity(int id, int factionId, Vector2 position, int health)
    {
        Id = id;
        FactionId = factionId;
        Position = position;
        Health = health;
    }

    public int Id { get; }

    /// <summary>Owner faction, or -1 for neutral.</summary>
    public int FactionId { get; }

    /// <summary>Center (buildings) or feet (units), in tile units.</summary>
    public Vector2 Position { get; internal set; }

    public int Health { get; internal set; }
    public abstract int MaxHealth { get; }
    public bool IsAlive => Health > 0;

    /// <summary>Fraction of projectile damage ignored (0..1).</summary>
    public abstract float RangedArmor { get; }

    /// <summary>Distance from <paramref name="point"/> to the edge of this body (0 when inside).</summary>
    public abstract float SurfaceDistance(Vector2 point);

    /// <summary>Faction that dealt the killing blow, for the death / destruction event.</summary>
    internal int KilledByFactionId { get; set; } = -1;

    /// <summary>The unit or building that dealt the killing blow, or -1; a unit earns experience by it.</summary>
    internal int KilledById { get; set; } = -1;
}

using System.Numerics;
using Warcrown.Sim.Core;

namespace Warcrown.Sim.Combat;

/// <summary>
/// A homing missile (an arrow, or a catapult's stone) shot by an archer or a tower. Whether it hits is decided here,
/// in the simulation: it always reaches a living target, and fizzles if the target is gone first. A stone with a
/// <see cref="SplashRadius"/> hurts every enemy unit that close to where it lands. A bolt with <see cref="ArmorPiercing"/>
/// ignores that share of the target's ranged armour; a trebuchet's stone (<see cref="IsStone"/>) flies in an arc.
/// </summary>
public sealed class Projectile
{
    internal Projectile(int sourceId, int factionId, int damage, float speed, Vector2 from, Entity target, float splashRadius = 0f,
        float armorPiercing = 0f, bool isStone = false)
    {
        ArmorPiercing = armorPiercing;
        IsStone = isStone || splashRadius > 0f;
        SourceId = sourceId;
        FactionId = factionId;
        Damage = damage;
        Speed = speed;
        Target = target;
        Position = from;
        PreviousPosition = from;
        Origin = from;
        SplashRadius = splashRadius;
    }

    /// <summary>Where it was shot from (a stone flies in an arc between there and its target).</summary>
    public Vector2 Origin { get; }
    public float SplashRadius { get; }
    public float ArmorPiercing { get; }

    /// <summary>A stone (catapult, trebuchet), drawn flying in an arc rather than as an arrow.</summary>
    public bool IsStone { get; }

    /// <summary>Id of the archer or tower that shot it (it may be gone by the time the arrow lands).</summary>
    public int SourceId { get; }
    public int FactionId { get; }
    public int Damage { get; }
    public float Speed { get; }
    public Entity Target { get; }
    public Vector2 Position { get; internal set; }
    public Vector2 PreviousPosition { get; internal set; }
}

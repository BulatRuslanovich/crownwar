using System.Numerics;
using Lordsio.Sim.Core;

namespace Lordsio.Sim.Combat;

/// <summary>
/// A homing missile (an arrow, or a catapult's stone) shot by an archer or a tower. Whether it hits is decided here,
/// in the simulation: it always reaches a living target, and fizzles if the target is gone first. A stone with a
/// <see cref="SplashRadius"/> hurts every enemy unit that close to where it lands.
/// </summary>
public sealed class Projectile
{
    internal Projectile(int sourceId, int factionId, int damage, float speed, Vector2 from, Entity target, float splashRadius = 0f)
    {
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

    /// <summary>Id of the archer or tower that shot it (it may be gone by the time the arrow lands).</summary>
    public int SourceId { get; }
    public int FactionId { get; }
    public int Damage { get; }
    public float Speed { get; }
    public Entity Target { get; }
    public Vector2 Position { get; internal set; }
    public Vector2 PreviousPosition { get; internal set; }
}

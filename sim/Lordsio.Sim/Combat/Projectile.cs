using System.Numerics;
using Lordsio.Sim.Units;

namespace Lordsio.Sim.Combat;

/// <summary>
/// A homing missile (arrow). Whether it hits is decided here, in the simulation:
/// it always reaches a living target, and fizzles if the target dies first.
/// </summary>
public sealed class Projectile
{
    internal Projectile(Unit source, Unit target)
    {
        FactionId = source.FactionId;
        Damage = source.Type.Damage;
        Speed = source.Type.ProjectileSpeed;
        Target = target;
        Position = source.Position;
        PreviousPosition = source.Position;
    }

    public int FactionId { get; }
    public int Damage { get; }
    public float Speed { get; }
    public Unit Target { get; }
    public Vector2 Position { get; internal set; }
    public Vector2 PreviousPosition { get; internal set; }
}

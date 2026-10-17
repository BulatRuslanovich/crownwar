using System.Numerics;

namespace Warcrown.Sim.Units;

/// <summary>
/// Soft body separation: overlapping units are pushed apart a bit every tick.
/// Moving units shove idle ones aside more than the other way round, so a marching group
/// flows through units standing in its way instead of stopping in front of them. Flyers only jostle other flyers: the
/// air and the ground do not push each other.
/// </summary>
internal static class UnitCollisions
{
    /// <summary>Fraction of the overlap removed per tick; below 1 to avoid jitter.</summary>
    private const float Stiffness = 0.5f;
    private const float MoverShare = 0.2f;

    /// <summary>All-pairs check. Fine for tens of units; replace with a spatial grid once profiling asks for it.</summary>
    public static void Resolve(IReadOnlyList<Unit> units)
    {
        for (int i = 0; i < units.Count; i++)
        {
            var a = units[i];
            for (int j = i + 1; j < units.Count; j++)
            {
                var b = units[j];
                if (a.Type.IsFlying != b.Type.IsFlying)
                    continue;
                float minDist = a.Type.Radius + b.Type.Radius;
                Vector2 delta = b.Position - a.Position;
                float distSq = delta.LengthSquared();
                if (distSq >= minDist * minDist)
                    continue;

                float dist = MathF.Sqrt(distSq);
                Vector2 normal = dist > 1e-5f ? delta / dist : FallbackDirection(a.Id, b.Id);
                float push = (minDist - dist) * Stiffness;

                bool aMoving = a.State == UnitState.Moving, bMoving = b.State == UnitState.Moving;
                float aShare = aMoving == bMoving ? 0.5f : aMoving ? MoverShare : 1f - MoverShare;
                a.Position -= normal * (push * aShare);
                b.Position += normal * (push * (1f - aShare));
            }
        }
    }

    /// <summary>Deterministic direction for two units at exactly the same point.</summary>
    private static Vector2 FallbackDirection(int idA, int idB)
    {
        float angle = (idA * 0.618034f + idB * 0.414214f) * MathF.Tau;
        return new Vector2(MathF.Cos(angle), MathF.Sin(angle));
    }
}

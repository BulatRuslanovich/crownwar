using System.Numerics;
using Lordsio.Sim.Core;
using Lordsio.Sim.Units;
using Lordsio.Sim.World;

namespace Lordsio.Sim.Combat;

/// <summary>
/// Per-unit fighting behaviour (target choice, chasing, attacking) and projectile flight.
/// Damage is applied here by the rules, never by animations.
/// </summary>
internal sealed class CombatSystem
{
    /// <summary>Targets are (re)acquired every this many ticks, staggered by unit id.</summary>
    internal const int ScanIntervalTicks = 5;
    /// <summary>A chased target is dropped once it is this many sight ranges away.</summary>
    private const float LoseTargetFactor = 1.5f;
    /// <summary>A guarding unit gives up a chase this far (tiles) from its post and walks back.</summary>
    internal const float LeashRange = 8f;
    /// <summary>A guard further than this from its post after a fight walks back; small shoves are ignored.</summary>
    private const float ReturnToPostDistance = 1.5f;
    private const int ChaseRepathIntervalTicks = 10;
    /// <summary>An attack-moving unit within this distance of its slot counts as arrived.</summary>
    private const float ArrivalSlack = 1.5f;

    private readonly TileGrid _map;
    private readonly Pathfinder _pathfinder;
    private readonly List<Projectile> _projectiles;

    public CombatSystem(TileGrid map, Pathfinder pathfinder, List<Projectile> projectiles)
    {
        _map = map;
        _pathfinder = pathfinder;
        _projectiles = projectiles;
    }

    public static bool InAttackRange(Unit attacker, Unit target) =>
        Vector2.Distance(attacker.Position, target.Position) - attacker.Type.Radius - target.Type.Radius
        <= attacker.Type.AttackRange;

    /// <summary>Decides what the unit does this tick: fight, chase, resume its march or go back to its post.</summary>
    public void UpdateBehaviour(Unit unit, IReadOnlyList<Unit> units, int tick)
    {
        if (unit.AttackCooldownTicks > 0)
            unit.AttackCooldownTicks--;
        unit.IsAttacking = false;
        if (unit.Order == UnitOrder.Move)
            return;

        if (unit.Target is { } current && !IsStillValidTarget(unit, current))
            unit.Target = null;
        if (unit.Order == UnitOrder.AttackUnit && unit.Target == null)
        {
            unit.CompleteOrder();
            return;
        }
        if (unit.Order == UnitOrder.None && unit.Target != null
            && Vector2.Distance(unit.Position, unit.GuardPoint) > LeashRange)
        {
            ReturnToPost(unit);
            return;
        }

        if (unit.Target == null && (tick + unit.Id) % ScanIntervalTicks == 0)
            unit.Target = FindNearestEnemy(unit, units);

        if (unit.Target is { } target)
        {
            if (InAttackRange(unit, target))
            {
                unit.ClearPath();
                unit.IsAttacking = true;
                if (unit.AttackCooldownTicks == 0)
                    Attack(unit, target);
            }
            else
            {
                Chase(unit, target);
            }
            return;
        }

        // Nothing to fight.
        if (unit.Order == UnitOrder.AttackMove && unit.State == UnitState.Idle && unit.Destination is { } destination)
        {
            if (Vector2.Distance(unit.Position, destination) <= ArrivalSlack)
            {
                unit.CompleteOrder();
                return;
            }
            var path = _pathfinder.FindPath(unit.Position, destination, unit.Type.Radius);
            if (path == null)
                unit.CompleteOrder();
            else
                unit.ReplacePath(path);
        }
        else if (unit.Order == UnitOrder.None
                 && (unit.State == UnitState.Moving
                     || Vector2.Distance(unit.Position, unit.GuardPoint) > ReturnToPostDistance))
        {
            ReturnToPost(unit); // the intruder is dead or gone
        }
    }

    public void AdvanceProjectiles(float dt)
    {
        for (int i = _projectiles.Count - 1; i >= 0; i--)
        {
            var p = _projectiles[i];
            p.PreviousPosition = p.Position;
            if (!p.Target.IsAlive)
            {
                _projectiles.RemoveAt(i);
                continue;
            }

            Vector2 toTarget = p.Target.Position - p.Position;
            float dist = toTarget.Length();
            float step = p.Speed * dt;
            if (dist <= step)
            {
                p.Position = p.Target.Position;
                ApplyDamage(p.Target, p.Damage, ranged: true, p.FactionId);
                _projectiles.RemoveAt(i);
            }
            else
            {
                p.Position += toTarget / dist * step;
            }
        }
    }

    public static void ApplyDamage(Unit target, int damage, bool ranged, int attackerFactionId)
    {
        if (!target.IsAlive)
            return;
        int dealt = ranged ? Math.Max(1, (int)MathF.Round(damage * (1f - target.Type.RangedArmor))) : damage;
        target.Health -= dealt;
        if (!target.IsAlive)
            target.KilledByFactionId = attackerFactionId;
    }

    private bool IsStillValidTarget(Unit unit, Unit target)
    {
        if (!target.IsAlive)
            return false;
        if (unit.Order == UnitOrder.AttackUnit)
            return true; // explicit orders are followed anywhere
        float limit = unit.Type.SightRange * LoseTargetFactor;
        return Vector2.DistanceSquared(unit.Position, target.Position) <= limit * limit;
    }

    private static Unit? FindNearestEnemy(Unit unit, IReadOnlyList<Unit> units)
    {
        Unit? best = null;
        float bestDistSq = unit.Type.SightRange * unit.Type.SightRange;
        foreach (var other in units)
        {
            if (other.FactionId == unit.FactionId || !other.IsAlive)
                continue;
            float distSq = Vector2.DistanceSquared(unit.Position, other.Position);
            if (distSq <= bestDistSq)
            {
                bestDistSq = distSq;
                best = other;
            }
        }
        return best;
    }

    private void Attack(Unit attacker, Unit target)
    {
        attacker.AttackCooldownTicks = Math.Max(1, (int)MathF.Round(attacker.Type.AttackCooldown * Simulation.TicksPerSecond));
        if (attacker.Type.IsRanged)
            _projectiles.Add(new Projectile(attacker, target));
        else
            ApplyDamage(target, attacker.Type.Damage, ranged: false, attacker.FactionId);
    }

    private void Chase(Unit unit, Unit target)
    {
        if (_map.IsCorridorClear(unit.Position, target.Position, unit.Type.Radius))
        {
            unit.SetSingleWaypoint(target.Position);
            unit.ChaseRepathTicks = 0;
            return;
        }
        if (--unit.ChaseRepathTicks > 0 && unit.State == UnitState.Moving)
            return;

        var path = _pathfinder.FindPath(unit.Position, target.Position, unit.Type.Radius);
        if (path == null)
        {
            unit.Target = null;
            return;
        }
        unit.ReplacePath(path);
        unit.ChaseRepathTicks = ChaseRepathIntervalTicks;
    }

    private void ReturnToPost(Unit unit)
    {
        Vector2 post = unit.GuardPoint;
        var path = _pathfinder.FindPath(unit.Position, post, unit.Type.Radius);
        if (path == null || path.Count == 0)
        {
            unit.CompleteOrder();
            return;
        }
        unit.StartMove(UnitOrder.Move, post, path);
    }
}

using System.Numerics;
using Lordsio.Sim.Buildings;
using Lordsio.Sim.Core;
using Lordsio.Sim.Units;
using Lordsio.Sim.World;

namespace Lordsio.Sim.Combat;

/// <summary>
/// Fighting behaviour of units (target choice, chasing, attacking) and towers, plus projectile flight.
/// Damage is applied here by the rules, never by animations.
/// </summary>
internal sealed class CombatSystem
{
    /// <summary>Targets are (re)acquired every this many ticks, staggered by id.</summary>
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
    /// <summary>How far around a building we look for a free spot to stand and hit it from.</summary>
    private const int ApproachSearchRadius = 6;

    private readonly TileGrid _map;
    private readonly Pathfinder _pathfinder;
    private readonly List<Projectile> _projectiles;
    private readonly Action<SimEvent> _emit;
    private readonly Func<int, int, bool> _areEnemies;

    public CombatSystem(TileGrid map, Pathfinder pathfinder, List<Projectile> projectiles, Action<SimEvent> emit,
        Func<int, int, bool> areEnemies)
    {
        _map = map;
        _pathfinder = pathfinder;
        _projectiles = projectiles;
        _emit = emit;
        _areEnemies = areEnemies;
    }

    public static bool InAttackRange(Unit attacker, Entity target) =>
        target.SurfaceDistance(attacker.Position) - attacker.Type.Radius <= attacker.Type.AttackRange;

    /// <summary>Decides what the unit does this tick: fight, chase, resume its march or go back to its post.</summary>
    public void UpdateBehaviour(Unit unit, IReadOnlyList<Unit> units, IReadOnlyList<Building> buildings, int tick)
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

        // Re-scan even while hitting a building: an enemy soldier showing up takes priority.
        bool scan = (tick + unit.Id) % ScanIntervalTicks == 0;
        if (scan && unit.Order != UnitOrder.AttackUnit && unit.Target is null or Building)
            unit.Target = FindEnemyUnit(unit, units) ?? unit.Target ?? FindEnemyBuilding(unit, buildings);

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

    /// <summary>A finished tower shoots the nearest enemy unit in range.</summary>
    public void UpdateTower(Building tower, IReadOnlyList<Unit> units, int tick)
    {
        if (tower.Type.Attack is not { } attack || !tower.IsComplete)
            return;
        if (tower.AttackCooldownTicks > 0)
            tower.AttackCooldownTicks--;

        if (tower.Target is { } current && (!current.IsAlive || tower.SurfaceDistance(current.Position) > attack.Range))
            tower.Target = null;
        if (tower.Target == null && (tick + tower.Id) % ScanIntervalTicks == 0)
            tower.Target = FindEnemyUnit(tower, units, attack.Range);
        if (tower.Target is not { } target || tower.AttackCooldownTicks > 0)
            return;

        tower.AttackCooldownTicks = CooldownTicks(attack.Cooldown);
        Fire(tower.Id, tower.FactionId, attack.Damage, attack.ProjectileSpeed, tower.Position, target);
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

            float step = p.Speed * dt;
            if (p.Target.SurfaceDistance(p.Position) <= step)
            {
                ApplyDamage(p.Target, p.Damage, ranged: true, p.FactionId);
                _emit(new AttackLanded(p.FactionId, p.SourceId, p.Target.Id, p.Target.FactionId, p.Position, Ranged: true));
                _projectiles.RemoveAt(i);
                continue;
            }
            Vector2 toTarget = p.Target.Position - p.Position;
            p.Position += Vector2.Normalize(toTarget) * step;
        }
    }

    public static void ApplyDamage(Entity target, int damage, bool ranged, int attackerFactionId)
    {
        if (!target.IsAlive)
            return;
        int dealt = ranged ? Math.Max(1, (int)MathF.Round(damage * (1f - target.RangedArmor))) : damage;
        target.Health -= dealt;
        if (!target.IsAlive)
            target.KilledByFactionId = attackerFactionId;
    }

    private bool IsStillValidTarget(Unit unit, Entity target)
    {
        if (!target.IsAlive)
            return false;
        if (unit.Order == UnitOrder.AttackUnit)
            return true; // explicit orders are followed anywhere
        float limit = unit.Type.SightRange * LoseTargetFactor;
        return target.SurfaceDistance(unit.Position) <= limit;
    }

    private Unit? FindEnemyUnit(Unit unit, IReadOnlyList<Unit> units) =>
        FindEnemyUnit(unit, units, unit.Type.SightRange);

    /// <summary>Nearest living unit of another faction within <paramref name="range"/> of the seeker's body.</summary>
    private Unit? FindEnemyUnit(Entity seeker, IReadOnlyList<Unit> units, float range)
    {
        Unit? best = null;
        float bestDistance = float.MaxValue;
        foreach (var other in units)
        {
            if (!_areEnemies(seeker.FactionId, other.FactionId) || !other.IsAlive)
                continue;
            float distance = seeker.SurfaceDistance(other.Position);
            if (distance <= range && distance < bestDistance)
            {
                bestDistance = distance;
                best = other;
            }
        }
        return best;
    }

    private Building? FindEnemyBuilding(Unit unit, IReadOnlyList<Building> buildings)
    {
        Building? best = null;
        float bestDistance = unit.Type.SightRange;
        foreach (var building in buildings)
        {
            if (!_areEnemies(unit.FactionId, building.FactionId) || !building.IsAlive)
                continue;
            float distance = building.SurfaceDistance(unit.Position);
            if (distance <= bestDistance)
            {
                bestDistance = distance;
                best = building;
            }
        }
        return best;
    }

    private void Attack(Unit attacker, Entity target)
    {
        attacker.AttackCooldownTicks = CooldownTicks(attacker.Type.AttackCooldown);
        if (attacker.Type.IsRanged)
        {
            Fire(attacker.Id, attacker.FactionId, attacker.Type.Damage, attacker.Type.ProjectileSpeed, attacker.Position, target);
        }
        else
        {
            ApplyDamage(target, attacker.Type.Damage, ranged: false, attacker.FactionId);
            _emit(new AttackLanded(attacker.FactionId, attacker.Id, target.Id, target.FactionId, target.Position, Ranged: false));
        }
    }

    private void Fire(int sourceId, int factionId, int damage, float speed, Vector2 from, Entity target)
    {
        _projectiles.Add(new Projectile(sourceId, factionId, damage, speed, from, target));
        _emit(new ProjectileFired(factionId, from));
    }

    private static int CooldownTicks(float seconds) => Math.Max(1, (int)MathF.Round(seconds * Simulation.TicksPerSecond));

    private void Chase(Unit unit, Entity target)
    {
        // Buildings are solid: head for the nearest free spot next to them instead of their center.
        Vector2 goal = target.Position;
        if (target is Building)
        {
            int component = _map.ComponentOf(Cell.FromPosition(unit.Position));
            if (_map.FindNearestInComponent(Cell.FromPosition(target.Position), component, ApproachSearchRadius) is not { } spot)
            {
                unit.Target = null;
                return;
            }
            goal = spot.Center;
        }

        if (_map.IsCorridorClear(unit.Position, goal, unit.Type.Radius))
        {
            unit.SetSingleWaypoint(goal);
            unit.ChaseRepathTicks = 0;
            return;
        }
        if (--unit.ChaseRepathTicks > 0 && unit.State == UnitState.Moving)
            return;

        var path = _pathfinder.FindPath(unit.Position, goal, unit.Type.Radius);
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
        if (!_map.IsWalkable(Cell.FromPosition(post)))
        {
            unit.CompleteOrder(); // a building now stands on the old post: guard from here
            return;
        }
        var path = _pathfinder.FindPath(unit.Position, post, unit.Type.Radius);
        if (path == null || path.Count == 0)
        {
            unit.CompleteOrder();
            return;
        }
        unit.StartMove(UnitOrder.Move, post, path);
    }
}

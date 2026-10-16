using System.Numerics;
using Warcrown.Sim.Buildings;
using Warcrown.Sim.Core;
using Warcrown.Sim.Units;
using Warcrown.Sim.World;

namespace Warcrown.Sim.Combat;

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
    /// <summary>How far around a building we look for a free spot to stand and hit it from.</summary>
    private const int ApproachSearchRadius = 6;

    private readonly TileGrid _map;
    private readonly Pathfinder _pathfinder;
    private readonly List<Projectile> _projectiles;
    private readonly Action<SimEvent> _emit;
    private readonly Func<int, int, bool> _areEnemies;
    /// <summary>Units to march on this tick (see <see cref="ResumeMarches"/>).</summary>
    private readonly List<Unit> _resuming = new();

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
        if (!unit.IsAlive)
            return; // fell earlier this tick: it is removed at the end of the tick, and does nothing meanwhile
        if (unit.AttackCooldownTicks > 0)
            unit.AttackCooldownTicks--;
        unit.IsAttacking = false;
        if (unit.Order == UnitOrder.Move)
            return;
        if (unit.Type.Heal is { } heal)
        {
            UpdateHealer(unit, heal, units, tick);
            return;
        }

        if (unit.Target is { } current && !IsStillValidTarget(unit, current))
            unit.Target = null;
        if (unit.Order == UnitOrder.AttackUnit && unit.Target == null)
        {
            unit.CompleteOrder();
            return;
        }
        if (ChasedTooFar(unit))
        {
            ReturnToPost(unit);
            return;
        }

        // Re-scan even while hitting a building: an enemy soldier showing up takes priority.
        bool scan = (tick + unit.Id) % ScanIntervalTicks == 0;
        if (scan && unit.Order != UnitOrder.AttackUnit && unit.Target is null or Building)
            unit.Target = unit.Type.IsSiege
                ? unit.Target ?? FindEnemyBuilding(unit, buildings) // a ram only ever goes for buildings
                : FindEnemyUnit(unit, units) ?? unit.Target ?? FindEnemyBuilding(unit, buildings);

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
        WhenIdle(unit);
    }

    /// <summary>With nothing to fight (or mend): an attack-move marches on, a guard goes back to its post.</summary>
    private void WhenIdle(Unit unit)
    {
        if (unit.Order == UnitOrder.AttackMove && unit.State == UnitState.Idle && unit.Destination is { } destination)
        {
            if (Vector2.Distance(unit.Position, destination) <= Simulation.ArrivalSlack)
            {
                unit.CompleteOrder();
                return;
            }
            _resuming.Add(unit); // the way on is found for all of them at once, after everyone has acted
        }
        else if (unit.Order == UnitOrder.None
                 && (unit.State == UnitState.Moving
                     || Vector2.Distance(unit.Position, unit.GuardPoint) > ReturnToPostDistance))
        {
            ReturnToPost(unit); // the intruder is dead or gone
        }
    }

    /// <summary>
    /// Attack-moving units that have nothing to fight any more march on towards their slots. Units of a group order
    /// follow its route field again (kept from the order, see <see cref="Pathfinder.FindPathViaField"/>) instead of each
    /// searching the way, often a long way round, on its own when a fight ends and the whole army resumes.
    /// </summary>
    public void ResumeMarches()
    {
        foreach (var unit in _resuming)
        {
            var destination = unit.Destination!.Value;
            var path = unit.MarchGoal is { } goal
                ? _pathfinder.FindPathViaField(goal, unit.Position, destination, unit.Type.Radius)
                : _pathfinder.FindPath(unit.Position, destination, unit.Type.Radius);
            if (path == null)
                unit.CompleteOrder();
            else
                unit.ReplacePath(path);
        }
        _resuming.Clear();
    }

    /// <summary>
    /// A monk: walks to the nearest wounded friend in sight (its own troops, allies, and those at peace with it) and
    /// mends it once in reach, again and again until it is whole; then looks for the next. It never fights.
    /// </summary>
    private void UpdateHealer(Unit unit, HealAbility heal, IReadOnlyList<Unit> units, int tick)
    {
        if (unit.Target is { } current && !IsWoundedFriend(unit, current, unit.Type.SightRange * LoseTargetFactor))
            unit.Target = null;
        if (ChasedTooFar(unit))
        {
            ReturnToPost(unit);
            return;
        }
        if (unit.Target == null && (tick + unit.Id) % ScanIntervalTicks == 0)
            unit.Target = FindWoundedFriend(unit, units);
        if (unit.Target is not Unit patient)
        {
            WhenIdle(unit);
            return;
        }
        if (patient.SurfaceDistance(unit.Position) - unit.Type.Radius > heal.Range)
        {
            Chase(unit, patient);
            return;
        }
        unit.ClearPath();
        unit.IsAttacking = true; // standing over the patient (the view shows the blessing)
        if (unit.AttackCooldownTicks > 0)
            return;
        unit.AttackCooldownTicks = Ticks.AtLeastOne(heal.Cooldown);
        int mended = Math.Min(heal.Amount, patient.MaxHealth - patient.Health);
        patient.Health += mended;
        _emit(new UnitHealed(unit.FactionId, unit.Id, patient.Id, mended, patient.Position));
    }

    private bool IsWoundedFriend(Unit healer, Entity other, float range) =>
        other is Unit friend && friend.IsAlive && friend.Id != healer.Id && friend.Health < friend.MaxHealth
        && !_areEnemies(healer.FactionId, friend.FactionId) && friend.SurfaceDistance(healer.Position) <= range;

    private Unit? FindWoundedFriend(Unit healer, IReadOnlyList<Unit> units)
    {
        Unit? best = null;
        float bestDistance = float.MaxValue;
        foreach (var other in units)
        {
            if (!IsWoundedFriend(healer, other, healer.Type.SightRange))
                continue;
            float distance = other.SurfaceDistance(healer.Position);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = other;
            }
        }
        return best;
    }

    /// <summary>A finished tower shoots the nearest enemy unit in range.</summary>
    public void UpdateTower(Building tower, IReadOnlyList<Unit> units, int tick)
    {
        if (tower.Type.Attack is not { } attack || !tower.IsComplete || !tower.IsAlive)
            return;
        if (tower.AttackCooldownTicks > 0)
            tower.AttackCooldownTicks--;

        if (tower.Target is { } current && (!current.IsAlive || tower.SurfaceDistance(current.Position) > attack.Range
                                            || !_areEnemies(tower.FactionId, current.FactionId)))
            tower.Target = null;
        if (tower.Target == null && (tick + tower.Id) % ScanIntervalTicks == 0)
            tower.Target = FindEnemyUnit(tower, units, attack.Range);
        if (tower.Target is not { } target || tower.AttackCooldownTicks > 0)
            return;

        tower.AttackCooldownTicks = Ticks.AtLeastOne(attack.Cooldown);
        Fire(tower.Id, tower.FactionId, attack.Damage, attack.ProjectileSpeed, tower.Position, target, attack.SplashRadius);
    }

    public void AdvanceProjectiles(float dt, IReadOnlyList<Unit> units)
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
                if (!_areEnemies(p.FactionId, p.Target.FactionId))
                {
                    // Peace was made while it flew: it falls harmlessly.
                    _projectiles.RemoveAt(i);
                    continue;
                }
                if (p.SplashRadius > 0f)
                    Splash(p, units);
                else
                {
                    ApplyDamage(p.Target, p.Damage, ranged: true, p.FactionId, p.ArmorPiercing, p.SourceId);
                    _emit(new AttackLanded(p.FactionId, p.SourceId, p.Target.Id, p.Target.FactionId, p.Position, Ranged: true));
                }
                _projectiles.RemoveAt(i);
                continue;
            }
            Vector2 toTarget = p.Target.Position - p.Position;
            p.Position += Vector2.Normalize(toTarget) * step;
        }
    }

    /// <summary>
    /// A stone lands on its target: every living enemy unit whose body is within the splash radius of the target
    /// takes the full damage (reduced by ranged armour, like any missile). Friends and buildings are spared.
    /// </summary>
    private void Splash(Projectile p, IReadOnlyList<Unit> units)
    {
        var impact = p.Target.Position;
        _emit(new SplashLanded(p.FactionId, p.SourceId, impact, p.SplashRadius));
        foreach (var unit in units)
        {
            if (!unit.IsAlive || !_areEnemies(p.FactionId, unit.FactionId) || unit.SurfaceDistance(impact) > p.SplashRadius)
                continue;
            ApplyDamage(unit, p.Damage, ranged: true, p.FactionId, attackerId: p.SourceId);
            _emit(new AttackLanded(p.FactionId, p.SourceId, unit.Id, unit.FactionId, unit.Position, Ranged: true, Splash: true));
        }
    }

    /// <param name="armorPiercing">Share of the target's ranged armour the missile ignores.</param>
    /// <param name="attackerId">The unit or building that struck (for the killer's experience).</param>
    public static void ApplyDamage(Entity target, int damage, bool ranged, int attackerFactionId, float armorPiercing = 0f, int attackerId = -1)
    {
        if (!target.IsAlive)
            return;
        float armor = target.RangedArmor * (1f - armorPiercing);
        int dealt = ranged ? Math.Max(1, (int)MathF.Round(damage * (1f - armor))) : damage;
        target.Health -= dealt;
        if (!target.IsAlive)
        {
            target.KilledByFactionId = attackerFactionId;
            target.KilledById = attackerId;
        }
    }

    private bool IsStillValidTarget(Unit unit, Entity target)
    {
        if (!target.IsAlive || !_areEnemies(unit.FactionId, target.FactionId))
            return false; // dead, or made peace with
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
        attacker.AttackCooldownTicks = Ticks.AtLeastOne(attacker.Type.AttackCooldown);
        int damage = attacker.DamageAgainst(target);
        if (attacker.Type.IsRanged)
        {
            Fire(attacker.Id, attacker.FactionId, damage, attacker.Type.ProjectileSpeed, attacker.Position, target,
                armorPiercing: attacker.Type.ArmorPiercing, isStone: attacker.Type.IsSiege);
        }
        else
        {
            ApplyDamage(target, damage, ranged: false, attacker.FactionId, attackerId: attacker.Id);
            _emit(new AttackLanded(attacker.FactionId, attacker.Id, target.Id, target.FactionId, target.Position, Ranged: false));
        }
    }

    private void Fire(int sourceId, int factionId, int damage, float speed, Vector2 from, Entity target, float splashRadius = 0f,
        float armorPiercing = 0f, bool isStone = false)
    {
        var projectile = new Projectile(sourceId, factionId, damage, speed, from, target, splashRadius, armorPiercing, isStone);
        _projectiles.Add(projectile);
        _emit(new ProjectileFired(factionId, from, Splash: projectile.IsStone));
    }

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

    /// <summary>A guard (no orders) that has followed a target beyond its leash.</summary>
    private static bool ChasedTooFar(Unit unit) =>
        unit.Order == UnitOrder.None && unit.Target != null && Vector2.Distance(unit.Position, unit.GuardPoint) > LeashRange;

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

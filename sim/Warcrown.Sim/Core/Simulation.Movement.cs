using System.Numerics;
using Warcrown.Sim.Buildings;
using Warcrown.Sim.Units;
using Warcrown.Sim.World;

namespace Warcrown.Sim.Core;

/// <summary>Move, attack and rally orders; group marches in formation and what happens when units get stuck.</summary>
public sealed partial class Simulation
{
    /// <summary>How far from a blocked click we look for a reachable replacement point.</summary>
    private const int MaxGoalSearchRadius = 64;

    // Stuck handling: every second a marching unit must cover a fraction of its speed.
    private const int ProgressCheckTicks = TicksPerSecond;
    private const float MinProgressFraction = 0.25f;
    private const int RepathAfterChecks = 2;
    private const int GiveUpAfterChecks = 5;
    /// <summary>A unit stuck this close to its slot (crowded by neighbours) simply counts as arrived.</summary>
    internal const float ArrivalSlack = 1.5f;
    /// <summary>Groups at least this big share one route search (see <see cref="Pathfinder.BuildField"/>).</summary>
    internal const int SharedRouteGroupSize = 6;

    /// <summary>
    /// Units that died or belong to someone else are silently skipped: the issuer's view of the world
    /// may be a tick out of date, and foreign units are not theirs to command.
    /// </summary>
    private IEnumerable<Unit> OwnLivingUnits(int factionId, IReadOnlyList<int> unitIds)
    {
        foreach (int id in unitIds.Distinct())
        {
            if (GetUnit(id) is { IsAlive: true } unit && unit.FactionId == factionId)
                yield return unit;
        }
    }

    private void ApplyMove(MoveCommand move)
    {
        // Units are grouped by walkable region, since each region resolves a blocked target differently.
        var groups = new SortedDictionary<int, List<Unit>>();
        foreach (var unit in OwnLivingUnits(move.FactionId, move.UnitIds))
        {
            int component = Map.ComponentOf(Cell.FromPosition(unit.Position));
            if (!groups.TryGetValue(component, out var list))
                groups[component] = list = new List<Unit>();
            list.Add(unit);
        }

        var order = move.Mode == MoveMode.AttackMove ? UnitOrder.AttackMove : UnitOrder.Move;
        foreach (var (component, group) in groups)
            OrderGroupMove(group, component, move.Target, order);
    }

    private void ApplyAttack(AttackCommand attack)
    {
        // Monks do not fight: an order to attack leaves them mending.
        attack = attack with { UnitIds = attack.UnitIds.Where(id => GetUnit(id) is not { Type.IsHealer: true }).ToArray() };
        if (GetEntity(attack.TargetId) is not { IsAlive: true } target || !AreEnemies(attack.FactionId, target.FactionId))
            return;
        // A ram cannot fight soldiers: sent at one, it advances with the army and goes for the buildings it meets.
        var rams = OwnLivingUnits(attack.FactionId, attack.UnitIds).Where(u => u.Type.IsSiege && target is not Building).Select(u => u.Id).ToList();
        if (rams.Count > 0)
            ApplyMove(new MoveCommand(attack.FactionId, rams, target.Position, MoveMode.AttackMove));
        foreach (var unit in OwnLivingUnits(attack.FactionId, attack.UnitIds))
        {
            if (unit.Type.IsSiege && target is not Building)
                continue;
            unit.StartAttack(target);
            unit.LastOrder = MoveOrderResult.Accepted;
        }
    }

    private void ApplyRally(SetRallyCommand rally)
    {
        if (GetBuilding(rally.BuildingId) is not { IsAlive: true } building || building.FactionId != rally.FactionId
            || building.Type.Recruits.Count == 0)
            return;
        building.RallyPoint = rally.Point is { } p
            ? new Vector2(Math.Clamp(p.X, 0.5f, Map.Width - 0.5f), Math.Clamp(p.Y, 0.5f, Map.Height - 0.5f))
            : null;
    }

    /// <summary>A recruit fresh out of a building with a rally point walks there.</summary>
    private void SendToRally(Unit unit, Building building)
    {
        if (building.RallyPoint is not { } rally)
            return;
        OrderGroupMove(new List<Unit> { unit }, Map.ComponentOf(Cell.FromPosition(unit.Position)), rally, UnitOrder.Move);
    }

    private void OrderGroupMove(List<Unit> group, int component, Vector2 target, UnitOrder order)
    {
        Cell requested = Cell.FromPosition(target);
        Cell? goal = Map.FindNearestInComponent(requested, component, MaxGoalSearchRadius);
        if (goal == null)
        {
            foreach (var unit in group)
            {
                unit.CompleteOrder();
                unit.LastOrder = MoveOrderResult.Unreachable;
            }
            return;
        }

        bool adjusted = goal.Value != requested;
        Vector2 center = adjusted ? goal.Value.Center : target;
        float clearance = group.Max(u => u.Type.Radius);
        var slots = Formation.ComputeSlots(Map, center, group.Count, clearance);

        // Closest units take the central slots; the rest fill the outer rings.
        group.Sort((a, b) =>
        {
            int byDistance = Vector2.DistanceSquared(a.Position, center).CompareTo(Vector2.DistanceSquared(b.Position, center));
            return byDistance != 0 ? byDistance : a.Id.CompareTo(b.Id);
        });

        Cell? shared = group.Count >= SharedRouteGroupSize ? Cell.FromPosition(center) : null;
        for (int i = 0; i < group.Count; i++)
        {
            var unit = group[i];
            unit.MarchGoal = shared;
            var path = shared is { } goalCell
                ? _pathfinder.FindPathViaField(goalCell, unit.Position, slots[i], unit.Type.Radius)
                : _pathfinder.FindPath(unit.Position, slots[i], unit.Type.Radius);
            if (path == null)
            {
                unit.CompleteOrder();
                unit.LastOrder = MoveOrderResult.Unreachable;
                continue;
            }
            unit.StartMove(order, slots[i], path);
            unit.LastOrder = adjusted ? MoveOrderResult.Adjusted : MoveOrderResult.Accepted;
        }
    }


    /// <summary>Undoes pushes into walls, buildings, the map edge or another walkable region.</summary>
    private void KeepOnWalkableGround(Unit unit)
    {
        Vector2 pos = Map.PushOutOfWalls(unit.Position, unit.Type.Radius);
        var cell = Cell.FromPosition(pos);
        var previousCell = Cell.FromPosition(unit.PreviousPosition);
        if (!Map.IsWalkable(cell) || Map.ComponentOf(cell) != Map.ComponentOf(previousCell))
            pos = unit.PreviousPosition;
        unit.Position = pos;
    }

    /// <summary>A march whose path has run out (and that is not busy fighting) is done.</summary>
    private static void CompleteFinishedOrder(Unit unit)
    {
        if (unit.State != UnitState.Idle || unit.Target != null)
            return;
        if (unit.Order == UnitOrder.Move)
            unit.CompleteOrder();
        else if (unit.Order == UnitOrder.AttackMove && unit.Destination is { } d
                 && Vector2.Distance(unit.Position, d) <= ArrivalSlack)
            unit.CompleteOrder();
        // An attack-move that ended a chase away from its slot resumes next tick.
    }

    private void CheckProgress(Unit unit)
    {
        if (unit.State != UnitState.Moving || unit.Target != null || unit.Destination is not { } destination)
            return;
        if (++unit.TicksSinceProgressCheck < ProgressCheckTicks)
            return;

        float moved = Vector2.Distance(unit.Position, unit.ProgressAnchor);
        float expected = unit.Type.Speed * ProgressCheckTicks * TickDuration;
        unit.StuckChecks = moved < expected * MinProgressFraction ? unit.StuckChecks + 1 : 0;
        unit.TicksSinceProgressCheck = 0;
        unit.ProgressAnchor = unit.Position;
        if (unit.StuckChecks == 0)
            return;

        bool nearSlot = Vector2.Distance(unit.Position, destination) <= ArrivalSlack;
        if (unit.StuckChecks >= RepathAfterChecks && nearSlot)
        {
            unit.CompleteOrder(); // close enough: the slot is crowded, stand where we are
            return;
        }
        if (unit.StuckChecks >= GiveUpAfterChecks)
        {
            unit.CompleteOrder();
            unit.LastOrder = MoveOrderResult.Blocked;
            return;
        }
        if (unit.StuckChecks == RepathAfterChecks)
            Repath(unit, destination);
    }

    private void Repath(Unit unit, Vector2 destination)
    {
        // The slot itself may now be under a new building: aim for the nearest free spot instead.
        int component = Map.ComponentOf(Cell.FromPosition(unit.Position));
        if (Map.FindNearestInComponent(Cell.FromPosition(destination), component, MaxGoalSearchRadius) is not { } goal)
        {
            unit.CompleteOrder();
            unit.LastOrder = MoveOrderResult.Blocked;
            return;
        }
        var target = goal == Cell.FromPosition(destination) ? destination : goal.Center;
        var path = _pathfinder.FindPath(unit.Position, target, unit.Type.Radius);
        if (path == null)
        {
            unit.CompleteOrder();
            unit.LastOrder = MoveOrderResult.Blocked;
            return;
        }
        unit.ReplacePath(path);
    }
}

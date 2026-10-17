using System.Numerics;
using Warcrown.Sim.Buildings;
using Warcrown.Sim.Units;
using Warcrown.Sim.World;

namespace Warcrown.Sim.Core;

/// <summary>Distant construction: buildings paid for whose builders are still walking over (see <see cref="BuildPlan"/>).</summary>
public sealed partial class Simulation
{
    private readonly List<BuildPlan> _plans = new();
    private int _nextPlanId = 1;

    /// <summary>Buildings paid for whose builders are on their way.</summary>
    public IReadOnlyList<BuildPlan> BuildPlans => _plans;

    /// <summary>
    /// The soldier who would walk over to build there: the nearest of the faction's units that can reach the place
    /// (or fly there) and has not been sent to build already, preferring those not fighting; never a siege engine. Null if none.
    /// </summary>
    public Unit? FindBuilder(int factionId, string buildingTypeId, Cell anchor)
    {
        if (!Rules.HasBuildingType(buildingTypeId))
            return null;
        var type = Rules.GetBuildingType(buildingTypeId);
        var center = Building.CenterOf(anchor, type.Size);
        int component = Map.ComponentOf(Cell.FromPosition(center));
        if (component < 0)
            return null;
        Unit? best = null;
        (bool Busy, float Distance) bestKey = (true, float.MaxValue);
        foreach (var unit in _units)
        {
            if (unit.FactionId != factionId || !unit.IsAlive || unit.Type.IsSiege
                || !unit.Type.IsFlying && Map.ComponentOf(Cell.FromPosition(unit.Position)) != component
                || IsBuilder(unit.Id))
                continue;
            var key = (unit.Target != null, Vector2.Distance(unit.Position, center));
            if (key.Item1 != bestKey.Busy ? !key.Item1 : key.Item2 < bestKey.Distance)
            {
                best = unit;
                bestKey = key;
            }
        }
        return best;
    }

    private bool IsBuilder(int unitId)
    {
        foreach (var p in _plans)
        {
            if (p.BuilderId == unitId)
                return true;
        }
        return false;
    }

    /// <summary>Whether the footprint overlaps a building the faction has already sent someone to put up.</summary>
    private bool OverlapsPlan(int factionId, Cell anchor, int size)
    {
        foreach (var p in _plans)
        {
            if (p.FactionId == factionId
                && anchor.X < p.Anchor.X + p.Type.Size && p.Anchor.X < anchor.X + size
                && anchor.Y < p.Anchor.Y + p.Type.Size && p.Anchor.Y < anchor.Y + size)
                return true;
        }
        return false;
    }

    private int PlannedCount(int factionId, string buildingTypeId)
    {
        int count = 0;
        foreach (var p in _plans)
        {
            if (p.FactionId == factionId && p.Type.Id == buildingTypeId)
                count++;
        }
        return count;
    }

    /// <summary>A build order with nobody near: pay, send the nearest free soldier, and lay out the site when he arrives.</summary>
    private bool TryPlanBuild(BuildCommand build)
    {
        var faction = GetFaction(build.FactionId)!;
        var type = Rules.GetBuildingType(build.BuildingTypeId);
        if (FindBuilder(build.FactionId, type.Id, build.Anchor) is not { } builder)
            return false;
        if (!faction.Treasury.TrySpend(BuildCost(type)))
        {
            _events.Add(new BuildRejected(build.FactionId, build.BuildingTypeId, BuildError.NotEnoughGold));
            return true;
        }
        var center = Building.CenterOf(build.Anchor, type.Size);
        OrderGroupMove(new List<Unit> { builder }, Map.ComponentOf(Cell.FromPosition(builder.Position)), center, UnitOrder.Move);
        var plan = new BuildPlan(_nextPlanId++, build.FactionId, type, build.Anchor, builder.Id, BuildCost(type), builder.Destination);
        _plans.Add(plan);
        _events.Add(new BuildPlanned(build.FactionId, plan.Id, type.Id, builder.Id));
        return true;
    }

    /// <summary>Builders that have arrived lay out their sites; orders that can no longer be carried out are dropped.</summary>
    private void UpdatePlans()
    {
        for (int i = 0; i < _plans.Count; i++)
        {
            var plan = _plans[i];
            var builder = GetUnit(plan.BuilderId);
            _plans.RemoveAt(i); // out of the way of its own placement check
            var error = CanBuild(plan.FactionId, plan.Type.Id, plan.Anchor, checkGold: false);
            if (error == BuildError.None)
            {
                PlaceSite(plan.Type, plan.FactionId, plan.Anchor).Invested = plan.Paid;
                // The builder's walk ends where he stands: the site needs nobody, and its middle is now walls.
                if (builder is { IsAlive: true } && builder.Destination == plan.Goal)
                    builder.CompleteOrder();
                i--;
                continue;
            }
            bool stillOnTheWay = error == BuildError.NoUnitsNearby && builder is { IsAlive: true } walker
                                 && walker.FactionId == plan.FactionId && walker.State == UnitState.Moving
                                 && walker.Destination == plan.Goal;
            if (stillOnTheWay)
            {
                _plans.Insert(i, plan);
                continue;
            }
            GetFaction(plan.FactionId)?.Treasury.Add(plan.Paid);
            _events.Add(new BuildPlanCancelled(plan.FactionId, plan.Id, plan.Type.Id, plan.Paid, error));
            i--;
        }
    }

    private void HashPlans(ref Fnv hash)
    {
        foreach (var plan in _plans)
        {
            hash.Add(plan.Id);
            hash.Add(plan.BuilderId);
            hash.Add(plan.Anchor.X);
            hash.Add(plan.Anchor.Y);
        }
    }
}

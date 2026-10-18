using System.Numerics;
using Warcrown.Sim.Buildings;
using Warcrown.Sim.Units;
using Warcrown.Sim.World;

namespace Warcrown.Sim.Core;

/// <summary>Construction progress, recruiting and training queues, and where recruits appear.</summary>
public sealed partial class Simulation
{
    /// <summary>Recruits and starting units appear this far in front of (below) their building's wall.</summary>
    private const float SpawnDistanceFromWall = 0.8f;

    /// <summary>
    /// Whether a <see cref="RecruitCommand"/> would currently succeed. The same check the command runs,
    /// exposed so the UI can mark buttons and bots can plan without guessing the rules.
    /// </summary>
    public RecruitError CanRecruit(int factionId, int buildingId, string unitTypeId)
    {
        if (GetBuilding(buildingId) is not { } building)
            return RecruitError.UnknownBuilding;
        if (building.FactionId != factionId || GetFaction(factionId) is not { IsEliminated: false } faction)
            return RecruitError.NotOwner;
        if (!building.IsComplete)
            return RecruitError.NotComplete;
        if (!building.Type.Recruits.Contains(unitTypeId))
            return RecruitError.CannotRecruitType;
        var type = UnitTypeFor(factionId, unitTypeId);
        if (!IsUnlocked(factionId, type))
            return RecruitError.Locked;
        if (PopulationOf(factionId) >= faction.ArmyCap)
            return RecruitError.ArmyCapReached;
        if (!faction.Treasury.CanAfford(type.Cost))
            return RecruitError.NotEnoughGold;
        return RecruitError.None;
    }

    /// <summary>
    /// How many steps of units the faction has opened: one per finished barracks (any building that unlocks units)
    /// still standing. Lose them and the steps close again.
    /// </summary>
    public int UnlockLevel(int factionId)
    {
        int level = 0;
        foreach (var b in _buildings)
        {
            if (b.FactionId == factionId && b.IsAlive && b.IsComplete && b.Type.UnlocksUnits)
                level++;
        }
        return level;
    }

    /// <summary>Whether the faction may train this kind now: enough barracks, and its doctrine if the kind has one.</summary>
    public bool IsUnlocked(int factionId, UnitType type) =>
        type.BarracksRequired <= UnlockLevel(factionId)
        && (type.Doctrine == null || GetFaction(factionId)?.Doctrine == type.Doctrine);

    private void ApplyRecruit(RecruitCommand recruit)
    {
        var error = Rules.Units.Contains(recruit.UnitTypeId)
            ? CanRecruit(recruit.FactionId, recruit.BuildingId, recruit.UnitTypeId)
            : RecruitError.CannotRecruitType;
        if (error != RecruitError.None)
        {
            _events.Add(new RecruitRejected(recruit.FactionId, recruit.BuildingId, recruit.UnitTypeId, error));
            return;
        }

        var type = UnitTypeFor(recruit.FactionId, recruit.UnitTypeId);
        var faction = GetFaction(recruit.FactionId)!;
        faction.Treasury.TrySpend(type.Cost);
        faction.Population++;
        int ticks = Ticks.AtLeastOne(type.BuildTime);
        GetBuilding(recruit.BuildingId)!.Enqueue(new ProductionItem(type, ticks));
    }

    /// <summary>How many recruits a building trains at once: one, and one more per finished barracks of its owner.</summary>
    public int TrainingSlots(Building building) => 1 + UnlockLevel(building.FactionId);

    private readonly List<UnitType> _finished = new();
    /// <summary>Training places per faction this tick (reused, filled as buildings come up).</summary>
    private readonly Dictionary<int, int> _trainingSlots = new();

    private void AdvanceConstructionAndTraining()
    {
        var slots = _trainingSlots;
        slots.Clear();
        foreach (var building in _buildings)
        {
            if (!building.IsComplete)
            {
                if (building.AdvanceConstruction())
                    _events.Add(new BuildingCompleted(building.FactionId, building.Id));
                continue;
            }
            if (building.AdvanceUpgrade())
                _events.Add(new BuildingUpgraded(building.FactionId, building.Id, building.Type.Tier));
            if (building.Queue.Count == 0)
                continue;
            if (!slots.TryGetValue(building.FactionId, out int count))
                slots[building.FactionId] = count = TrainingSlots(building);
            _finished.Clear();
            building.AdvanceProduction(count, _finished);
            foreach (var type in _finished)
            {
                var unit = SpawnUnit(type, building.FactionId, FreeSpotNear(building, type.Radius)); // as it was paid for
                SendToRally(unit, building);
                _events.Add(new UnitRecruited(building.FactionId, building.Id, unit.Id));
            }
        }
    }

    /// <summary>A walkable point just in front of (below) the building's footprint.</summary>
    private Vector2 SpawnPointOf(Building building)
    {
        var front = building.Position + new Vector2(0f, building.Type.Size / 2f + SpawnDistanceFromWall);
        if (Map.IsWalkable(Cell.FromPosition(front)))
            return front;
        // Water or another building right below: use the closest open ground instead.
        return Map.FindNearestInComponent(Cell.FromPosition(front), TileGrid.AnyComponent, 16)?.Center
               ?? throw new InvalidOperationException($"No open ground around {building.Type.Name} at {building.Anchor}.");
    }

    /// <summary>First formation slot in front of the building that no unit is standing on.</summary>
    private Vector2 FreeSpotNear(Building building, float radius)
    {
        var slots = Formation.ComputeSlots(Map, SpawnPointOf(building), 32, radius);
        foreach (var slot in slots)
        {
            if (!IsStoodOn(slot, radius))
                return slot;
        }
        return slots[0]; // crowded: separation will make room
    }

    /// <summary>Whether a body of <paramref name="radius"/> at the point would overlap any unit.</summary>
    private bool IsStoodOn(Vector2 point, float radius)
    {
        foreach (var u in _units)
        {
            if (Vector2.DistanceSquared(u.Position, point) < MathF.Pow(u.Type.Radius + radius, 2))
                return true;
        }
        return false;
    }
}

using System.Numerics;
using Lordsio.Sim.Buildings;
using Lordsio.Sim.Units;
using Lordsio.Sim.World;

namespace Lordsio.Sim.Core;

/// <summary>Placing buildings (the rules, capitals at the start, making room), and upgrading them.</summary>
public sealed partial class Simulation
{
    /// <summary>Standing buildings of one kind a faction has, construction sites included.</summary>
    public int CountBuildings(int factionId, string buildingTypeId)
    {
        int count = 0;
        foreach (var b in _buildings)
        {
            if (b.FactionId == factionId && b.IsAlive && b.Type.Id == buildingTypeId)
                count++;
        }
        return count;
    }

    /// <summary>
    /// Whether a <see cref="BuildCommand"/> would currently succeed: the same check the command runs,
    /// exposed so the UI can colour the placement ghost and bots can plan without guessing the rules.
    /// </summary>
    public BuildError CanBuild(int factionId, string buildingTypeId, Cell anchor) => CanBuild(factionId, buildingTypeId, anchor, checkGold: true);

    /// <param name="checkGold">False for a building already paid for (a builder arriving at a planned site).</param>
    private BuildError CanBuild(int factionId, string buildingTypeId, Cell anchor, bool checkGold)
    {
        if (!Rules.HasBuildingType(buildingTypeId))
            return BuildError.UnknownType;
        var type = Rules.GetBuildingType(buildingTypeId);
        if (!(type.IsBuildable || MayBuildOutpost(factionId, type)) || GetFaction(factionId) is not { IsEliminated: false } faction)
            return BuildError.NotBuildable;
        if (type.MaxCount > 0 && CountBuildings(factionId, type.Id) + PlannedCount(factionId, type.Id) >= type.MaxCount)
            return BuildError.LimitReached;
        if (OverlapsPlan(factionId, anchor, type.Size))
            return BuildError.Occupied;

        for (int y = 0; y < type.Size; y++)
        {
            for (int x = 0; x < type.Size; x++)
            {
                var cell = new Cell(anchor.X + x, anchor.Y + y);
                if (!Map.InBounds(cell))
                    return BuildError.OutOfBounds;
                if (!TerrainInfo.IsBuildable(Map.GetTerrain(cell)))
                    return BuildError.BlockedTerrain;
                if (Map.IsOccupied(cell))
                    return BuildError.Occupied;
            }
        }

        if (type.IsMine)
        {
            if (DepositAt(anchor) is not { } deposit || deposit.Anchor != anchor)
                return BuildError.NeedsDeposit;
        }
        else if (TouchesDeposit(anchor, type.Size))
        {
            return BuildError.OnDeposit;
        }

        if (!HasUnitNear(factionId, Building.CenterOf(anchor, type.Size), Rules.BuildRadius))
            return BuildError.NoUnitsNearby;

        if (checkGold && !faction.Treasury.CanAfford(BuildCost(type)))
            return BuildError.NotEnoughGold;
        return BuildError.None;
    }

    public GoldDeposit? DepositAt(Cell cell)
    {
        foreach (var deposit in _deposits)
        {
            if (deposit.Contains(cell))
                return deposit;
        }
        return null;
    }

    /// <summary>Whether any cell of the square overlaps a gold deposit.</summary>
    private bool TouchesDeposit(Cell anchor, int size)
    {
        foreach (var deposit in _deposits)
        {
            bool apart = anchor.X + size <= deposit.Anchor.X || deposit.Anchor.X + GoldDeposit.Size <= anchor.X
                      || anchor.Y + size <= deposit.Anchor.Y || deposit.Anchor.Y + GoldDeposit.Size <= anchor.Y;
            if (!apart)
                return true;
        }
        return false;
    }

    /// <summary>Whether a living unit of the faction stands within <paramref name="radius"/> of the point.</summary>
    private bool HasUnitNear(int factionId, Vector2 point, float radius)
    {
        float radiusSq = radius * radius;
        foreach (var u in _units)
        {
            if (u.FactionId == factionId && u.IsAlive && Vector2.DistanceSquared(u.Position, point) <= radiusSq)
                return true;
        }
        return false;
    }

    private void PlaceCapitals(IReadOnlyList<Cell> starts, MatchSetup setup)
    {
        if (setup.FactionCount > starts.Count)
            throw new ArgumentException($"The map has {starts.Count} start positions, not enough for {setup.FactionCount} factions.");

        var order = starts.ToList();
        if (setup.ShuffleStarts)
        {
            var rng = new Random(setup.Seed);
            for (int i = order.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }
        }

        var capitalType = Rules.Capital;
        for (int factionId = 0; factionId < setup.FactionCount; factionId++)
        {
            var anchor = order[factionId];
            foreach (var cell in Building.FootprintOf(anchor, capitalType.Size))
            {
                if (!Map.IsWalkable(cell) || DepositAt(cell) != null)
                    throw new ArgumentException($"A {capitalType.Name} does not fit at start position {anchor}.");
            }
            var capital = AddBuilding(capitalType, factionId, anchor, constructionTicks: 0);
            GetFaction(factionId)!.CapitalId = capital.Id;

            foreach (var group in Rules.StartingUnits)
            {
                var type = Rules.Units.Get(group.UnitTypeId);
                foreach (var slot in Formation.ComputeSlots(Map, SpawnPointOf(capital), group.Count, type.Radius))
                    SpawnUnit(type.Id, factionId, slot);
            }
        }
    }

    private Building AddBuilding(BuildingType type, int factionId, Cell anchor, int constructionTicks)
    {
        var deposit = type.IsMine ? DepositAt(anchor) : null;
        var building = new Building(_nextEntityId++, type, factionId, anchor, constructionTicks, deposit) { Invested = BuildCost(type) };
        _buildings.Add(building);
        _entitiesById.Add(building.Id, building);
        if (deposit != null)
            deposit.MineId = building.Id;
        Map.SetOccupied(building.Anchor, type.Size, true);
        MakeRoomFor(building);
        return building;
    }

    private void ApplyBuild(BuildCommand build)
    {
        var error = CanBuild(build.FactionId, build.BuildingTypeId, build.Anchor);
        // Nobody near: the nearest free soldier is sent to build it.
        if (error == BuildError.NoUnitsNearby && TryPlanBuild(build))
            return;
        if (error != BuildError.None)
        {
            _events.Add(new BuildRejected(build.FactionId, build.BuildingTypeId, error));
            return;
        }

        var type = Rules.GetBuildingType(build.BuildingTypeId);
        GetFaction(build.FactionId)!.Treasury.TrySpend(BuildCost(type));
        PlaceSite(type, build.FactionId, build.Anchor);
    }

    /// <summary>Lays out a construction site that has been paid for, and tells the views.</summary>
    private Building PlaceSite(BuildingType type, int factionId, Cell anchor)
    {
        var building = AddBuilding(type, factionId, anchor, Ticks.AtLeastOne(BuildSeconds(type)));
        if (type.IsCapital)
            GetFaction(factionId)!.CapitalId = building.Id; // a new outpost (Defense)
        _events.Add(new BuildingPlaced(factionId, building.Id));
        return building;
    }

    /// <summary>Units standing on a new footprint step aside; routes through it are planned again.</summary>
    private void MakeRoomFor(Building building)
    {
        foreach (var unit in _units)
        {
            if (building.Covers(Cell.FromPosition(unit.Position))
                && Map.FindNearestInComponent(Cell.FromPosition(unit.Position), TileGrid.AnyComponent, 8) is { } free)
            {
                unit.Position = free.Center;
                unit.PreviousPosition = free.Center;
            }

            if (unit.Target != null)
                unit.ChaseRepathTicks = 0; // chase routes are refreshed next tick
            else if (unit.State == UnitState.Moving && unit.Destination is { } destination && PathCrosses(unit, building))
                Repath(unit, destination);
        }
    }

    private static bool PathCrosses(Unit unit, Building building)
    {
        Vector2 from = unit.Position;
        for (int i = unit.PathIndex; i < unit.Path.Count; i++)
        {
            Vector2 to = unit.Path[i];
            // Sample the segment at quarter-tile steps; footprints are at least a tile wide.
            int steps = Math.Max(1, (int)(Vector2.Distance(from, to) * 4));
            for (int s = 0; s <= steps; s++)
            {
                if (building.Covers(Cell.FromPosition(Vector2.Lerp(from, to, (float)s / steps))))
                    return true;
            }
            from = to;
        }
        return false;
    }

    /// <summary>
    /// Whether an <see cref="UpgradeCommand"/> would currently succeed; the same check the command runs,
    /// for the UI and bots.
    /// </summary>
    public UpgradeError CanUpgrade(int factionId, int buildingId)
    {
        if (GetBuilding(buildingId) is not { IsAlive: true } building)
            return UpgradeError.UnknownBuilding;
        if (building.FactionId != factionId || GetFaction(factionId) is not { IsEliminated: false } faction)
            return UpgradeError.NotOwner;
        if (!building.IsComplete)
            return UpgradeError.NotComplete;
        if (building.IsUpgrading)
            return UpgradeError.InProgress;
        if (building.Type.NextTier == null)
            return UpgradeError.MaxTier;
        if (!faction.Treasury.CanAfford(building.Type.UpgradeCost))
            return UpgradeError.NotEnoughGold;
        return UpgradeError.None;
    }

    private void ApplyUpgrade(UpgradeCommand upgrade)
    {
        var error = CanUpgrade(upgrade.FactionId, upgrade.BuildingId);
        if (error != UpgradeError.None)
        {
            _events.Add(new UpgradeRejected(upgrade.FactionId, upgrade.BuildingId, error));
            return;
        }
        var building = GetBuilding(upgrade.BuildingId)!;
        GetFaction(upgrade.FactionId)!.Treasury.TrySpend(building.Type.UpgradeCost);
        building.Invested += building.Type.UpgradeCost;
        building.StartUpgrade(Ticks.FromSeconds(building.Type.UpgradeTime));
        _events.Add(new BuildingUpgradeStarted(upgrade.FactionId, building.Id));
    }
}

using System.Numerics;
using Lordsio.Sim.Buildings;
using Lordsio.Sim.Units;
using Lordsio.Sim.World;

namespace Lordsio.Sim.Core;

/// <summary>Buildings, construction, the economy, training, destruction and victory.</summary>
public sealed partial class Simulation
{
    /// <summary>Recruits and starting units appear this far in front of (below) their building's wall.</summary>
    private const float SpawnDistanceFromWall = 0.8f;

    /// <summary>Standing buildings of one kind a faction has, construction sites included.</summary>
    public int CountBuildings(int factionId, string buildingTypeId) =>
        _buildings.Count(b => b.FactionId == factionId && b.IsAlive && b.Type.Id == buildingTypeId);

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
        if (!type.IsBuildable || GetFaction(factionId) is not { IsEliminated: false } faction)
            return BuildError.NotBuildable;
        if (type.MaxCount > 0 && CountBuildings(factionId, type.Id) + PlannedCount(factionId, type.Id) >= type.MaxCount)
            return BuildError.LimitReached;
        if (OverlapsPlan(factionId, anchor, type.Size))
            return BuildError.Occupied;

        foreach (var cell in Building.FootprintOf(anchor, type.Size))
        {
            if (!Map.InBounds(cell))
                return BuildError.OutOfBounds;
            if (!TerrainInfo.IsBuildable(Map.GetTerrain(cell)))
                return BuildError.BlockedTerrain;
            if (Map.IsOccupied(cell))
                return BuildError.Occupied;
        }

        if (type.IsMine)
        {
            if (DepositAt(anchor) is not { } deposit || deposit.Anchor != anchor)
                return BuildError.NeedsDeposit;
        }
        else if (Building.FootprintOf(anchor, type.Size).Any(c => DepositAt(c) != null))
        {
            return BuildError.OnDeposit;
        }

        Vector2 center = Building.CenterOf(anchor, type.Size);
        float radiusSq = Rules.BuildRadius * Rules.BuildRadius;
        if (!_units.Any(u => u.FactionId == factionId && u.IsAlive && Vector2.DistanceSquared(u.Position, center) <= radiusSq))
            return BuildError.NoUnitsNearby;

        if (checkGold && !faction.Treasury.CanAfford(type.Cost))
            return BuildError.NotEnoughGold;
        return BuildError.None;
    }

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
        if (!IsUnlocked(factionId, Rules.Units.Get(unitTypeId)))
            return RecruitError.Locked;
        if (PopulationOf(factionId) >= faction.ArmyCap)
            return RecruitError.ArmyCapReached;
        if (!faction.Treasury.CanAfford(Rules.Units.Get(unitTypeId).Cost))
            return RecruitError.NotEnoughGold;
        return RecruitError.None;
    }

    /// <summary>
    /// How many steps of units the faction has opened: one per finished barracks (any building that unlocks units)
    /// still standing. Lose them and the steps close again.
    /// </summary>
    public int UnlockLevel(int factionId) =>
        _buildings.Count(b => b.FactionId == factionId && b.IsAlive && b.IsComplete && b.Type.UnlocksUnits);

    /// <summary>Whether the faction may train this kind now.</summary>
    public bool IsUnlocked(int factionId, UnitType type) => type.BarracksRequired <= UnlockLevel(factionId);

    public GoldDeposit? DepositAt(Cell cell) => _deposits.Find(d => d.Contains(cell));

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
        var building = new Building(_nextEntityId++, type, factionId, anchor, constructionTicks, deposit) { Invested = type.Cost };
        _buildings.Add(building);
        _entitiesById.Add(building.Id, building);
        if (deposit != null)
            deposit.MineId = building.Id;
        Map.SetOccupied(building.Footprint, true);
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
        GetFaction(build.FactionId)!.Treasury.TrySpend(type.Cost);
        int ticks = Math.Max(1, (int)MathF.Round(type.BuildTime * TicksPerSecond));
        var building = AddBuilding(type, build.FactionId, build.Anchor, ticks);
        _events.Add(new BuildingPlaced(build.FactionId, building.Id));
    }

    /// <summary>Units standing on a new footprint step aside; routes through it are planned again.</summary>
    private void MakeRoomFor(Building building)
    {
        var footprint = building.Footprint.ToHashSet();
        foreach (var unit in _units)
        {
            if (footprint.Contains(Cell.FromPosition(unit.Position))
                && Map.FindNearestInComponent(Cell.FromPosition(unit.Position), TileGrid.AnyComponent, 8) is { } free)
            {
                unit.Position = free.Center;
                unit.PreviousPosition = free.Center;
            }

            if (unit.Target != null)
                unit.ChaseRepathTicks = 0; // chase routes are refreshed next tick
            else if (unit.State == UnitState.Moving && unit.Destination is { } destination && PathCrosses(unit, footprint))
                Repath(unit, destination);
        }
    }

    private static bool PathCrosses(Unit unit, HashSet<Cell> cells)
    {
        Vector2 from = unit.Position;
        for (int i = unit.PathIndex; i < unit.Path.Count; i++)
        {
            Vector2 to = unit.Path[i];
            // Sample the segment at quarter-tile steps; footprints are at least a tile wide.
            int steps = Math.Max(1, (int)(Vector2.Distance(from, to) * 4));
            for (int s = 0; s <= steps; s++)
            {
                if (cells.Contains(Cell.FromPosition(Vector2.Lerp(from, to, (float)s / steps))))
                    return true;
            }
            from = to;
        }
        return false;
    }

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

        var type = Rules.Units.Get(recruit.UnitTypeId);
        var faction = GetFaction(recruit.FactionId)!;
        faction.Treasury.TrySpend(type.Cost);
        faction.Population++;
        int ticks = Math.Max(1, (int)MathF.Round(type.BuildTime * TicksPerSecond));
        GetBuilding(recruit.BuildingId)!.Enqueue(new ProductionItem(type, ticks));
    }

    /// <summary>Working mines dig their deposits; exhausted deposits refill.</summary>
    private void UpdateDeposits()
    {
        foreach (var deposit in _deposits)
        {
            var mine = deposit.MineId != 0 ? GetBuilding(deposit.MineId) : null;
            int owner = mine?.FactionId ?? -1;
            if (deposit.IsExhausted)
            {
                if (deposit.Regenerate())
                    _events.Add(new DepositRestored(owner, deposit.Id));
            }
            else if (mine is { IsAlive: true, IsComplete: true } && deposit.Mine(TicksPerSecond))
            {
                _events.Add(new DepositExhausted(owner, deposit.Id));
            }
        }
    }

    /// <summary>
    /// Recomputes income, upkeep, army cap and population of every faction, optionally paying one tick of
    /// income minus upkeep.
    /// </summary>
    private void UpdateEconomy(bool payIncome)
    {
        foreach (var faction in _factions)
        {
            int income = 0, cap = 0, upkeep = 0;
            foreach (var building in _buildings)
            {
                if (building.FactionId != faction.Id || !building.IsComplete || !building.IsAlive)
                    continue;
                income += building.Income;
                cap += building.Type.ArmyCap;
            }
            foreach (var unit in _units)
            {
                if (unit.FactionId == faction.Id && unit.IsAlive)
                    upkeep += unit.Type.Upkeep;
            }
            faction.IncomePerSecond = income;
            faction.ArmyUpkeepPerMinute = upkeep;
            faction.UpkeepPerMinute = Math.Max(0, upkeep - Rules.UpkeepAllowance);
            faction.ArmyCap = cap;
            faction.Population = PopulationOf(faction.Id);
            if (payIncome && !faction.IsEliminated)
                faction.Treasury.AccrueTick(faction.NetPerMinute, TicksPerSecond * 60);
        }
    }

    private int PopulationOf(int factionId)
    {
        int count = 0;
        foreach (var unit in _units)
            if (unit.FactionId == factionId)
                count++;
        foreach (var building in _buildings)
            if (building.FactionId == factionId)
                count += building.Queue.Count;
        return count;
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
        building.StartUpgrade((int)MathF.Round(building.Type.UpgradeTime * TicksPerSecond));
        _events.Add(new BuildingUpgradeStarted(upgrade.FactionId, building.Id));
    }

    /// <summary>How many recruits a building trains at once: one, and one more per finished barracks of its owner.</summary>
    public int TrainingSlots(Building building) => 1 + UnlockLevel(building.FactionId);

    private readonly List<UnitType> _finished = new();

    private void AdvanceConstructionAndTraining()
    {
        var slots = new Dictionary<int, int>();
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
                var unit = SpawnUnit(type.Id, building.FactionId, FreeSpotNear(building, type.Radius));
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
            bool occupied = _units.Any(u => Vector2.DistanceSquared(u.Position, slot) < MathF.Pow(u.Type.Radius + radius, 2));
            if (!occupied)
                return slot;
        }
        return slots[0]; // crowded: separation will make room
    }

    private void RemoveDestroyedBuildings()
    {
        for (int i = _buildings.Count - 1; i >= 0; i--)
        {
            var building = _buildings[i];
            if (building.IsAlive)
                continue;
            RemoveBuilding(building);
            _events.Add(new BuildingDestroyed(building.FactionId, building.Id, building.Type.Id, building.KilledByFactionId,
                building.Position, building.Type.Size));
            Loot(building);
        }
    }

    /// <summary>Whoever brought an enemy building down takes a share of what it cost.</summary>
    private void Loot(Building building)
    {
        int amount = building.Invested * Rules.LootPercent / 100;
        if (amount <= 0 || !AreEnemies(building.KilledByFactionId, building.FactionId)
            || GetFaction(building.KilledByFactionId) is not { IsEliminated: false } looter)
            return;
        looter.Treasury.Add(amount);
        _events.Add(new GoldLooted(looter.Id, building.FactionId, amount, building.Position));
    }

    /// <summary>
    /// Whether a <see cref="DemolishCommand"/> would currently succeed, and what it would give back: a share of
    /// what the building cost (with its upgrades) and the full price of recruits still in its queue.
    /// </summary>
    public DemolishError CanDemolish(int factionId, int buildingId, out int refund)
    {
        refund = 0;
        if (GetBuilding(buildingId) is not { IsAlive: true } building)
            return DemolishError.UnknownBuilding;
        if (building.FactionId != factionId || GetFaction(factionId) is not { IsEliminated: false })
            return DemolishError.NotOwner;
        if (building.Type.IsCapital)
            return DemolishError.Capital;
        refund = building.Invested * Rules.DemolishRefundPercent / 100 + building.Queue.Sum(item => item.Type.Cost);
        return DemolishError.None;
    }

    private void ApplyDemolish(DemolishCommand demolish)
    {
        var error = CanDemolish(demolish.FactionId, demolish.BuildingId, out int refund);
        if (error != DemolishError.None)
        {
            _events.Add(new DemolishRejected(demolish.FactionId, demolish.BuildingId, error));
            return;
        }
        var building = GetBuilding(demolish.BuildingId)!;
        RemoveBuilding(building);
        GetFaction(demolish.FactionId)!.Treasury.Add(refund);
        _events.Add(new BuildingDemolished(building.FactionId, building.Id, building.Type.Id, refund, building.Position, building.Type.Size));
    }

    private void RemoveBuilding(Building building)
    {
        building.Health = 0; // anyone still aiming at it lets go
        _buildings.Remove(building);
        _entitiesById.Remove(building.Id);
        if (building.Deposit != null)
            building.Deposit.MineId = 0;
        Map.SetOccupied(building.Footprint, false);
    }

    /// <summary>
    /// Contenders whose capital fell are out. When the standing contenders all belong to one team,
    /// that team wins (allies win together, even those already eliminated).
    /// </summary>
    private void CheckEliminations()
    {
        foreach (var faction in _factions)
        {
            if (faction.IsContender && !faction.IsEliminated && GetBuilding(faction.CapitalId) == null)
                Eliminate(faction);
        }

        var contenders = _factions.Where(f => f.IsContender).ToList();
        int teams = contenders.Select(f => f.Team).Distinct().Count();
        var standingTeams = contenders.Where(f => !f.IsEliminated).Select(f => f.Team).Distinct().ToList();
        if (teams >= 2 && standingTeams.Count <= 1)
        {
            IsOver = true;
            WinningTeam = standingTeams.Count == 1 ? standingTeams[0] : -1;
            _events.Add(new MatchEnded(WinningTeam));
        }
    }

    /// <summary>The army disbands and every remaining building of the faction collapses.</summary>
    private void Eliminate(Faction faction)
    {
        faction.IsEliminated = true;
        foreach (var unit in _units.Where(u => u.FactionId == faction.Id).ToList())
        {
            unit.Health = 0;
            _units.Remove(unit);
            _entitiesById.Remove(unit.Id);
        }
        foreach (var building in _buildings.Where(b => b.FactionId == faction.Id).ToList())
            RemoveBuilding(building);
        _events.Add(new FactionEliminated(faction.Id));
    }
}

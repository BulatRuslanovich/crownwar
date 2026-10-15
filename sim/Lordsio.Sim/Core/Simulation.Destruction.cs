using Lordsio.Sim.Buildings;

namespace Lordsio.Sim.Core;

/// <summary>Buildings falling (and the loot), demolition, eliminated factions and the end of the match.</summary>
public sealed partial class Simulation
{
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

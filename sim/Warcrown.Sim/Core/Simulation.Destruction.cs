using Warcrown.Sim.Buildings;

namespace Warcrown.Sim.Core;

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

    /// <summary>
    /// Whoever brought an enemy building down takes a share of what it cost, and from a mine also a share of the gold
    /// still in its deposit (which loses it): raiding the leader's mines pays.
    /// </summary>
    private void Loot(Building building)
    {
        if (!AreEnemies(building.KilledByFactionId, building.FactionId)
            || GetFaction(building.KilledByFactionId) is not { IsEliminated: false } looter)
            return;
        int amount = building.Invested * Rules.LootPercent / 100;
        if (building.Type.IsMine && building.Deposit is { } deposit)
            amount += deposit.Plunder(Rules.MineRaidPercent);
        if (amount <= 0)
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

    /// <param name="relabel">False when several buildings go at once; the caller then relabels the map once.</param>
    private void RemoveBuilding(Building building, bool relabel = true)
    {
        building.Health = 0; // anyone still aiming at it lets go
        _buildings.Remove(building);
        _entitiesById.Remove(building.Id);
        if (building.Deposit != null)
            building.Deposit.MineId = 0;
        Map.SetOccupied(building.Anchor, building.Type.Size, false, relabel);
    }

    /// <summary>
    /// Contenders whose capital fell are out. When the standing contenders all belong to one team,
    /// that team wins (allies win together, even those already eliminated).
    /// </summary>
    private void CheckEliminations()
    {
        if (Defense is { } defense)
        {
            CheckCitadel(defense); // outposts may fall and be built again: only the citadel counts
            return;
        }
        foreach (var faction in _factions)
        {
            if (faction.IsContender && !faction.IsEliminated && GetBuilding(faction.CapitalId) == null)
                Eliminate(faction);
        }

        // Counted without allocating, every tick: are there two teams at all, and how many still stand?
        int firstTeam = 0, standingTeam = 0;
        bool anyContender = false, twoTeams = false, anyStanding = false, twoStanding = false;
        foreach (var f in _factions)
        {
            if (!f.IsContender)
                continue;
            if (!anyContender)
            {
                anyContender = true;
                firstTeam = f.Team;
            }
            else if (f.Team != firstTeam)
            {
                twoTeams = true;
            }
            if (f.IsEliminated)
                continue;
            if (!anyStanding)
            {
                anyStanding = true;
                standingTeam = f.Team;
            }
            else if (f.Team != standingTeam)
            {
                twoStanding = true;
            }
        }
        if (twoTeams && !twoStanding)
        {
            IsOver = true;
            WinningTeam = anyStanding ? standingTeam : -1;
            _events.Add(new MatchEnded(WinningTeam));
        }
    }

    /// <summary>The army disbands and every remaining building of the faction collapses.</summary>
    private void Eliminate(Faction faction)
    {
        faction.IsEliminated = true;
        for (int i = 0; i < _units.Count; i++)
        {
            var unit = _units[i];
            if (unit.FactionId != faction.Id)
                continue;
            unit.Health = 0;
            _units.RemoveAt(i--);
            _entitiesById.Remove(unit.Id);
        }
        bool collapsed = false;
        for (int i = 0; i < _buildings.Count; i++)
        {
            if (_buildings[i].FactionId != faction.Id)
                continue;
            RemoveBuilding(_buildings[i--], relabel: false);
            collapsed = true;
        }
        if (collapsed)
            Map.Relabel();
        _events.Add(new FactionEliminated(faction.Id));
    }
}

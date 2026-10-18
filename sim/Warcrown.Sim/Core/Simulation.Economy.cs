
namespace Warcrown.Sim.Core;

/// <summary>Gold deposits being mined and refilling; income, upkeep, army cap and population.</summary>
public sealed partial class Simulation
{
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
            int income = 0, cap = 0, upkeep = 0, soldiers = 0;
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
                {
                    upkeep += unit.Type.Upkeep;
                    soldiers++;
                }
            }
            int surcharge = Math.Max(0, soldiers - Rules.UpkeepSurchargeFrom) * Rules.UpkeepSurchargePercent;
            upkeep += upkeep * surcharge / 100;
            faction.UpkeepSurchargePercent = surcharge;
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
}

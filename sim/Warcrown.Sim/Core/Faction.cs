using Warcrown.Sim.Economy;

namespace Warcrown.Sim.Core;

public sealed class Faction
{
    internal Faction(int id, int team, GameRules rules)
    {
        Id = id;
        Team = team;
        Treasury = new Treasury(rules.StartingGold, rules.GoldCap);
    }

    public int Id { get; }

    /// <summary>Factions with the same team are allies: they never fight and win together.</summary>
    public int Team { get; }
    public Treasury Treasury { get; }

    /// <summary>Gold per second from the capital and working mines; recomputed every tick.</summary>
    public int IncomePerSecond { get; internal set; }

    /// <summary>Upkeep of the whole living army, gold per minute, before the free allowance.</summary>
    public int ArmyUpkeepPerMinute { get; internal set; }

    /// <summary>Gold per minute actually paid: army upkeep above the rules' free allowance; recomputed every tick.</summary>
    public int UpkeepPerMinute { get; internal set; }

    /// <summary>Income minus upkeep, in gold per minute. When negative, gold drains down to zero.</summary>
    public int NetPerMinute => IncomePerSecond * 60 - UpkeepPerMinute;

    /// <summary>Maximum army size (living units plus units in training); recomputed every tick.</summary>
    public int ArmyCap { get; internal set; }

    /// <summary>Living units plus units queued for training.</summary>
    public int Population { get; internal set; }

    /// <summary>Id of the faction's capital building, or 0 if it never had one.</summary>
    public int CapitalId { get; internal set; }

    public bool IsEliminated { get; internal set; }

    /// <summary>Started with a capital, so it competes for victory and is eliminated without one.</summary>
    internal bool IsContender => CapitalId != 0;
}

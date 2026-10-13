using Lordsio.Sim.Economy;

namespace Lordsio.Sim.Core;

public sealed class Faction
{
    internal Faction(int id, int team, int startingGold)
    {
        Id = id;
        Team = team;
        Treasury = new Treasury(startingGold);
    }

    public int Id { get; }

    /// <summary>Factions with the same team are allies: they never fight and win together.</summary>
    public int Team { get; }
    public Treasury Treasury { get; }

    /// <summary>Gold per second from the capital and working mines; recomputed every tick.</summary>
    public int IncomePerSecond { get; internal set; }

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

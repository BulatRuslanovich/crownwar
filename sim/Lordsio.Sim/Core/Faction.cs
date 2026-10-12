using Lordsio.Sim.Economy;

namespace Lordsio.Sim.Core;

public sealed class Faction
{
    internal Faction(int id, int startingGold)
    {
        Id = id;
        Treasury = new Treasury(startingGold);
    }

    public int Id { get; }
    public Treasury Treasury { get; }

    /// <summary>Gold per second from owned settlements; recomputed every tick.</summary>
    public int IncomePerSecond { get; internal set; }

    /// <summary>Maximum army size (living units plus units in production); recomputed every tick.</summary>
    public int ArmyCap { get; internal set; }

    /// <summary>Living units plus units queued for production.</summary>
    public int Population { get; internal set; }

    public bool IsEliminated { get; internal set; }

    /// <summary>Started with a capital, so it competes for victory and is eliminated without one.</summary>
    internal bool IsContender { get; set; }
}

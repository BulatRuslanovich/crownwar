namespace Lordsio.Sim.Economy;

/// <summary>
/// A faction's gold. Integer arithmetic only: a rate is given per period (a second, a minute) and paid out
/// per tick, with the fractional part carried over, so N periods always add exactly N × rate.
/// A negative rate (upkeep above income) drains gold, but never below zero.
/// </summary>
public sealed class Treasury
{
    private int _incomeRemainder;

    public Treasury(int gold)
    {
        if (gold < 0)
            throw new ArgumentOutOfRangeException(nameof(gold), "Gold cannot be negative.");
        Gold = gold;
    }

    public int Gold { get; private set; }

    public bool CanAfford(int cost) => cost <= Gold;

    /// <summary>Deducts <paramref name="cost"/> if affordable; otherwise leaves the gold untouched.</summary>
    public bool TrySpend(int cost)
    {
        if (cost < 0)
            throw new ArgumentOutOfRangeException(nameof(cost), "Cost cannot be negative.");
        if (cost > Gold)
            return false;
        Gold -= cost;
        return true;
    }

    public void Add(int amount)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Use TrySpend to remove gold.");
        Gold += amount;
    }

    /// <summary>Pays (or, if negative, charges) one tick's share of <paramref name="amountPerPeriod"/>.</summary>
    public void AccrueTick(int amountPerPeriod, int ticksPerPeriod)
    {
        if (ticksPerPeriod <= 0)
            throw new ArgumentOutOfRangeException(nameof(ticksPerPeriod));
        _incomeRemainder += amountPerPeriod;
        int whole = (int)Math.Floor(_incomeRemainder / (double)ticksPerPeriod);
        _incomeRemainder -= whole * ticksPerPeriod;
        Gold += whole;
        if (Gold < 0)
        {
            Gold = 0;
            _incomeRemainder = 0; // no debt: an empty treasury simply stays empty
        }
    }

    internal int Remainder => _incomeRemainder;
}

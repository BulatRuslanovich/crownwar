namespace Lordsio.Sim.Economy;

/// <summary>
/// A faction's gold. Integer arithmetic only: income is given per second and paid out per tick,
/// with the fractional part carried over, so N seconds of income always add exactly N × rate.
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

    /// <summary>Pays one tick's share of <paramref name="incomePerSecond"/>.</summary>
    public void AccrueTick(int incomePerSecond, int ticksPerSecond)
    {
        if (incomePerSecond < 0)
            throw new ArgumentOutOfRangeException(nameof(incomePerSecond));
        _incomeRemainder += incomePerSecond;
        Gold += _incomeRemainder / ticksPerSecond;
        _incomeRemainder %= ticksPerSecond;
    }
}

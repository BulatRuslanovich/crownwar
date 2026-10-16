namespace Warcrown.Sim.Core;

/// <summary>
/// One rank of a veteran: reached at <paramref name="Experience"/> points, it makes the unit
/// <paramref name="HealthFactor"/> times as tough and <paramref name="DamageFactor"/> times as hard-hitting as a recruit.
/// </summary>
public sealed record VeteranRank(int Experience, float HealthFactor, float DamageFactor);

/// <summary>
/// Veterancy (the "veterancy" section of the rules file): a unit that lands the killing blow on an enemy unit gains
/// experience worth what the fallen cost, and climbs these ranks in order. No ranks, no veterans.
/// </summary>
public sealed record VeterancyRules(IReadOnlyList<VeteranRank> Ranks)
{
    public static readonly VeterancyRules None = new(Array.Empty<VeteranRank>());

    /// <summary>The highest rank (1-based, 0 for a recruit) that <paramref name="experience"/> earns.</summary>
    public int RankFor(int experience)
    {
        int rank = 0;
        while (rank < Ranks.Count && experience >= Ranks[rank].Experience)
            rank++;
        return rank;
    }

    internal void Validate()
    {
        for (int i = 0; i < Ranks.Count; i++)
        {
            var rank = Ranks[i];
            var below = i > 0 ? Ranks[i - 1] : new VeteranRank(0, 1f, 1f);
            if (rank.Experience <= below.Experience || rank.HealthFactor < below.HealthFactor || rank.DamageFactor < below.DamageFactor)
                throw new ArgumentException("Veteran ranks need ever more experience and never weaken the unit.");
        }
    }
}

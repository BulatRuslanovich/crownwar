namespace Lordsio.Sim.Core;

public enum MatchMode
{
    /// <summary>Everyone against everyone (or team against team): the last castle standing wins.</summary>
    Versus,
    /// <summary>All players on one team hold a shared citadel against endless waves (see <see cref="DefenseRules"/>).</summary>
    Defense,
}

/// <summary>
/// How a match starts. Factions 0..<paramref name="FactionCount"/>-1 each get a capital on one of the
/// map's start positions; with <paramref name="ShuffleStarts"/> the positions are picked at random from
/// <paramref name="Seed"/>, otherwise faction i takes start i (handy for tests).
/// <paramref name="Teams"/> gives each faction's team (allies share a number); null means everyone for
/// themselves. In <see cref="MatchMode.Defense"/> the players are all one team whatever <paramref name="Teams"/> says,
/// and two more factions join: the garrison holding the citadel (their ally) and the horde.
/// </summary>
public sealed record MatchSetup(int FactionCount, int Seed = 0, bool ShuffleStarts = true, IReadOnlyList<int>? Teams = null,
    MatchMode Mode = MatchMode.Versus)
{
    /// <summary>The players' team in Defense (the garrison is on it too); the horde is on <see cref="HordeTeam"/>.</summary>
    public const int DefendersTeam = 0, HordeTeam = 1;

    /// <summary>No capitals: a sandbox with only the units placed on the map.</summary>
    public static readonly MatchSetup Sandbox = new(0, ShuffleStarts: false);

    /// <summary>Defense: the faction owning the citadel (after the players).</summary>
    public int GarrisonFactionId => FactionCount;

    /// <summary>Defense: the faction of the waves.</summary>
    public int HordeFactionId => FactionCount + 1;

    public int TeamOf(int factionId)
    {
        if (Mode == MatchMode.Defense)
            return factionId == HordeFactionId ? HordeTeam : DefendersTeam;
        return Teams != null && factionId >= 0 && factionId < Teams.Count ? Teams[factionId] : factionId;
    }
}

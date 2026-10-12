namespace Lordsio.Sim.Core;

/// <summary>
/// How a match starts. Factions 0..<paramref name="FactionCount"/>-1 each get a capital on one of the
/// map's start positions; with <paramref name="ShuffleStarts"/> the positions are picked at random from
/// <paramref name="Seed"/>, otherwise faction i takes start i (handy for tests).
/// </summary>
public sealed record MatchSetup(int FactionCount, int Seed = 0, bool ShuffleStarts = true)
{
    /// <summary>No capitals: a sandbox with only the units placed on the map.</summary>
    public static readonly MatchSetup Sandbox = new(0, ShuffleStarts: false);
}

using Lordsio.Sim.AI;

namespace Lordsio.Core;

/// <summary>What the player chose in the menu. A null seed means a new random layout each match.</summary>
public sealed record MatchConfig(int BotCount, BotDifficulty Difficulty, int? Seed)
{
    public static readonly MatchConfig Default = new(3, BotDifficulty.Normal, null);
}

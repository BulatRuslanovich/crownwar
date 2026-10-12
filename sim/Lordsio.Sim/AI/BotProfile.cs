namespace Lordsio.Sim.AI;

public enum BotDifficulty
{
    Easy,
    Normal,
    Hard,
}

/// <summary>
/// How a bot plays. Difficulty only changes decisions (reaction time, aggression, build choices),
/// never the rules: every bot gets the same gold and plays through the same commands as the player.
/// </summary>
/// <param name="ThinkInterval">Seconds between decisions.</param>
/// <param name="AttackArmyValue">Army worth (gold spent on living units) at which the bot goes on the offensive.</param>
/// <param name="CapitalAssaultValue">Army worth needed before an enemy castle is chosen as a target.</param>
/// <param name="RetreatFraction">Retreat once the attacking army has shrunk below this share of its launch worth.</param>
/// <param name="BuildsTowers">Guards its mines with archer towers.</param>
/// <param name="MaxBarracks">Barracks it is willing to build.</param>
/// <param name="CountersComposition">Picks unit types against what enemies field instead of a fixed mix.</param>
public sealed record BotProfile(
    float ThinkInterval,
    int AttackArmyValue,
    int CapitalAssaultValue,
    float RetreatFraction,
    bool BuildsTowers,
    int MaxBarracks,
    bool CountersComposition)
{
    public static BotProfile For(BotDifficulty difficulty) => difficulty switch
    {
        BotDifficulty.Easy => new(ThinkInterval: 2f, AttackArmyValue: 900, CapitalAssaultValue: 1400,
            RetreatFraction: 0.2f, BuildsTowers: false, MaxBarracks: 1, CountersComposition: false),
        BotDifficulty.Hard => new(ThinkInterval: 0.5f, AttackArmyValue: 500, CapitalAssaultValue: 800,
            RetreatFraction: 0.45f, BuildsTowers: true, MaxBarracks: 3, CountersComposition: true),
        _ => new(ThinkInterval: 1f, AttackArmyValue: 650, CapitalAssaultValue: 1000,
            RetreatFraction: 0.35f, BuildsTowers: true, MaxBarracks: 2, CountersComposition: false),
    };
}

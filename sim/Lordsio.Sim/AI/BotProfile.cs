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
/// <param name="AttackAdvantage">Only attacks a target whose defenders (units, towers, castle) are worth at most
/// army worth / this factor: careful bots pick fights they can win.</param>
/// <param name="MaxMines">Mines it is willing to run.</param>
/// <param name="FirstMineDelay">Seconds into the match before it starts its first mine.</param>
/// <param name="MineInterval">Seconds between starting one mine and the next: easier bots grow their economy
/// at a pace a new player can match.</param>
/// <param name="MaxUpgrades">Mine levels it buys at most.</param>
/// <param name="UpgradeAfter">Seconds into the match before it upgrades anything.</param>
public sealed record BotProfile(
    float ThinkInterval,
    int AttackArmyValue,
    int CapitalAssaultValue,
    float RetreatFraction,
    bool BuildsTowers,
    int MaxBarracks,
    bool CountersComposition,
    float AttackAdvantage,
    int MaxMines,
    float FirstMineDelay = 0f,
    float MineInterval = 0f,
    int MaxUpgrades = 0,
    float UpgradeAfter = 0f)
{
    public static BotProfile For(BotDifficulty difficulty) => difficulty switch
    {
        BotDifficulty.Easy => new(ThinkInterval: 2f, AttackArmyValue: 900, CapitalAssaultValue: 2000,
            RetreatFraction: 0.2f, BuildsTowers: false, MaxBarracks: 1, CountersComposition: false,
            AttackAdvantage: 1f, MaxMines: 2, FirstMineDelay: 60f, MineInterval: 60f),
        BotDifficulty.Hard => new(ThinkInterval: 0.5f, AttackArmyValue: 700, CapitalAssaultValue: 1500,
            RetreatFraction: 0.45f, BuildsTowers: true, MaxBarracks: 3, CountersComposition: true,
            AttackAdvantage: 1.6f, MaxMines: int.MaxValue, MaxUpgrades: int.MaxValue, UpgradeAfter: 90f),
        _ => new(ThinkInterval: 1f, AttackArmyValue: 800, CapitalAssaultValue: 1700,
            RetreatFraction: 0.35f, BuildsTowers: true, MaxBarracks: 2, CountersComposition: false,
            AttackAdvantage: 1.3f, MaxMines: 3, FirstMineDelay: 20f, MineInterval: 30f, MaxUpgrades: 3, UpgradeAfter: 150f),
    };
}

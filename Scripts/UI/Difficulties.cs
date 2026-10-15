using Lordsio.Sim.AI;

namespace Lordsio.UI;

/// <summary>The bot difficulties in menu order, with their names in the translation tables.</summary>
public static class Difficulties
{
    public static readonly BotDifficulty[] All = { BotDifficulty.Easy, BotDifficulty.Normal, BotDifficulty.Hard };

    /// <summary>"Easy" / "Normal" / "Hard" in the main menu.</summary>
    public static string MenuKey(BotDifficulty difficulty) => difficulty switch
    {
        BotDifficulty.Easy => "DIFFICULTY_EASY",
        BotDifficulty.Hard => "DIFFICULTY_HARD",
        _ => "DIFFICULTY_NORMAL",
    };

    /// <summary>"Bot (easy)" and so on, for a lobby slot.</summary>
    public static string SlotKey(BotDifficulty difficulty) => difficulty switch
    {
        BotDifficulty.Easy => "NET_SLOT_BOT_EASY",
        BotDifficulty.Hard => "NET_SLOT_BOT_HARD",
        _ => "NET_SLOT_BOT_NORMAL",
    };
}

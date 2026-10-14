using Godot;
using Lordsio.Core;
using Lordsio.Sim.AI;
using Lordsio.Sim.Buildings;
using Lordsio.Sim.Units;

namespace Lordsio.UI;

/// <summary>
/// Display names for rule data. The rules carry English names; the translation tables may override them
/// with keys UNIT_&lt;ID&gt; / BUILDING_&lt;ID&gt;, so new types still show a name before they are translated.
/// </summary>
public static class Names
{
    public static string Of(UnitType type) => Translated("UNIT_" + type.Id.ToUpperInvariant(), type.Name);

    public static string Of(BuildingType type) => Translated("BUILDING_" + type.Id.ToUpperInvariant(), type.Name);

    /// <summary>A player's name (network games), "you", or the bot and its difficulty.</summary>
    public static string Player(MatchController match, int faction)
    {
        var slots = match.Config.Slots;
        if (faction >= slots.Count)
            return Faction(faction);
        var slot = slots[faction];
        if (slot.Kind == SlotKind.Bot)
        {
            return TranslationServer.Translate(slot.Difficulty switch
            {
                BotDifficulty.Easy => "NET_SLOT_BOT_EASY",
                BotDifficulty.Hard => "NET_SLOT_BOT_HARD",
                _ => "NET_SLOT_BOT_NORMAL",
            });
        }
        string name = slot.Name.Length > 0 ? slot.Name : GameSettings.Instance?.PlayerName ?? "";
        return faction == match.LocalFactionId ? $"{name} {TranslationServer.Translate("NET_YOU")}".Trim() : name;
    }

    public static string Faction(int factionId) => string.Format(TranslationServer.Translate("FACTION_N"), factionId);

    private static string Translated(string key, string fallback)
    {
        string text = TranslationServer.Translate(key);
        return text == key ? fallback : text;
    }
}

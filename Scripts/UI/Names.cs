using Godot;
using Lordsio.Sim.Buildings;
using Lordsio.Sim.Core;
using Lordsio.Sim.Units;

namespace Lordsio.UI;

/// <summary>
/// Display names for rule data. The rules carry English names; the translation tables may override them
/// with keys UNIT_&lt;ID&gt; / BUILDING_&lt;ID&gt; / UPGRADE_&lt;ID&gt;, so new types still show a name before they are translated.
/// </summary>
public static class Names
{
    public static string Of(UnitType type) => Translated("UNIT_" + type.Id.ToUpperInvariant(), type.Name);

    public static string Of(BuildingType type) => Translated("BUILDING_" + type.Id.ToUpperInvariant(), type.Name);

    public static string Of(UpgradeType upgrade) => Translated("UPGRADE_" + upgrade.Id.ToUpperInvariant(), upgrade.Name);

    public static string Faction(int factionId) => string.Format(TranslationServer.Translate("FACTION_N"), factionId);

    private static string Translated(string key, string fallback)
    {
        string text = TranslationServer.Translate(key);
        return text == key ? fallback : text;
    }
}

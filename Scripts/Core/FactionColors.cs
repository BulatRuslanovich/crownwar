using Godot;

namespace Warcrown.Core;

/// <summary>
/// Display colour per faction id. Purely visual, so it lives on the Godot side. A Defense match sets its two extra
/// sides (see <see cref="UseDefense"/>): the citadel's garrison in pale stone and the horde in a sickly olive, whatever
/// their ids, so they never look like a player.
/// </summary>
public static class FactionColors
{
    private static readonly Color Neutral = new("9a9a9a");
    private static readonly Color Garrison = new("e4dccb");
    private static readonly Color Horde = new("6f8f2a");
    private static int _garrison = -1, _horde = -1;

    /// <summary>The match being set up: its garrison and horde (Defense), or -1 for a versus match.</summary>
    public static void UseDefense(int garrisonFactionId, int hordeFactionId)
    {
        _garrison = garrisonFactionId;
        _horde = hordeFactionId;
    }

    private static readonly Color[] Palette =
    {
        new("3c6fd8"), // blue — the player
        new("d8433c"), // red
        new("e0b62f"), // yellow
        new("9b4fd0"), // purple
        new("3cb371"), // green
        new("e07b2f"), // orange
    };

    public static Color Get(int factionId) =>
        factionId < 0 ? Neutral
        : factionId == _garrison ? Garrison
        : factionId == _horde ? Horde
        : Palette[factionId % Palette.Length];
}

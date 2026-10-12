using Godot;

namespace Lordsio.Core;

/// <summary>Display colour per faction id. Purely visual, so it lives on the Godot side.</summary>
public static class FactionColors
{
    private static readonly Color Neutral = new("9a9a9a");

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
        factionId < 0 ? Neutral : Palette[factionId % Palette.Length];
}

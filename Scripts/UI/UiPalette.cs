using Godot;

namespace Lordsio.UI;

/// <summary>Colours the interface and the markers in the world share, so the same meaning always looks the same.</summary>
public static class UiPalette
{
    /// <summary>Ordinary text on the parchment panels.</summary>
    public static readonly Color Text = new(0.95f, 0.9f, 0.8f);

    /// <summary>Secondary text and chart axes.</summary>
    public static readonly Color TextMuted = new(0.95f, 0.9f, 0.8f, 0.7f);

    /// <summary>Something is wrong or out of reach: too little gold, a full treasury, war, defeat.</summary>
    public static readonly Color Problem = new("ff8a80");

    /// <summary>The player themselves, a victory, something waiting for an answer.</summary>
    public static readonly Color Highlight = new("ffe066");

    /// <summary>Own selection and move orders.</summary>
    public static readonly Color Friendly = new("7cff6b");

    /// <summary>Attacks: attack orders, alerts, pings.</summary>
    public static readonly Color Hostile = new("ff5a4a");

    /// <summary>Gold coins and ore.</summary>
    public static readonly Color Gold = new("f2c230");

    /// <summary>The dark outline of drawn icons and cursors.</summary>
    public static readonly Color Outline = new("1c1a17");

    /// <summary>The night's blue on the day/night clock.</summary>
    public static readonly Color Night = new("9fb4ff");
}

using Godot;

namespace Lordsio.Core;

/// <summary>Clock texts for the interface.</summary>
public static class TimeFormat
{
    /// <summary>Whole seconds as m:ss.</summary>
    public static string MinSec(int seconds) => $"{seconds / 60}:{seconds % 60:00}";

    /// <summary>Seconds left as m:ss, rounded up (so "0:00" never shows while time is left).</summary>
    public static string MinSecLeft(float seconds) => MinSec(Mathf.CeilToInt(seconds));
}

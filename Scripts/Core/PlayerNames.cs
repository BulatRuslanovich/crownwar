namespace Lordsio.Core;

/// <summary>Player names as the lobby, the network and the scoreboard show them.</summary>
public static class PlayerNames
{
    public const int MaxLength = 16;
    public const string Fallback = "Player";

    /// <summary>Trimmed and cut to <see cref="MaxLength"/>; <see cref="Fallback"/> when empty.</summary>
    public static string Sanitize(string? name)
    {
        name = name?.Trim() ?? "";
        if (name.Length == 0)
            return Fallback;
        return name.Length > MaxLength ? name[..MaxLength] : name;
    }
}

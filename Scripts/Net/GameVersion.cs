using Godot;

namespace Lordsio.Net;

/// <summary>
/// Identifies a build for network play: lockstep needs every machine to run the same code and the
/// same rules and map, so the version string includes a hash of the data files.
/// </summary>
public static class GameVersion
{
    public const string Number = "0.7.0";

    private static string? _current;

    public static string Current => _current ??= $"{Number}+{DataHash():x8}";

    private static uint DataHash()
    {
        uint hash = 2166136261;
        foreach (string path in new[] { "res://Data/rules.json", "res://Maps/default.txt" })
        {
            foreach (byte b in FileAccess.GetFileAsBytes(path))
            {
                hash ^= b;
                hash *= 16777619;
            }
        }
        return hash;
    }
}

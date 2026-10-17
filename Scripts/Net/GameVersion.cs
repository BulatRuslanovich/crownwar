using Godot;

namespace Warcrown.Net;

/// <summary>
/// Identifies a build for network play: lockstep needs every machine to run the same code and the
/// same rules, so the version string includes a hash of the rules file. Maps are generated from the
/// match seed by code, so the code version (<see cref="Number"/>) covers them.
/// </summary>
public static class GameVersion
{
    public const string Number = "1.1.0";

    private static string? _current;

    public static string Current => _current ??= $"{Number}+{DataHash():x8}";

    private static uint DataHash()
    {
        uint hash = 2166136261;
        foreach (string path in new[] { "res://Data/rules.json" })
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

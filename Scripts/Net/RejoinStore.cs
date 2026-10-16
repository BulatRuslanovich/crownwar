using Godot;

namespace Warcrown.Net;

/// <summary>
/// The rejoin key of the last network match, kept in user://rejoin.cfg so that it survives a crash:
/// after restarting the game the player can connect again and get their faction back.
/// </summary>
public static class RejoinStore
{
    private const string FilePath = "user://rejoin.cfg";

    public static void Save(string token)
    {
        var config = new ConfigFile();
        config.SetValue("rejoin", "token", token);
        config.Save(FilePath);
    }

    public static string Load()
    {
        var config = new ConfigFile();
        return config.Load(FilePath) == Error.Ok ? (string)config.GetValue("rejoin", "token", "") : "";
    }
}

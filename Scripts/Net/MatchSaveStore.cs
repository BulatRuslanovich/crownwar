using System.Collections.Generic;
using System.IO;
using Godot;
using Warcrown.Sim.Net;

namespace Warcrown.Net;

/// <summary>
/// The host's running network match on disk (user://netmatch.save, see <see cref="MatchSave"/>): written while the
/// match is played, so after a crash the host can bring it back and the others rejoin. Removed when the match ends.
/// </summary>
public static class MatchSaveStore
{
    private static string FilePath => ProjectSettings.GlobalizePath("user://netmatch.save");

    /// <summary>A save of this game version, or null (none, unreadable, another version, or not a single frame).</summary>
    public static SavedMatch? TryLoad()
    {
        if (!File.Exists(FilePath))
            return null;
        try
        {
            using var stream = File.OpenRead(FilePath);
            return MatchSave.Read(stream) is { Frames.Count: > 0 } save && save.Version == GameVersion.Current ? save : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>Starts a new save (replacing any old one) with the match's header.</summary>
    public static FileStream? Create(StartMessage start, IEnumerable<SavedPlayer> players)
    {
        try
        {
            var stream = new FileStream(FilePath, FileMode.Create, System.IO.FileAccess.Write, FileShare.Read);
            MatchSave.WriteHeader(stream, GameVersion.Current, start, players);
            return stream;
        }
        catch (IOException e)
        {
            GD.PushWarning($"Cannot save the network match: {e.Message}");
            return null;
        }
    }

    /// <summary>Goes on writing a resumed match's save, after the frames it already holds.</summary>
    public static FileStream? Append()
    {
        try
        {
            return new FileStream(FilePath, FileMode.Append, System.IO.FileAccess.Write, FileShare.Read);
        }
        catch (IOException e)
        {
            GD.PushWarning($"Cannot save the network match: {e.Message}");
            return null;
        }
    }

    public static void Delete()
    {
        try
        {
            File.Delete(FilePath);
        }
        catch (IOException)
        {
            // Nothing to do: the next match overwrites it anyway.
        }
    }
}

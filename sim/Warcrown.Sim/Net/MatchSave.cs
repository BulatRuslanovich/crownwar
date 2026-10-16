namespace Warcrown.Sim.Net;

/// <summary>A human player of a saved match: their faction, rejoin key and name.</summary>
public sealed record SavedPlayer(int Faction, string Token, string Name);

/// <summary>Everything a host needs to bring a network match back: how it started, who played, and every frame.</summary>
public sealed record SavedMatch(string Version, StartMessage Start, IReadOnlyList<SavedPlayer> Players, IReadOnlyList<FrameMessage> Frames);

/// <summary>
/// The host's match on disk, so a crash does not end it for everyone: a header (version, start, players) written
/// once, then frames appended as they are made. Each record is length-prefixed and encoded with
/// <see cref="NetCodec"/>; a record cut short by a crash is simply where the match resumes.
/// </summary>
public static class MatchSave
{
    private const uint Magic = 0x5641534C; // "LSAV"

    public static void WriteHeader(Stream stream, string version, StartMessage start, IEnumerable<SavedPlayer> players)
    {
        using var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        w.Write(Magic);
        w.Write(version);
        WriteRecord(w, NetCodec.Encode(start));
        var list = players.ToList();
        w.Write(list.Count);
        foreach (var p in list)
        {
            w.Write(p.Faction);
            w.Write(p.Token);
            w.Write(p.Name);
        }
        w.Flush();
    }

    public static void AppendFrames(Stream stream, IEnumerable<FrameMessage> frames)
    {
        using var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        foreach (var frame in frames)
            WriteRecord(w, NetCodec.Encode(frame));
        w.Flush();
    }

    /// <summary>Reads a save; null if it is not one. Frames stop at the first incomplete or broken record.</summary>
    public static SavedMatch? Read(Stream stream)
    {
        try
        {
            using var r = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
            if (r.ReadUInt32() != Magic)
                return null;
            string version = r.ReadString();
            if (NetCodec.Decode(ReadRecord(r)!) is not StartMessage start)
                return null;
            int count = r.ReadInt32();
            var players = new List<SavedPlayer>();
            for (int i = 0; i < count; i++)
                players.Add(new SavedPlayer(r.ReadInt32(), r.ReadString(), r.ReadString()));

            var frames = new List<FrameMessage>();
            while (ReadRecord(r) is { } data)
            {
                if (NetCodec.Decode(data) is not FrameMessage frame || frame.Tick != frames.Count + 1)
                    break;
                frames.Add(frame);
            }
            return new SavedMatch(version, start, players, frames);
        }
        catch (Exception e) when (e is EndOfStreamException or FormatException or IOException)
        {
            return null;
        }
    }

    private static void WriteRecord(BinaryWriter w, byte[] data)
    {
        w.Write(data.Length);
        w.Write(data);
    }

    /// <summary>The next record, or null at the end (or where a crash cut the file short).</summary>
    private static byte[]? ReadRecord(BinaryReader r)
    {
        try
        {
            int length = r.ReadInt32();
            if (length <= 0 || length > 1 << 20)
                return null;
            var data = r.ReadBytes(length);
            return data.Length == length ? data : null;
        }
        catch (EndOfStreamException)
        {
            return null;
        }
    }
}

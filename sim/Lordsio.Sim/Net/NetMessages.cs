using System.Numerics;
using System.Text;
using Lordsio.Sim.Core;
using Lordsio.Sim.World;

namespace Lordsio.Sim.Net;

/// <summary>Who holds a lobby slot. Slot i becomes faction i when the match starts.</summary>
public enum NetSlotKind : byte
{
    Empty,
    Human,
    Bot,
}

/// <param name="PeerId">Network peer that controls a human slot (0 for bots and empty slots).</param>
/// <param name="Difficulty">Bot difficulty as <see cref="AI.BotDifficulty"/>.</param>
public sealed record NetSlot(NetSlotKind Kind, string Name, byte Difficulty, byte Team, int PeerId);

public abstract record NetMessage;

/// <summary>
/// Client → host on connect. Both sides must run the same build and data. <paramref name="RejoinToken"/> is
/// the key the host gave this player when the match started (empty if none): with it a player who dropped
/// out can come back into the running match.
/// </summary>
public sealed record HelloMessage(string Version, string Name, string RejoinToken = "") : NetMessage;

/// <summary>Host → client: connection refused (a translation key for the reason).</summary>
public sealed record RejectMessage(string Reason) : NetMessage;

/// <summary>Host → all: the current lobby.</summary>
public sealed record LobbyMessage(IReadOnlyList<NetSlot> Slots) : NetMessage;

/// <summary>Client → host: move my slot to another team.</summary>
public sealed record SetTeamMessage(byte Team) : NetMessage;

/// <summary>
/// Host → each client: the match starts; <paramref name="YourFaction"/> is the receiver's slot and
/// <paramref name="RejoinToken"/> its key for coming back after a drop. With <paramref name="Rejoin"/> the match
/// is already running: every frame so far follows, to be replayed before playing on.
/// </summary>
public sealed record StartMessage(int Seed, IReadOnlyList<NetSlot> Slots, int YourFaction, string RejoinToken = "", bool Rejoin = false)
    : NetMessage;

/// <summary>A faction a returning player takes back from its bot, starting with <paramref name="FromTick"/>.</summary>
public sealed record FactionReturn(int Faction, int FromTick);

/// <summary>Peer → host: my commands for <paramref name="Tick"/> (sent even when empty).</summary>
public sealed record InputMessage(int Tick, IReadOnlyList<Command> Commands) : NetMessage;

/// <summary>
/// Host → all: everything to apply before simulating <paramref name="Tick"/>: the commands of every
/// player in a fixed order, factions that a bot takes over from this tick on, and factions whose returning
/// player takes them back from the bot at a later tick (announced ahead, so the player can send input in time).
/// </summary>
public sealed record FrameMessage(int Tick, IReadOnlyList<Command> Commands, IReadOnlyList<int> Takeovers, IReadOnlyList<FactionReturn> Returns)
    : NetMessage;

/// <summary>Returning client → host: the replay reached <paramref name="Tick"/>; the faction can be handed back.</summary>
public sealed record CaughtUpMessage(int Tick) : NetMessage;

/// <summary>Peer → host: state fingerprint after simulating <paramref name="Tick"/>.</summary>
public sealed record ChecksumMessage(int Tick, ulong Value) : NetMessage;

/// <summary>Host → all: peers diverged at <paramref name="Tick"/>; the match cannot continue.</summary>
public sealed record DesyncMessage(int Tick) : NetMessage;

/// <summary>
/// Compact binary encoding of all network messages. Decoding treats input as untrusted:
/// sizes are bounded and malformed data throws <see cref="FormatException"/>.
/// </summary>
public static class NetCodec
{
    public const int MaxMessageBytes = 64 * 1024;
    private const int MaxListLength = 1024;
    private const int MaxStringBytes = 256;

    private enum Tag : byte
    {
        Hello = 1, Reject, Lobby, SetTeam, Start, Input, Frame, Checksum, Desync, CaughtUp,
    }

    private enum CommandTag : byte
    {
        Move = 1, Attack, Recruit, Build, Upgrade,
    }

    public static byte[] Encode(NetMessage message)
    {
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream, Encoding.UTF8);
        switch (message)
        {
            case HelloMessage m: w.Write((byte)Tag.Hello); WriteString(w, m.Version); WriteString(w, m.Name); WriteString(w, m.RejoinToken); break;
            case RejectMessage m: w.Write((byte)Tag.Reject); WriteString(w, m.Reason); break;
            case LobbyMessage m: w.Write((byte)Tag.Lobby); WriteSlots(w, m.Slots); break;
            case SetTeamMessage m: w.Write((byte)Tag.SetTeam); w.Write(m.Team); break;
            case StartMessage m:
                w.Write((byte)Tag.Start); w.Write(m.Seed); WriteSlots(w, m.Slots); w.Write(m.YourFaction);
                WriteString(w, m.RejoinToken); w.Write(m.Rejoin);
                break;
            case InputMessage m: w.Write((byte)Tag.Input); w.Write(m.Tick); WriteCommands(w, m.Commands); break;
            case FrameMessage m:
                w.Write((byte)Tag.Frame);
                w.Write(m.Tick);
                WriteCommands(w, m.Commands);
                w.Write((ushort)m.Takeovers.Count);
                foreach (int f in m.Takeovers)
                    w.Write(f);
                w.Write((ushort)m.Returns.Count);
                foreach (var ret in m.Returns)
                {
                    w.Write(ret.Faction);
                    w.Write(ret.FromTick);
                }
                break;
            case CaughtUpMessage m: w.Write((byte)Tag.CaughtUp); w.Write(m.Tick); break;
            case ChecksumMessage m: w.Write((byte)Tag.Checksum); w.Write(m.Tick); w.Write(m.Value); break;
            case DesyncMessage m: w.Write((byte)Tag.Desync); w.Write(m.Tick); break;
            default: throw new NotSupportedException($"Cannot encode {message.GetType().Name}.");
        }
        w.Flush();
        return stream.ToArray();
    }

    public static NetMessage Decode(byte[] data)
    {
        if (data.Length == 0 || data.Length > MaxMessageBytes)
            throw new FormatException("Bad message size.");
        try
        {
            using var r = new BinaryReader(new MemoryStream(data), Encoding.UTF8);
            NetMessage message = (Tag)r.ReadByte() switch
            {
                Tag.Hello => new HelloMessage(ReadString(r), ReadString(r), ReadString(r)),
                Tag.Reject => new RejectMessage(ReadString(r)),
                Tag.Lobby => new LobbyMessage(ReadSlots(r)),
                Tag.SetTeam => new SetTeamMessage(r.ReadByte()),
                Tag.Start => new StartMessage(r.ReadInt32(), ReadSlots(r), r.ReadInt32(), ReadString(r), r.ReadBoolean()),
                Tag.Input => new InputMessage(r.ReadInt32(), ReadCommands(r)),
                Tag.Frame => new FrameMessage(r.ReadInt32(), ReadCommands(r), ReadInts(r), ReadReturns(r)),
                Tag.CaughtUp => new CaughtUpMessage(r.ReadInt32()),
                Tag.Checksum => new ChecksumMessage(r.ReadInt32(), r.ReadUInt64()),
                Tag.Desync => new DesyncMessage(r.ReadInt32()),
                var tag => throw new FormatException($"Unknown message tag {(byte)tag}."),
            };
            if (r.BaseStream.Position != data.Length)
                throw new FormatException("Trailing bytes in message.");
            return message;
        }
        catch (EndOfStreamException e)
        {
            throw new FormatException("Truncated message.", e);
        }
    }

    private static List<FactionReturn> ReadReturns(BinaryReader r)
    {
        int count = ReadCount(r);
        var list = new List<FactionReturn>(count);
        for (int i = 0; i < count; i++)
            list.Add(new FactionReturn(r.ReadInt32(), r.ReadInt32()));
        return list;
    }

    private static void WriteString(BinaryWriter w, string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        if (bytes.Length > MaxStringBytes)
            throw new ArgumentException("String too long for the network.");
        w.Write((byte)bytes.Length);
        w.Write(bytes);
    }

    private static string ReadString(BinaryReader r)
    {
        int length = r.ReadByte();
        var bytes = r.ReadBytes(length);
        if (bytes.Length != length)
            throw new EndOfStreamException();
        return Encoding.UTF8.GetString(bytes);
    }

    private static int ReadCount(BinaryReader r)
    {
        int count = r.ReadUInt16();
        if (count > MaxListLength)
            throw new FormatException("List too long.");
        return count;
    }

    private static void WriteSlots(BinaryWriter w, IReadOnlyList<NetSlot> slots)
    {
        w.Write((ushort)slots.Count);
        foreach (var s in slots)
        {
            w.Write((byte)s.Kind);
            WriteString(w, s.Name);
            w.Write(s.Difficulty);
            w.Write(s.Team);
            w.Write(s.PeerId);
        }
    }

    private static List<NetSlot> ReadSlots(BinaryReader r)
    {
        int count = ReadCount(r);
        var slots = new List<NetSlot>(count);
        for (int i = 0; i < count; i++)
        {
            var kind = (NetSlotKind)r.ReadByte();
            if (!Enum.IsDefined(kind))
                throw new FormatException("Bad slot kind.");
            slots.Add(new NetSlot(kind, ReadString(r), r.ReadByte(), r.ReadByte(), r.ReadInt32()));
        }
        return slots;
    }

    private static List<int> ReadInts(BinaryReader r)
    {
        int count = ReadCount(r);
        var list = new List<int>(count);
        for (int i = 0; i < count; i++)
            list.Add(r.ReadInt32());
        return list;
    }

    private static void WriteIds(BinaryWriter w, IReadOnlyList<int> ids)
    {
        w.Write((ushort)ids.Count);
        foreach (int id in ids)
            w.Write(id);
    }

    private static void WriteCommands(BinaryWriter w, IReadOnlyList<Command> commands)
    {
        w.Write((ushort)commands.Count);
        foreach (var command in commands)
        {
            switch (command)
            {
                case MoveCommand c:
                    w.Write((byte)CommandTag.Move); w.Write(c.FactionId); WriteIds(w, c.UnitIds);
                    w.Write(c.Target.X); w.Write(c.Target.Y); w.Write((byte)c.Mode);
                    break;
                case AttackCommand c:
                    w.Write((byte)CommandTag.Attack); w.Write(c.FactionId); WriteIds(w, c.UnitIds); w.Write(c.TargetId);
                    break;
                case RecruitCommand c:
                    w.Write((byte)CommandTag.Recruit); w.Write(c.FactionId); w.Write(c.BuildingId); WriteString(w, c.UnitTypeId);
                    break;
                case BuildCommand c:
                    w.Write((byte)CommandTag.Build); w.Write(c.FactionId); WriteString(w, c.BuildingTypeId);
                    w.Write(c.Anchor.X); w.Write(c.Anchor.Y);
                    break;
                case UpgradeCommand c:
                    w.Write((byte)CommandTag.Upgrade); w.Write(c.FactionId); w.Write(c.BuildingId);
                    break;
                default:
                    throw new NotSupportedException($"Cannot encode {command.GetType().Name}.");
            }
        }
    }

    private static List<Command> ReadCommands(BinaryReader r)
    {
        int count = ReadCount(r);
        var commands = new List<Command>(count);
        for (int i = 0; i < count; i++)
        {
            commands.Add((CommandTag)r.ReadByte() switch
            {
                CommandTag.Move => new MoveCommand(r.ReadInt32(), ReadInts(r), ReadVector(r), ReadMode(r)),
                CommandTag.Attack => new AttackCommand(r.ReadInt32(), ReadInts(r), r.ReadInt32()),
                CommandTag.Recruit => new RecruitCommand(r.ReadInt32(), r.ReadInt32(), ReadString(r)),
                CommandTag.Build => new BuildCommand(r.ReadInt32(), ReadString(r), new Cell(r.ReadInt32(), r.ReadInt32())),
                CommandTag.Upgrade => new UpgradeCommand(r.ReadInt32(), r.ReadInt32()),
                var tag => throw new FormatException($"Unknown command tag {(byte)tag}."),
            });
        }
        return commands;
    }

    private static Vector2 ReadVector(BinaryReader r)
    {
        var v = new Vector2(r.ReadSingle(), r.ReadSingle());
        if (!float.IsFinite(v.X) || !float.IsFinite(v.Y))
            throw new FormatException("Non-finite position.");
        return v;
    }

    private static MoveMode ReadMode(BinaryReader r)
    {
        var mode = (MoveMode)r.ReadByte();
        return Enum.IsDefined(mode) ? mode : throw new FormatException("Bad move mode.");
    }
}

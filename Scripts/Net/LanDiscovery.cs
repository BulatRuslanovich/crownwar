using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Godot;

namespace Lordsio.Net;

/// <summary>A game announced on the local network. <paramref name="Id"/> tells games apart when one arrives over several adapters.</summary>
/// <paramref name="InMatch"/>: the match is running; <paramref name="FreeSlots"/> then counts players who dropped out and may rejoin.
public sealed record LanGame(string Id, string Address, int Port, string HostName, string Version, int Players, int FreeSlots, bool InMatch)
{
    public double LastSeen { get; set; }
}

/// <summary>
/// Finds games on the local network, like Minecraft's "LAN worlds": a host broadcasts a small UDP
/// announcement every second; menus listen and list what they hear. Joining itself goes over ENet.
/// </summary>
public partial class LanDiscovery : Node
{
    public const int DiscoveryPort = 47801;
    private const string Magic = "LORDSIO1";
    private const double AnnounceInterval = 1.0;
    /// <summary>Games not heard from for this long disappear from the list.</summary>
    private const double ForgetAfter = 4.0;

    private readonly Dictionary<string, LanGame> _games = new();
    private PacketPeerUdp? _socket;
    private Func<(string HostName, int Players, int FreeSlots, bool InMatch)>? _announcement;
    private int _gamePort;
    private string _gameId = "";
    private double _sinceAnnounce;

    /// <summary>False when the discovery port is taken (e.g. a second copy on the same PC): use the IP field.</summary>
    public bool IsListening { get; private set; }

    public IReadOnlyCollection<LanGame> Games => _games.Values;

    /// <summary>Starts listening for announcements.</summary>
    public void Listen()
    {
        Stop();
        _socket = new PacketPeerUdp();
        IsListening = _socket.Bind(DiscoveryPort) == Error.Ok;
    }

    /// <summary>Starts announcing a hosted game; <paramref name="describe"/> is asked for fresh details each time.</summary>
    public void Announce(int gamePort, Func<(string HostName, int Players, int FreeSlots, bool InMatch)> describe)
    {
        Stop();
        _socket = new PacketPeerUdp();
        _socket.SetBroadcastEnabled(true);
        _gamePort = gamePort;
        _gameId = GD.Randi().ToString("x8", CultureInfo.InvariantCulture);
        _announcement = describe;
        _sinceAnnounce = AnnounceInterval; // announce right away
    }

    public void Stop()
    {
        _socket?.Close();
        _socket = null;
        _announcement = null;
        IsListening = false;
        _games.Clear();
    }

    public override void _ExitTree() => Stop();

    public override void _Process(double delta)
    {
        if (_socket == null)
            return;

        if (_announcement != null)
        {
            _sinceAnnounce += delta;
            if (_sinceAnnounce >= AnnounceInterval)
            {
                _sinceAnnounce = 0;
                var (hostName, players, free, inMatch) = _announcement();
                string text = string.Join('|', Magic, GameVersion.Current, Sanitize(hostName),
                    players.ToString(CultureInfo.InvariantCulture), free.ToString(CultureInfo.InvariantCulture),
                    _gamePort.ToString(CultureInfo.InvariantCulture), _gameId, inMatch ? "1" : "0");
                byte[] packet = Encoding.UTF8.GetBytes(text);
                foreach (string target in BroadcastTargets())
                {
                    _socket.SetDestAddress(target, DiscoveryPort);
                    _socket.PutPacket(packet);
                }
            }
            return;
        }

        double now = Time.GetTicksMsec() / 1000.0;
        while (IsListening && _socket.GetAvailablePacketCount() > 0)
        {
            byte[] packet = _socket.GetPacket();
            string address = _socket.GetPacketIP();
            if (packet.Length > 512 || TryParse(Encoding.UTF8.GetString(packet), address) is not { } game)
                continue;
            // Keep the address first heard from, so the entry does not flicker between adapters.
            if (_games.TryGetValue(game.Id, out var known))
                game = game with { Address = known.Address };
            game.LastSeen = now;
            _games[game.Id] = game;
        }
        foreach (var key in new List<string>(_games.Keys))
        {
            if (now - _games[key].LastSeen > ForgetAfter)
                _games.Remove(key);
        }
    }

    private static LanGame? TryParse(string text, string address)
    {
        var parts = text.Split('|');
        if (parts.Length != 8 || parts[0] != Magic)
            return null;
        if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int players)
            || !int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int free)
            || !int.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int port))
            return null;
        return new LanGame(parts[6], address, port, parts[2], parts[1], players, free, parts[7] == "1");
    }

    /// <summary>
    /// Windows sends 255.255.255.255 out of one adapter only, often a virtual one (Hyper-V, VPN), so the
    /// announcement also goes to each local subnet's broadcast address. Subnets are assumed to be /24,
    /// as on nearly every home router; the global broadcast still covers the rest.
    /// </summary>
    private static IEnumerable<string> BroadcastTargets()
    {
        var targets = new HashSet<string> { "255.255.255.255" };
        foreach (string address in IP.GetLocalAddresses())
        {
            var parts = address.Split('.');
            if (parts.Length == 4 && parts[0] != "127" && !(parts[0] == "169" && parts[1] == "254"))
                targets.Add($"{parts[0]}.{parts[1]}.{parts[2]}.255");
        }
        return targets;
    }

    private static string Sanitize(string name) => name.Replace('|', '/');
}

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Warcrown.Core;
using Warcrown.Sim.AI;
using Warcrown.Sim.Core;
using Warcrown.Sim.Net;

namespace Warcrown.Net;

/// <summary>
/// A network game from lobby to the end of the match, over ENet (reliable, ordered).
/// The host owns the lobby: it assigns slots to joining players, lets them pick teams, adds bots and
/// starts the match. During the match this node only carries lockstep messages (see <see cref="NetMatchDriver"/>).
/// Peer id 1 is always the host.
/// </summary>
public partial class NetSession : Node
{
    public const int GamePort = 47800;
    public const int MaxSlots = 4;
    private const double ConnectTimeout = 6.0;
    private const int PeerTimeoutLimit = 32;
    private const int PeerTimeoutMinMs = 3000;
    private const int PeerTimeoutMaxMs = 8000;

    private ENetMultiplayerPeer? _peer;
    private LanDiscovery? _discovery;
    private string _localName = "";
    private bool _helloSent;
    private double _connectingFor;
    private readonly HashSet<int> _greeted = new();
    private readonly Dictionary<int, int> _factionOfPeer = new();
    /// <summary>Host, during the match: each human faction's rejoin key and player name.</summary>
    private readonly Dictionary<int, (string Token, string Name)> _players = new();
    /// <summary>Host, during the match: human factions whose player is gone (a bot plays them) and may come back.</summary>
    private readonly HashSet<int> _departed = new();
    /// <summary>Client: lockstep messages that arrived before the match (and its driver) was set up.</summary>
    private readonly List<NetMessage> _early = new();

    public bool IsHost { get; private set; }
    public bool InMatch { get; private set; }
    public int LocalPeerId => _peer?.GetUniqueId() ?? 0;

    /// <summary>The lobby as the host sees it (clients receive copies).</summary>
    public List<NetSlot> Slots { get; private set; } = new();

    /// <summary>The mode the host picked for the match (clients receive it with the lobby).</summary>
    public MatchMode Mode { get; private set; }

    public event Action? LobbyUpdated;
    /// <summary>The match is starting; the message carries this machine's faction.</summary>
    public event Action<StartMessage>? MatchStarting;
    /// <summary>The session failed or was closed by the other side; the argument is a translation key.</summary>
    public event Action<string>? Failed;
    /// <summary>Lockstep traffic during the match: (sender peer id, message).</summary>
    public event Action<int, NetMessage>? MessageReceived;
    /// <summary>Host only: a player left mid-match; the argument is their faction.</summary>
    public event Action<int>? PlayerLeftMatch;
    /// <summary>Host only: a player who had left is back (peer id, faction) and must be sent the match so far.</summary>
    public event Action<int, int>? PlayerRejoining;

    /// <summary>Host: the seed and factions the match started with, for players who rejoin.</summary>
    public StartMessage? StartedWith { get; private set; }

    /// <summary>Host: the saved match this session brought back after a crash (its driver replays these frames).</summary>
    public SavedMatch? Resumed { get; private set; }

    /// <summary>Host, during the match: every human player (faction, rejoin key, name), for the save.</summary>
    public IEnumerable<SavedPlayer> MatchPlayers => _players.Select(p => new SavedPlayer(p.Key, p.Value.Token, p.Value.Name));

    /// <summary>Host: the name of the player at a peer (the host's own for peer 1).</summary>
    public string NameOfPeer(int peerId) =>
        peerId == 1 ? _localName
        : _factionOfPeer.TryGetValue(peerId, out int faction) && _players.TryGetValue(faction, out var p) ? p.Name
        : "?";

    public Error Host(string playerName)
    {
        Close();
        _peer = new ENetMultiplayerPeer();
        var error = _peer.CreateServer(GamePort, MaxSlots - 1);
        if (error != Error.Ok)
        {
            _peer = null;
            return error;
        }
        Wire();
        IsHost = true;
        _localName = Trim(playerName);
        Slots = new List<NetSlot> { new(NetSlotKind.Human, _localName, (byte)BotDifficulty.Normal, 0, 1) };
        for (int i = 1; i < MaxSlots; i++)
            Slots.Add(new NetSlot(NetSlotKind.Empty, "", (byte)BotDifficulty.Normal, (byte)i, 0));

        _discovery = new LanDiscovery();
        AddChild(_discovery);
        // Announced in the lobby (free slots) and during the match (places of players who dropped out).
        _discovery.Announce(GamePort, () => InMatch
            ? (_localName, _players.Count - _departed.Count, _departed.Count, true)
            : (_localName, Slots.Count(s => s.Kind == NetSlotKind.Human), Slots.Count(s => s.Kind == NetSlotKind.Empty), false));
        LobbyUpdated?.Invoke();
        return Error.Ok;
    }

    public Error Join(string address, string playerName)
    {
        Close();
        _peer = new ENetMultiplayerPeer();
        var error = _peer.CreateClient(address.Trim(), GamePort);
        if (error != Error.Ok)
        {
            _peer = null;
            return error;
        }
        Wire();
        IsHost = false;
        _localName = Trim(playerName);
        _connectingFor = 0;
        return Error.Ok;
    }

    public void Close()
    {
        if (_peer != null)
        {
            _peer.PeerConnected -= OnPeerConnected;
            _peer.PeerDisconnected -= OnPeerDisconnected;
            _peer.Close();
            _peer = null;
        }
        _discovery?.QueueFree();
        _discovery = null;
        _greeted.Clear();
        _factionOfPeer.Clear();
        _players.Clear();
        _departed.Clear();
        _early.Clear();
        _delayed.Clear();
        _lastDueMs = 0;
        _connectingFor = 0;
        StartedWith = null;
        Resumed = null;
        Slots = new List<NetSlot>();
        Mode = MatchMode.Versus;
        _helloSent = false;
        IsHost = false;
        InMatch = false;
    }

    public override void _ExitTree() => Close();

    /// <summary>
    /// Receives and handles what has arrived. Runs every frame, and once more right before the match steps,
    /// so a frame that came in this frame is used now and not a frame later.
    /// </summary>
    public void Receive()
    {
        if (PollPeer())
            ReadPackets();
    }

    /// <summary>Sends what is due and lets ENet take in what has arrived; false when there is (no longer) a session.</summary>
    [System.Diagnostics.CodeAnalysis.MemberNotNullWhen(true, nameof(_peer))]
    private bool PollPeer()
    {
        if (_peer == null)
            return false;
        SendDue();
        _peer.Poll();
        return _peer != null; // a handler may have closed the session
    }

    public override void _Process(double delta)
    {
        if (!PollPeer())
            return;

        if (!IsHost && !_helloSent)
        {
            _connectingFor += delta;
            if (_peer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Disconnected || _connectingFor > ConnectTimeout)
            {
                Fail("NET_CONNECT_FAILED");
                return;
            }
        }
        ReadPackets();
    }

    private void ReadPackets()
    {
        while (_peer != null && _peer.GetAvailablePacketCount() > 0)
        {
            int from = _peer.GetPacketPeer();
            byte[] data = _peer.GetPacket();
            NetMessage message;
            try
            {
                message = NetCodec.Decode(data);
            }
            catch (FormatException)
            {
                if (IsHost)
                    _peer.DisconnectPeer(from); // garbage from a client: drop it
                continue;
            }
            if (IsHost)
                HandleAsHost(from, message);
            else
                HandleAsClient(message);
        }
    }

    private void HandleAsClient(NetMessage message)
    {
        switch (message)
        {
            case LobbyMessage lobby:
                Slots = lobby.Slots.ToList();
                Mode = lobby.Mode;
                LobbyUpdated?.Invoke();
                break;
            case RejectMessage reject:
                Fail(reject.Reason);
                break;
            case StartMessage start:
                InMatch = true;
                RejoinStore.Save(start.RejoinToken); // survives a crash, so the player can come back
                MatchStarting?.Invoke(start);
                break;
            case FrameMessage or DesyncMessage or PauseMessage:
                if (MessageReceived == null)
                    _early.Add(message); // the match is still being built; its driver will ask for them
                else
                    MessageReceived.Invoke(1, message);
                break;
        }
    }

    private void Wire()
    {
        _peer!.PeerConnected += OnPeerConnected;
        _peer.PeerDisconnected += OnPeerDisconnected;
    }

    private void OnPeerConnected(long id)
    {
        // A player whose game crashed sends no goodbye: notice the silence after seconds, not ENet's default 30 s,
        // since everyone else's match stands still meanwhile.
        _peer!.GetPeer((int)id)?.SetTimeout(PeerTimeoutLimit, PeerTimeoutMinMs, PeerTimeoutMaxMs);
        if (!IsHost && id == 1 && !_helloSent)
        {
            _helloSent = true;
            Send(1, new HelloMessage(GameVersion.Current, _localName, RejoinStore.Load()));
        }
    }

    private void OnPeerDisconnected(long id)
    {
        int peer = (int)id;
        if (!IsHost)
        {
            if (peer == 1)
                Fail(InMatch ? "NET_HOST_LEFT" : "NET_HOST_CLOSED");
            return;
        }

        _greeted.Remove(peer);
        if (InMatch)
        {
            if (_factionOfPeer.Remove(peer, out int faction))
            {
                _departed.Add(faction);
                PlayerLeftMatch?.Invoke(faction);
            }
            return;
        }
        int slot = Slots.FindIndex(s => s.PeerId == peer);
        if (slot > 0)
        {
            Slots[slot] = new NetSlot(NetSlotKind.Empty, "", (byte)BotDifficulty.Normal, Slots[slot].Team, 0);
            BroadcastLobby();
        }
    }

    private void Fail(string reasonKey)
    {
        Close();
        Failed?.Invoke(reasonKey);
    }

    private static string Trim(string name) => PlayerNames.Sanitize(name);
}

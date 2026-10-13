using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Lordsio.Sim.AI;
using Lordsio.Sim.Net;

namespace Lordsio.Net;

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

    public bool IsHost { get; private set; }
    public bool InMatch { get; private set; }
    public int LocalPeerId => _peer?.GetUniqueId() ?? 0;

    /// <summary>The lobby as the host sees it (clients receive copies).</summary>
    public List<NetSlot> Slots { get; private set; } = new();

    public event Action? LobbyUpdated;
    /// <summary>The match is starting; the message carries this machine's faction.</summary>
    public event Action<StartMessage>? MatchStarting;
    /// <summary>The session failed or was closed by the other side; the argument is a translation key.</summary>
    public event Action<string>? Failed;
    /// <summary>Lockstep traffic during the match: (sender peer id, message).</summary>
    public event Action<int, NetMessage>? MessageReceived;
    /// <summary>Host only: a player left mid-match; the argument is their faction.</summary>
    public event Action<int>? PlayerLeftMatch;

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
        _discovery.Announce(GamePort, () => (_localName, Slots.Count(s => s.Kind == NetSlotKind.Human), Slots.Count(s => s.Kind == NetSlotKind.Empty)));
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
        _helloSent = false;
        InMatch = false;
    }

    public override void _ExitTree() => Close();

    // ---------------------------------------------------------------- lobby (host)

    /// <summary>Host: turn a non-player slot into a bot of the given difficulty, or empty it.</summary>
    public void SetSlot(int index, NetSlotKind kind, BotDifficulty difficulty)
    {
        if (!IsHost || InMatch || index <= 0 || index >= Slots.Count || Slots[index].Kind == NetSlotKind.Human || kind == NetSlotKind.Human)
            return;
        Slots[index] = Slots[index] with { Kind = kind, Difficulty = (byte)difficulty, Name = "" };
        BroadcastLobby();
    }

    /// <summary>Host: move any slot to a team. Clients use <see cref="RequestTeam"/> for their own slot.</summary>
    public void SetTeam(int index, int team)
    {
        if (!IsHost || InMatch || index < 0 || index >= Slots.Count)
            return;
        Slots[index] = Slots[index] with { Team = (byte)Math.Clamp(team, 0, MaxSlots - 1) };
        BroadcastLobby();
    }

    public void RequestTeam(int team)
    {
        if (IsHost)
            SetTeam(0, team);
        else
            Send(1, new SetTeamMessage((byte)Math.Clamp(team, 0, MaxSlots - 1)));
    }

    /// <summary>At least two occupied slots on at least two different teams.</summary>
    public bool CanStart
    {
        get
        {
            var used = Slots.Where(s => s.Kind != NetSlotKind.Empty).ToList();
            return used.Count >= 2 && used.Select(s => s.Team).Distinct().Count() >= 2;
        }
    }

    /// <summary>Host: start the match. Empty slots are dropped; slot order becomes faction order.</summary>
    public void StartMatch()
    {
        if (!IsHost || InMatch || !CanStart)
            return;
        var factions = Slots.Where(s => s.Kind != NetSlotKind.Empty).ToList();
        int seed = (int)(GD.Randi() & int.MaxValue);
        InMatch = true;
        _discovery?.Stop();
        for (int faction = 0; faction < factions.Count; faction++)
        {
            var slot = factions[faction];
            if (slot.Kind != NetSlotKind.Human)
                continue;
            _factionOfPeer[slot.PeerId] = faction;
            if (slot.PeerId != 1)
                Send(slot.PeerId, new StartMessage(seed, factions, faction));
        }
        MatchStarting?.Invoke(new StartMessage(seed, factions, _factionOfPeer[1]));
    }

    // ---------------------------------------------------------------- messaging

    public void Send(int peerId, NetMessage message)
    {
        if (_peer == null)
            return;
        _peer.TransferMode = MultiplayerPeer.TransferModeEnum.Reliable;
        _peer.SetTargetPeer(peerId);
        _peer.PutPacket(NetCodec.Encode(message));
    }

    public void Broadcast(NetMessage message) => Send((int)MultiplayerPeer.TargetPeerBroadcast, message);

    /// <summary>Host: the faction a connected player controls in the match.</summary>
    public int? FactionOf(int peerId) => _factionOfPeer.TryGetValue(peerId, out int f) ? f : null;

    public override void _Process(double delta)
    {
        if (_peer == null)
            return;
        _peer.Poll();
        if (_peer == null)
            return; // a handler closed the session

        if (!IsHost && !_helloSent)
        {
            _connectingFor += delta;
            if (_peer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Disconnected || _connectingFor > ConnectTimeout)
            {
                Fail("NET_CONNECT_FAILED");
                return;
            }
        }

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

    private void HandleAsHost(int from, NetMessage message)
    {
        switch (message)
        {
            case HelloMessage hello:
                if (hello.Version != GameVersion.Current)
                    Reject(from, "NET_REJECT_VERSION");
                else if (InMatch)
                    Reject(from, "NET_REJECT_STARTED");
                else if (Slots.FindIndex(s => s.Kind == NetSlotKind.Empty) is var free and >= 0)
                {
                    _greeted.Add(from);
                    Slots[free] = Slots[free] with { Kind = NetSlotKind.Human, Name = Trim(hello.Name), PeerId = from };
                    BroadcastLobby();
                }
                else
                    Reject(from, "NET_REJECT_FULL");
                break;
            case SetTeamMessage team when !InMatch && Slots.FindIndex(s => s.PeerId == from) is var own and >= 0:
                SetTeam(own, team.Team);
                break;
            case InputMessage or ChecksumMessage when InMatch && _greeted.Contains(from):
                MessageReceived?.Invoke(from, message);
                break;
        }
    }

    private void HandleAsClient(NetMessage message)
    {
        switch (message)
        {
            case LobbyMessage lobby:
                Slots = lobby.Slots.ToList();
                LobbyUpdated?.Invoke();
                break;
            case RejectMessage reject:
                Fail(reject.Reason);
                break;
            case StartMessage start:
                InMatch = true;
                MatchStarting?.Invoke(start);
                break;
            case FrameMessage or DesyncMessage:
                MessageReceived?.Invoke(1, message);
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
            Send(1, new HelloMessage(GameVersion.Current, _localName));
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
            if (_factionOfPeer.TryGetValue(peer, out int faction))
                PlayerLeftMatch?.Invoke(faction);
            return;
        }
        int slot = Slots.FindIndex(s => s.PeerId == peer);
        if (slot > 0)
        {
            Slots[slot] = new NetSlot(NetSlotKind.Empty, "", (byte)BotDifficulty.Normal, Slots[slot].Team, 0);
            BroadcastLobby();
        }
    }

    private void BroadcastLobby()
    {
        Broadcast(new LobbyMessage(Slots));
        LobbyUpdated?.Invoke();
    }

    private void Reject(int peer, string reason)
    {
        Send(peer, new RejectMessage(reason));
        // Let the message leave before dropping the connection.
        _peer?.DisconnectPeer(peer, force: false);
    }

    private void Fail(string reasonKey)
    {
        Close();
        Failed?.Invoke(reasonKey);
    }

    private static string Trim(string name)
    {
        name = name.Trim();
        if (name.Length == 0)
            name = "Player";
        return name.Length > 16 ? name[..16] : name;
    }
}

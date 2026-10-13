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

    /// <summary>Host: a faction's rejoin key.</summary>
    public string TokenOf(int faction) => _players.TryGetValue(faction, out var p) ? p.Token : "";

    /// <summary>Delivers lockstep messages that arrived before anyone listened (the match was still being built).</summary>
    public void FlushEarlyMessages()
    {
        var early = _early.ToList();
        _early.Clear();
        foreach (var message in early)
            MessageReceived?.Invoke(1, message);
    }

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
        StartedWith = null;
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
        for (int faction = 0; faction < factions.Count; faction++)
        {
            var slot = factions[faction];
            if (slot.Kind != NetSlotKind.Human)
                continue;
            _factionOfPeer[slot.PeerId] = faction;
            string token = GD.Randi().ToString("x8") + GD.Randi().ToString("x8");
            _players[faction] = (token, slot.Name);
            if (slot.PeerId != 1)
                Send(slot.PeerId, new StartMessage(seed, factions, faction, token));
        }
        StartedWith = new StartMessage(seed, factions, _factionOfPeer[1], TokenOf(_factionOfPeer[1]));
        MatchStarting?.Invoke(StartedWith);
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
                else if (InMatch && ReturningFaction(hello) is { } back)
                {
                    _greeted.Add(from);
                    _factionOfPeer[from] = back;
                    _departed.Remove(back);
                    PlayerRejoining?.Invoke(from, back);
                }
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
            case InputMessage or ChecksumMessage or CaughtUpMessage when InMatch && _greeted.Contains(from):
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
                RejoinStore.Save(start.RejoinToken); // survives a crash, so the player can come back
                MatchStarting?.Invoke(start);
                break;
            case FrameMessage or DesyncMessage:
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

    /// <summary>
    /// The faction a connecting player may take back: the one whose rejoin key they bring, or else the
    /// only departed player with their name (if the key file was lost).
    /// </summary>
    private int? ReturningFaction(HelloMessage hello)
    {
        foreach (int faction in _departed)
            if (hello.RejoinToken.Length > 0 && _players[faction].Token == hello.RejoinToken)
                return faction;
        var byName = _departed.Where(f => _players[f].Name == Trim(hello.Name)).ToList();
        return byName.Count == 1 ? byName[0] : null;
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

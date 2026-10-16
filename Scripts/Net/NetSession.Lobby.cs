using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Warcrown.Core;
using Warcrown.Sim.AI;
using Warcrown.Sim.Core;
using Warcrown.Sim.Net;

namespace Warcrown.Net;

/// <summary>The host's lobby: slots, teams, newcomers saying hello, and the start of the match.</summary>
public partial class NetSession
{
    /// <summary>
    /// Host: pick the mode. Defense is for people only (the waves are the enemy), so its bot slots are opened again;
    /// teams do not matter there (everyone defends together).
    /// </summary>
    public void SetMode(MatchMode mode)
    {
        if (!IsHost || InMatch || mode == Mode)
            return;
        Mode = mode;
        if (mode == MatchMode.Defense)
            for (int i = 0; i < Slots.Count; i++)
                if (Slots[i].Kind == NetSlotKind.Bot)
                    Slots[i] = Slots[i] with { Kind = NetSlotKind.Empty };
        BroadcastLobby();
    }

    /// <summary>Host: turn a non-player slot into a bot of the given difficulty (not in Defense), or empty it.</summary>
    public void SetSlot(int index, NetSlotKind kind, BotDifficulty difficulty)
    {
        if (!IsHost || InMatch || index <= 0 || index >= Slots.Count || Slots[index].Kind == NetSlotKind.Human || kind == NetSlotKind.Human
            || (kind == NetSlotKind.Bot && Mode == MatchMode.Defense))
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

    /// <summary>Versus: at least two occupied slots on at least two different teams. Defense: anyone at all.</summary>
    public bool CanStart
    {
        get
        {
            var used = Slots.Where(s => s.Kind != NetSlotKind.Empty).ToList();
            if (Mode == MatchMode.Defense)
                return used.Count >= 1;
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
                Send(slot.PeerId, new StartMessage(seed, factions, faction, token, Mode: Mode));
        }
        StartedWith = new StartMessage(seed, factions, _factionOfPeer[1], TokenOf(_factionOfPeer[1]), Mode: Mode);
        MatchStarting?.Invoke(StartedWith);
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
            case InputMessage or ChecksumMessage or CaughtUpMessage or PauseMessage when InMatch && _greeted.Contains(from):
                MessageReceived?.Invoke(from, message);
                break;
        }
    }

    private void BroadcastLobby()
    {
        Broadcast(new LobbyMessage(Slots, Mode));
        LobbyUpdated?.Invoke();
    }

    private void Reject(int peer, string reason)
    {
        Send(peer, new RejectMessage(reason));
        // Let the message leave before dropping the connection.
        _peer?.DisconnectPeer(peer, force: false);
    }
}

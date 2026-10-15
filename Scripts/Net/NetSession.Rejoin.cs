using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Lordsio.Core;
using Lordsio.Sim.AI;
using Lordsio.Sim.Net;

namespace Lordsio.Net;

/// <summary>Coming back: a host resuming a saved match, and players rejoining with their key.</summary>
public partial class NetSession
{
    /// <summary>
    /// Hosts a saved match again after a crash: the server starts already in the match, every other player counts as
    /// departed (a bot plays them) and can rejoin with their key as usual; the host replays the save and plays on.
    /// </summary>
    public Error HostResume(string playerName, SavedMatch save)
    {
        var error = Host(playerName);
        if (error != Error.Ok)
            return error;
        Resumed = save;
        int own = save.Start.YourFaction;
        foreach (var player in save.Players)
        {
            _players[player.Faction] = (player.Token, player.Name);
            if (player.Faction != own)
                _departed.Add(player.Faction);
        }
        _factionOfPeer[1] = own;
        InMatch = true;
        StartedWith = save.Start;
        MatchStarting?.Invoke(save.Start with { Rejoin = true });
        return Error.Ok;
    }

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
}

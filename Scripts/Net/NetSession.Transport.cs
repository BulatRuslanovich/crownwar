using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Lordsio.Core;
using Lordsio.Sim.AI;
using Lordsio.Sim.Net;

namespace Lordsio.Net;

/// <summary>Sending: ordered reliable packets, the dev lag simulation, flushing, and what ENet knows of each link.</summary>
public partial class NetSession
{
    /// <summary>Dev (--net-lag): outgoing packets are held back this long, plus up to <see cref="DevJitterMs"/>, to test a poor link on one PC.</summary>
    public int DevLagMs { get; set; }
    public int DevJitterMs { get; set; }
    private readonly Queue<(ulong DueMs, int PeerId, byte[] Data)> _delayed = new();
    private ulong _lastDueMs;
    private readonly Random _devRandom = new();

    public void Send(int peerId, NetMessage message)
    {
        if (_peer == null)
            return;
        byte[] data = NetCodec.Encode(message);
        if (DevLagMs > 0 || DevJitterMs > 0)
        {
            // Never before an earlier packet: the channel is ordered anyway.
            _lastDueMs = Math.Max(_lastDueMs, Time.GetTicksMsec() + (ulong)(DevLagMs + _devRandom.Next(DevJitterMs + 1)));
            _delayed.Enqueue((_lastDueMs, peerId, data));
            return;
        }
        Put(peerId, data);
    }

    private void Put(int peerId, byte[] data)
    {
        _peer!.TransferMode = MultiplayerPeer.TransferModeEnum.Reliable;
        _peer.SetTargetPeer(peerId);
        _peer.PutPacket(data);
    }

    private void SendDue()
    {
        while (_peer != null && _delayed.TryPeek(out var next) && next.DueMs <= Time.GetTicksMsec())
        {
            _delayed.Dequeue();
            Put(next.PeerId, next.Data);
        }
    }

    public void Broadcast(NetMessage message) => Send((int)MultiplayerPeer.TargetPeerBroadcast, message);

    /// <summary>Host: the faction a connected player controls in the match.</summary>
    public int? FactionOf(int peerId) => _factionOfPeer.TryGetValue(peerId, out int f) ? f : null;

    /// <summary>
    /// Sends what was queued right away instead of on the next frame's poll: in lockstep every hop of the
    /// round trip counts against the input delay.
    /// </summary>
    public void Flush()
    {
        SendDue();
        _peer?.Host?.Flush();
    }

    /// <summary>Round-trip time and its variance (ms) to a connected peer, as ENet measures them.</summary>
    public (double RoundTrip, double Variance)? LinkTo(int peerId)
    {
        if (_peer?.GetPeer(peerId) is not { } link || !link.IsActive())
            return null;
        return (link.GetStatistic(ENetPacketPeer.PeerStatistic.RoundTripTime),
            link.GetStatistic(ENetPacketPeer.PeerStatistic.RoundTripTimeVariance));
    }

    /// <summary>Host: the other players' peers in the match (the host itself, peer 1, is not a link).</summary>
    public IEnumerable<int> MatchPeers => _factionOfPeer.Keys.Where(id => id != 1);
}

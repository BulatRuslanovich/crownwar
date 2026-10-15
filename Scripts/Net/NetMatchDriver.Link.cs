using System;
using System.Collections.Generic;
using System.Linq;
using Lordsio.Sim.Core;
using Lordsio.Sim.Net;

namespace Lordsio.Net;

/// <summary>The link: ping and jitter, and the input delay a client adapts to them and to stalls.</summary>
public sealed partial class NetMatchDriver
{
    /// <summary>
    /// What a client's input needs on top of the round trip to reach the host in time: the client runs up to a
    /// tick behind the host, plus a rendered frame on each side, plus a margin.
    /// </summary>
    private const double PipelineMilliseconds = 100;
    /// <summary>The input delay is raised at once when the link gets worse, but lowered only after this many good checks in a row.</summary>
    private const int LowerAfterChecks = 5;
    /// <summary>A delay raised because the match stalled is kept at least this many checks (seconds) per extra tick.</summary>
    private const int StallFloorChecks = 60;
    private double _sinceLinkCheck;
    private int _goodChecks;
    private int _stallsSinceCheck;
    /// <summary>Client: the input delay the stalls have shown to be needed, whatever the measured round trip says.</summary>
    private int _stallFloor = LockstepHost.InputDelay;
    private int _checksSinceStall;

    /// <summary>Ticks ahead our commands are sent (a client's grows with a slow or jittery link).</summary>
    public int InputDelay => _peer.InputDelay;

    /// <summary>Round trip to the host (client) or to the slowest player (host), and its variance, in ms; -1 if unknown.</summary>
    public double PingMilliseconds { get; private set; } = -1;
    public double JitterMilliseconds { get; private set; } = -1;

    /// <summary>Frames already received beyond the current tick.</summary>
    public int BufferedFrames(int currentTick) => Math.Max(0, _peer.LatestFrameTick - currentTick);

    /// <summary>The match had to stop for a whole tick waiting for a frame: the client's commands may arrive too late.</summary>
    public void ReportStall() => _stallsSinceCheck++;

    /// <summary>
    /// Measures the link; a client sends its commands far enough ahead that they reach the host before the host
    /// needs them (round trip + four variances + the pipeline), so the match does not stall on every spike.
    /// Stalls the round trip does not explain (a busy PC, a link ENet measures as fine) raise the delay too,
    /// and keep it up for a while.
    /// </summary>
    private void CheckLink()
    {
        int stalls = _stallsSinceCheck;
        _stallsSinceCheck = 0;
        if (LogChecksums)
            Godot.GD.Print($"[net] link: ping {PingMilliseconds:0} ±{JitterMilliseconds:0} ms, input delay {_peer.InputDelay}, stalls {stalls}");
        var links = (_host == null ? new[] { 1 } : _session.MatchPeers)
            .Select(id => _session.LinkTo(id)).OfType<(double RoundTrip, double Variance)>().ToList();
        if (links.Count > 0)
        {
            var worst = links.MaxBy(l => l.RoundTrip + 4 * l.Variance);
            PingMilliseconds = worst.RoundTrip;
            JitterMilliseconds = worst.Variance;
        }
        if (_host != null || IsRejoin && !_caughtUpReported)
            return; // the host's own commands reach it at once; a replay is not real time

        if (stalls > 0)
        {
            _stallFloor = Math.Min(LockstepPeer.MaxInputDelay, Math.Max(_stallFloor, _peer.InputDelay) + 1);
            _checksSinceStall = 0;
        }
        else if (++_checksSinceStall >= StallFloorChecks && _stallFloor > LockstepHost.InputDelay)
        {
            _stallFloor--;
            _checksSinceStall = 0;
        }

        double needed = PingMilliseconds >= 0 ? PingMilliseconds + 4 * JitterMilliseconds + PipelineMilliseconds : 0;
        int wanted = Math.Max(_stallFloor, (int)Math.Ceiling(needed / (Simulation.TickDuration * 1000)));
        if (wanted >= _peer.InputDelay)
        {
            _peer.InputDelay = wanted;
            _goodChecks = 0;
        }
        else if (++_goodChecks >= LowerAfterChecks)
        {
            _peer.InputDelay--;
            _goodChecks = 0;
        }
    }
}

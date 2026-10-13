using System;
using System.Collections.Generic;
using System.Linq;
using Lordsio.Sim.Core;
using Lordsio.Sim.Net;

namespace Lordsio.Net;

/// <summary>
/// Lockstep for one networked match, on top of <see cref="NetSession"/>. Every machine runs the full
/// simulation; only commands travel. Local commands are batched per tick and sent to the host, which
/// merges everyone's input into frames and sends them back; a tick is simulated only once its frame is
/// known. Checksums are compared every second to detect a desync.
/// </summary>
public sealed class NetMatchDriver
{
    /// <summary>Ticks between checksum reports.</summary>
    public const int ChecksumInterval = Simulation.TicksPerSecond;

    private readonly NetSession _session;
    private readonly int _localFaction;
    private readonly LockstepPeer _peer = new();
    private readonly LockstepHost? _host;
    private readonly ChecksumTracker? _checksums;
    /// <summary>Host: every frame of the match, for players who rejoin.</summary>
    private readonly List<FrameMessage> _history = new();
    private bool _caughtUpReported;

    /// <summary>Ticks between a returning player's "caught up" and taking the faction back (one second).</summary>
    private const int ReturnDelay = Simulation.TicksPerSecond;

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

    public NetMatchDriver(NetSession session, StartMessage start)
    {
        _session = session;
        _localFaction = start.YourFaction;
        if (session.IsHost)
        {
            var humans = start.Slots.Select((slot, faction) => (slot, faction))
                .Where(p => p.slot.Kind == NetSlotKind.Human).Select(p => p.faction);
            _host = new LockstepHost(humans);
            _checksums = new ChecksumTracker();
            session.PlayerLeftMatch += faction => _host.DropFaction(faction);
            session.PlayerRejoining += SendMatchSoFar;
        }
        IsRejoin = start.Rejoin;
        session.MessageReceived += OnMessage;
        session.FlushEarlyMessages();
    }

    public bool IsHost => _host != null;

    /// <summary>This machine came back into a running match and is replaying it.</summary>
    public bool IsRejoin { get; }

    /// <summary>The newest frame known: how far the match has gone.</summary>
    public int LatestFrameTick => _peer.LatestFrameTick;

    /// <summary>Returning player: the replay has reached the others; the host may hand the faction back.</summary>
    public void ReportCaughtUp(int tick)
    {
        if (!IsRejoin || _caughtUpReported)
            return;
        _caughtUpReported = true;
        _session.Send(1, new CaughtUpMessage(tick));
    }

    /// <summary>Host: a player is back; send them the start of the match and every frame so far.</summary>
    private void SendMatchSoFar(int peer, int faction)
    {
        if (_session.StartedWith is not { } start)
            return;
        if (LogChecksums)
            Godot.GD.Print($"[net] faction {faction} rejoining: sending {_history.Count} frames");
        _session.Send(peer, start with { YourFaction = faction, RejoinToken = _session.TokenOf(faction), Rejoin = true });
        foreach (var frame in _history)
            _session.Send(peer, frame);
    }

    /// <summary>Raised once when the machines' simulations diverged; the argument is the tick.</summary>
    public event Action<int>? DesyncDetected;

    public bool IsDesynced { get; private set; }

    /// <summary>Dev: print every checksum, to compare two instances' logs.</summary>
    public bool LogChecksums { get; set; }

    public void QueueLocal(Command command) => _peer.QueueLocal(command);

    /// <summary>Ticks ahead our commands are sent (a client's grows with a slow or jittery link).</summary>
    public int InputDelay => _peer.InputDelay;

    /// <summary>Round trip to the host (client) or to the slowest player (host), and its variance, in ms; -1 if unknown.</summary>
    public double PingMilliseconds { get; private set; } = -1;
    public double JitterMilliseconds { get; private set; } = -1;

    /// <summary>Frames already received beyond the current tick.</summary>
    public int BufferedFrames(int currentTick) => Math.Max(0, _peer.LatestFrameTick - currentTick);

    /// <summary>
    /// Before the match steps: take in what has arrived; the host turns complete inputs into frames, sends them
    /// out and keeps its own copy; a client adapts its input delay to the link now and then.
    /// </summary>
    public void Pump(double delta)
    {
        _session.Receive();
        _sinceLinkCheck += delta;
        if (_sinceLinkCheck >= 1.0)
        {
            _sinceLinkCheck = 0;
            CheckLink();
        }
        EmitFrames();
    }

    /// <summary>The match had to stop for a whole tick waiting for a frame: the client's commands may arrive too late.</summary>
    public void ReportStall() => _stallsSinceCheck++;

    /// <summary>
    /// After the match stepped: send our inputs, frames and checksums now rather than next frame. The host first
    /// turns its own fresh input into frames, which are often the last ones the others were waiting for.
    /// </summary>
    public void Flush()
    {
        EmitFrames();
        _session.Flush();
    }

    private void EmitFrames()
    {
        if (_host == null)
            return;
        foreach (var frame in _host.CollectReadyFrames())
        {
            _session.Broadcast(frame);
            _peer.ReceiveFrame(frame);
            _history.Add(frame);
        }
    }

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

    public bool HasFrame(int tick) => _peer.HasFrame(tick);

    public FrameMessage TakeFrame(int tick) => _peer.TakeFrame(tick);

    /// <summary>After simulating <paramref name="tick"/>: send our input for a later tick and, now and then, a checksum.</summary>
    public void AfterStep(int tick, Simulation sim, bool sendInput = true)
    {
        foreach (var input in _peer.TakeInputs(tick))
        {
            if (!sendInput)
            {
                // Replaying the past: those ticks are long decided, the host would ignore the input anyway.
            }
            else if (_host != null)
                _host.SubmitInput(_localFaction, input);
            else
                _session.Send(1, input);
        }

        if (tick % ChecksumInterval != 0)
            return;
        ulong checksum = sim.ComputeChecksum();
        if (LogChecksums)
            Godot.GD.Print($"[net] tick {tick} checksum {checksum:x16}");
        if (_checksums != null)
        {
            if (_checksums.RecordOwn(tick, checksum))
                ReportDesync(tick);
        }
        else
        {
            _session.Send(1, new ChecksumMessage(tick, checksum));
        }
    }

    private void OnMessage(int from, NetMessage message)
    {
        switch (message)
        {
            case InputMessage input when _host != null && _session.FactionOf(from) is { } faction:
                _host.SubmitInput(faction, input);
                break;
            case CaughtUpMessage caught when _host != null && _session.FactionOf(from) is { } back:
                _host.RestoreFaction(back, _host.NextFrameTick + ReturnDelay);
                if (LogChecksums)
                    Godot.GD.Print($"[net] faction {back} caught up at tick {caught.Tick}, returns at {_host.NextFrameTick + ReturnDelay}");
                break;
            case ChecksumMessage check when _checksums != null:
                if (_checksums.RecordPeer(check.Tick, check.Value))
                    ReportDesync(check.Tick);
                break;
            case FrameMessage frame when _host == null:
                _peer.ReceiveFrame(frame);
                break;
            case DesyncMessage desync when _host == null:
                MarkDesynced(desync.Tick);
                break;
        }
    }

    private void ReportDesync(int tick)
    {
        _session.Broadcast(new DesyncMessage(tick));
        MarkDesynced(tick);
    }

    private void MarkDesynced(int tick)
    {
        if (IsDesynced)
            return;
        IsDesynced = true;
        DesyncDetected?.Invoke(tick);
    }
}

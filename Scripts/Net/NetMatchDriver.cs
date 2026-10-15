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
public sealed partial class NetMatchDriver : IDisposable
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

    public NetMatchDriver(NetSession session, StartMessage start)
    {
        _session = session;
        _localFaction = start.YourFaction;
        if (session.IsHost)
        {
            var humans = start.Slots.Select((slot, faction) => (slot, faction))
                .Where(p => p.slot.Kind == NetSlotKind.Human).Select(p => p.faction).ToList();
            _checksums = new ChecksumTracker();
            if (session.Resumed is { } save)
            {
                // Brought back after a crash: replay the saved frames, then go on from the next tick; the others are
                // bots' until they rejoin.
                _host = new LockstepHost(humans, save.Frames.Count + 1);
                SilentReplayLimit = save.Frames.Count;
                foreach (int faction in humans.Where(f => f != start.YourFaction))
                    _host.DropFaction(faction);
                foreach (var frame in save.Frames)
                {
                    _history.Add(frame);
                    _peer.ReceiveFrame(frame);
                }
                _save = MatchSaveStore.Append();
            }
            else
            {
                _host = new LockstepHost(humans);
                _save = MatchSaveStore.Create(start, session.MatchPlayers);
            }
            session.PlayerLeftMatch += OnPlayerLeft;
            session.PlayerRejoining += SendMatchSoFar;
        }
        IsRejoin = start.Rejoin;
        session.MessageReceived += OnMessage;
        session.FlushEarlyMessages();
    }

    /// <summary>
    /// Leaving the match screen (to the menu, or quitting): the save keeps what was played so far, and the session
    /// (which outlives the match until the menu) stops calling back into this driver.
    /// </summary>
    public void Dispose()
    {
        CloseSave();
        _session.MessageReceived -= OnMessage;
        _session.PlayerLeftMatch -= OnPlayerLeft;
        _session.PlayerRejoining -= SendMatchSoFar;
    }

    private void OnPlayerLeft(int faction) => _host?.DropFaction(faction);

    public bool IsHost => _host != null;

    /// <summary>
    /// The last tick a replay may run without sending our input: a rejoining player's input is ignored until they
    /// take their faction back, but a host resuming its save plays its own faction from the first new tick on.
    /// </summary>
    public int SilentReplayLimit { get; } = int.MaxValue;

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
        if (_host == null)
            _session.Send(1, new CaughtUpMessage(tick)); // a host resuming its own save has no one to tell
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
        if (IsPausedByPlayer)
            _session.Send(peer, new PauseMessage(true, PausedBy));
    }

    /// <summary>Raised once when the machines' simulations diverged; the argument is the tick.</summary>
    public event Action<int>? DesyncDetected;

    public bool IsDesynced { get; private set; }

    /// <summary>Dev: print every checksum, to compare two instances' logs.</summary>
    public bool LogChecksums { get; set; }

    public void QueueLocal(Command command) => _peer.QueueLocal(command);

    /// <summary>
    /// Before the match steps: take in what has arrived; the host turns complete inputs into frames, sends them
    /// out and keeps its own copy; a client adapts its input delay to the link now and then.
    /// </summary>
    public void Pump(double delta)
    {
        _session.Receive();
        _sinceSave += delta;
        if (_sinceSave >= SaveIntervalSeconds)
        {
            _sinceSave = 0;
            SaveNow();
        }
        _sinceLinkCheck += delta;
        if (_sinceLinkCheck >= 1.0)
        {
            _sinceLinkCheck = 0;
            CheckLink();
        }
        EmitFrames();
    }

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
        if (_host == null || IsPausedByPlayer)
            return; // paused: no new ticks for anyone
        foreach (var frame in _host.CollectReadyFrames())
        {
            _session.Broadcast(frame);
            _peer.ReceiveFrame(frame);
            _history.Add(frame);
            _unsaved.Add(frame);
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
                continue; // replaying the past: those ticks are long decided, the host would ignore the input anyway
            if (_host != null)
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
            case PauseMessage pause when _host != null:
                SetPause(pause.Paused, _session.NameOfPeer(from));
                break;
            case PauseMessage pause:
                SetPause(pause.Paused, pause.By); // the host's word
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

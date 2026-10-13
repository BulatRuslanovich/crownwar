using System;
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
        }
        session.MessageReceived += OnMessage;
    }

    /// <summary>Raised once when the machines' simulations diverged; the argument is the tick.</summary>
    public event Action<int>? DesyncDetected;

    public bool IsDesynced { get; private set; }

    /// <summary>Dev: print every checksum, to compare two instances' logs.</summary>
    public bool LogChecksums { get; set; }

    public void QueueLocal(Command command) => _peer.QueueLocal(command);

    /// <summary>Host: turn complete inputs into frames, send them out and keep our own copy.</summary>
    public void Pump()
    {
        if (_host == null)
            return;
        foreach (var frame in _host.CollectReadyFrames())
        {
            _session.Broadcast(frame);
            _peer.ReceiveFrame(frame);
        }
    }

    public bool HasFrame(int tick) => _peer.HasFrame(tick);

    public FrameMessage TakeFrame(int tick) => _peer.TakeFrame(tick);

    /// <summary>After simulating <paramref name="tick"/>: send our input for a later tick and, now and then, a checksum.</summary>
    public void AfterStep(int tick, Simulation sim)
    {
        var input = _peer.TakeInput(tick);
        if (_host != null)
            _host.SubmitInput(_localFaction, input);
        else
            _session.Send(1, input);

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

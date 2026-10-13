using Lordsio.Sim.Core;

namespace Lordsio.Sim.Net;

/// <summary>
/// Host side of host-relayed lockstep. Every human faction sends one <see cref="InputMessage"/> per tick
/// (possibly empty) for tick T + <see cref="InputDelay"/>. Once the inputs of all humans still in the game
/// have arrived for a tick, the host emits a <see cref="FrameMessage"/> for it; every peer (the host included)
/// applies frames in order, so all simulations receive exactly the same commands on the same tick.
/// </summary>
public sealed class LockstepHost
{
    /// <summary>Ticks between issuing a command and it taking effect (150 ms at 20 ticks/s).</summary>
    public const int InputDelay = 3;

    /// <summary>Inputs this far ahead of the next frame are refused (a misbehaving or broken peer).</summary>
    private const int MaxTicksAhead = 200;

    private readonly SortedSet<int> _humans;
    private readonly Dictionary<int, Dictionary<int, IReadOnlyList<Command>>> _inputs = new();
    private readonly List<int> _pendingTakeovers = new();
    private int _nextFrameTick = 1;

    public LockstepHost(IEnumerable<int> humanFactions)
    {
        _humans = new SortedSet<int>(humanFactions);
    }

    /// <summary>Human factions the host is still waiting on.</summary>
    public IReadOnlyCollection<int> Humans => _humans;

    public int NextFrameTick => _nextFrameTick;

    /// <summary>
    /// Records a faction's commands for a tick. Each command is re-stamped with the sender's faction:
    /// a peer cannot order someone else's units, whatever it puts in the message.
    /// </summary>
    public void SubmitInput(int faction, InputMessage input)
    {
        if (!_humans.Contains(faction) || input.Tick < _nextFrameTick || input.Tick > _nextFrameTick + MaxTicksAhead)
            return;
        if (!_inputs.TryGetValue(input.Tick, out var byFaction))
            _inputs[input.Tick] = byFaction = new Dictionary<int, IReadOnlyList<Command>>();
        byFaction.TryAdd(faction, input.Commands.Select(c => c with { FactionId = faction }).ToList());
    }

    /// <summary>The faction's player left: stop waiting for it and hand it to a bot in the next frame.</summary>
    public void DropFaction(int faction)
    {
        if (_humans.Remove(faction))
            _pendingTakeovers.Add(faction);
    }

    /// <summary>Frames that are complete, in tick order. Ticks before the first possible input are always ready.</summary>
    public List<FrameMessage> CollectReadyFrames()
    {
        var frames = new List<FrameMessage>();
        while (true)
        {
            int tick = _nextFrameTick;
            _inputs.TryGetValue(tick, out var byFaction);
            bool ready = tick <= InputDelay || _humans.All(f => byFaction != null && byFaction.ContainsKey(f));
            if (!ready)
                break;

            // Fixed order: by faction, then in the order the player issued them.
            var commands = new List<Command>();
            if (byFaction != null)
            {
                foreach (int faction in byFaction.Keys.Order())
                    commands.AddRange(byFaction[faction]);
            }
            frames.Add(new FrameMessage(tick, commands, _pendingTakeovers.ToList()));
            _pendingTakeovers.Clear();
            _inputs.Remove(tick);
            _nextFrameTick++;
            if (frames.Count >= MaxTicksAhead)
                break;
        }
        return frames;
    }
}

/// <summary>
/// One player's side of lockstep: buffers frames from the host and batches local commands into the
/// input for the tick they will take effect on.
/// </summary>
public sealed class LockstepPeer
{
    private readonly Dictionary<int, FrameMessage> _frames = new();
    private readonly List<Command> _local = new();

    public void QueueLocal(Command command) => _local.Add(command);

    public void ReceiveFrame(FrameMessage frame) => _frames.TryAdd(frame.Tick, frame);

    public bool HasFrame(int tick) => _frames.ContainsKey(tick);

    public FrameMessage TakeFrame(int tick)
    {
        var frame = _frames[tick];
        _frames.Remove(tick);
        return frame;
    }

    /// <summary>
    /// Called once per simulated tick: all commands queued since the last call, addressed to the tick
    /// <see cref="LockstepHost.InputDelay"/> ahead.
    /// </summary>
    public InputMessage TakeInput(int currentTick)
    {
        var input = new InputMessage(currentTick + LockstepHost.InputDelay, _local.ToList());
        _local.Clear();
        return input;
    }
}

/// <summary>Host-side comparison of state checksums reported by peers.</summary>
public sealed class ChecksumTracker
{
    private readonly Dictionary<int, ulong> _reference = new();
    private readonly Dictionary<int, List<ulong>> _pending = new();

    /// <summary>Records the host's own checksum; returns true if an earlier peer report disagrees.</summary>
    public bool RecordOwn(int tick, ulong value)
    {
        _reference[tick] = value;
        bool mismatch = _pending.Remove(tick, out var reports) && reports.Any(r => r != value);
        Prune(tick);
        return mismatch;
    }

    /// <summary>Records a peer's checksum; returns true if it disagrees with the host's.</summary>
    public bool RecordPeer(int tick, ulong value)
    {
        if (_reference.TryGetValue(tick, out ulong own))
            return own != value;
        if (!_pending.TryGetValue(tick, out var list))
            _pending[tick] = list = new List<ulong>();
        list.Add(value);
        return false;
    }

    private void Prune(int tick)
    {
        // Peers lag the host by at most a few seconds; older reference values are not needed.
        foreach (int old in _reference.Keys.Where(t => t < tick - 2000).ToList())
            _reference.Remove(old);
    }
}

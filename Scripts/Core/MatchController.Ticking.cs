using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using Warcrown.Net;
using Warcrown.Sim.AI;
using Warcrown.Sim.Core;
using Warcrown.Sim.Net;
using Warcrown.UI;

namespace Warcrown.Core;

/// <summary>Advancing the simulation: fixed ticks in single player, lockstep frames (and the replay after a rejoin) in a network game.</summary>
public partial class MatchController
{
    /// <summary>Real time not yet turned into ticks.</summary>
    private double _accumulator;

    /// <summary>A networked match is stalled until another player's commands arrive.</summary>
    public bool WaitingForPlayers { get; private set; }

    /// <summary>
    /// Frames buffered this far ahead mean the match is being replayed (this player rejoined after a drop):
    /// ticks run as fast as the frame budget allows, with nothing drawn or heard on the way.
    /// </summary>
    private const int ReplayThreshold = 40;
    private const double ReplayBudgetMilliseconds = 25;

    /// <summary>Replaying the match after rejoining it.</summary>
    public bool CatchingUp { get; private set; }

    /// <summary>How far the replay has come, 0..1.</summary>
    public float CatchUpProgress => Net is { LatestFrameTick: > 0 } net ? Math.Min(1f, (float)Sim.Tick / net.LatestFrameTick) : 0f;

    private bool _replayedThisFrame;

    /// <summary>Factions a returning player takes back from their bot, by the tick it happens.</summary>
    private readonly Dictionary<int, int> _returnAt = new();

    /// <summary>
    /// Frames buffered further ahead than the input delay mean we fell behind the others: run ticks without
    /// waiting for real time. (Up to the input delay is normal: our own inputs are sent that far ahead.)
    /// </summary>
    private const int LockstepCatchUpThreshold = LockstepHost.InputDelay + 2;

    /// <summary>Upper bound on catch-up ticks per frame so a long hitch does not snowball.</summary>
    private const int MaxStepsPerFrame = 5;

    /// <summary>Smoothed wall-clock cost of one simulation tick plus bot decisions, for the debug overlay.</summary>
    public double StepMilliseconds { get; private set; }

    /// <summary>Fraction of the way from the previous tick to the next one, for smooth rendering.</summary>
    public float InterpolationAlpha => (float)(_accumulator / Simulation.TickDuration);

    private void ProcessLocal(double delta)
    {
        _accumulator += delta;
        int steps = 0;
        while (_accumulator >= Simulation.TickDuration && steps < MaxStepsPerFrame)
        {
            StepOnce();
            _accumulator -= Simulation.TickDuration;
            steps++;
        }
        if (_accumulator >= Simulation.TickDuration)
            _accumulator = 0; // drop the backlog after a hitch instead of fast-forwarding
    }

    /// <summary>Times the match had to stop and wait for another player's commands (shown on the F3 overlay).</summary>
    public int NetStalls { get; private set; }
    private bool _starving;

    private void ProcessNetworked(double delta)
    {
        var net = Net!;
        net.Pump(delta);
        StepNetworked(net, delta);
        net.Flush();
    }

    /// <summary>
    /// Lockstep: a tick runs only once its frame (everyone's commands) has arrived. When frames pile up
    /// (we fell behind) we catch up a few ticks per frame; when none is there we wait.
    /// </summary>
    private void StepNetworked(NetMatchDriver net, double delta)
    {
        _replayedThisFrame = false;
        if (net.IsPausedByPlayer && !CatchingUp)
        {
            // Everyone stands still; do not bank the time, nor take the silence for a slow link.
            _accumulator = 0;
            WaitingForPlayers = false;
            _starving = false;
            return;
        }
        if (Sim.IsOver || net.IsDesynced)
            return;

        if (net.LatestFrameTick - Sim.Tick > ReplayThreshold && Sim.Tick < net.SilentReplayLimit)
        {
            CatchingUp = true;
            _replayedThisFrame = true;
            WaitingForPlayers = false;
            _accumulator = 0;
            long start = Stopwatch.GetTimestamp();
            // Only the decided past is replayed silently: a host resuming its save must send input for its first new
            // ticks, or it would wait for itself forever.
            while (net.HasFrame(Sim.Tick + 1) && Sim.Tick < net.SilentReplayLimit
                   && Stopwatch.GetElapsedTime(start).TotalMilliseconds < ReplayBudgetMilliseconds)
                ApplyFrame(net, net.TakeFrame(Sim.Tick + 1), replaying: true);
            return;
        }
        CatchingUp = false;
        // A returning player who has caught up asks for their faction back (once).
        if (net.IsRejoin && Sim.Tick > 0 && net.LatestFrameTick - Sim.Tick <= 2)
            net.ReportCaughtUp(Sim.Tick);

        _accumulator += delta;
        int steps = 0;
        while (steps < MaxStepsPerFrame && net.HasFrame(Sim.Tick + 1)
               && (_accumulator >= Simulation.TickDuration || net.HasFrame(Sim.Tick + 1 + LockstepCatchUpThreshold)))
        {
            ApplyFrame(net, net.TakeFrame(Sim.Tick + 1), replaying: false);
            _accumulator = Math.Max(0, _accumulator - Simulation.TickDuration);
            steps++;
        }
        // A stall: a whole tick overdue and still no frame (a few ms late is only clocks drifting apart).
        bool starving = !net.HasFrame(Sim.Tick + 1) && _accumulator >= Simulation.TickDuration * 2;
        if (starving && !_starving)
        {
            NetStalls++;
            net.ReportStall();
        }
        _starving = starving;
        // While waiting for others, do not bank time to burst through later.
        _accumulator = Math.Min(_accumulator, Simulation.TickDuration * 4);
        WaitingForPlayers = !net.HasFrame(Sim.Tick + 1) && _accumulator >= Simulation.TickDuration * 4;
    }

    /// <summary>
    /// What every machine does with a frame: returning players take their faction back from the bot, the
    /// commands go in, dropped players are handed to bots, then one tick runs.
    /// </summary>
    private void ApplyFrame(NetMatchDriver net, FrameMessage frame, bool replaying)
    {
        foreach (var ret in frame.Returns)
            _returnAt[ret.Faction] = ret.FromTick;
        foreach (var (faction, _) in _returnAt.Where(r => r.Value == frame.Tick).ToList())
        {
            // Only the stand-in that plays the faction on every machine; a bot that sends its commands through
            // the network (dev autoplay) is a player, not part of the simulation.
            _bots.RemoveAll(b => b.FactionId == faction && b.CommandSink == null);
            _returnAt.Remove(faction);
            if (net.LogChecksums)
                GD.Print($"[net] faction {faction} back under player control at tick {frame.Tick}");
            if (!replaying)
                _hud.ShowNotice(faction == LocalFactionId
                    ? Tr("NET_YOU_RETURNED")
                    : string.Format(Tr("NET_PLAYER_RETURNED"), Names.Faction(faction)));
        }
        foreach (var command in frame.Commands)
            Sim.Issue(command);
        foreach (int faction in frame.Takeovers)
        {
            if (_bots.Any(b => b.FactionId == faction && b.CommandSink == null))
                continue;
            AddBot(faction, BotDifficulty.Normal);
            if (!replaying)
                _hud.ShowNotice(string.Format(Tr("NET_PLAYER_LEFT"), Names.Faction(faction)));
        }
        StepOnce();
        net.AfterStep(Sim.Tick, Sim, sendInput: !replaying);
    }

    private void StepOnce()
    {
        long start = Stopwatch.GetTimestamp();
        Sim.Step();
        // Bots decide right after a tick, like a player reacting to what they see.
        foreach (var bot in _bots)
            bot.Update();
        double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        StepMilliseconds = StepMilliseconds * 0.9 + ms * 0.1;
    }
}

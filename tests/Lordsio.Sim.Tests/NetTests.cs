using System.Numerics;
using Lordsio.Sim.AI;
using Lordsio.Sim.Core;
using Lordsio.Sim.Net;
using Lordsio.Sim.World;

namespace Lordsio.Sim.Tests;

public class NetCodecTests
{
    private static IEnumerable<NetMessage> Samples()
    {
        var slots = new[] { new NetSlot(NetSlotKind.Human, "Аня", 0, 0, 1), new NetSlot(NetSlotKind.Bot, "", 2, 1, 0), new NetSlot(NetSlotKind.Empty, "", 0, 0, 0) };
        var commands = new Command[]
        {
            new MoveCommand(0, new[] { 3, 4, 5 }, new Vector2(10.25f, 3.5f), MoveMode.AttackMove),
            new AttackCommand(1, new[] { 7 }, 99),
            new RecruitCommand(2, 12, "heavy"),
            new BuildCommand(3, "tower", new Cell(40, 21)),
            new ProposePeaceCommand(0, 2),
            new AnswerPeaceCommand(2, 0, Accept: true),
            new AnswerPeaceCommand(1, 0, Accept: false),
            new DeclareWarCommand(3, 1),
        };
        yield return new HelloMessage("1.0+abc", "Игрок");
        yield return new HelloMessage("1.0+abc", "Игрок", "a1b2c3");
        yield return new RejectMessage("NET_REJECT_VERSION");
        yield return new LobbyMessage(slots);
        yield return new SetTeamMessage(2);
        yield return new StartMessage(12345, slots, 1);
        yield return new StartMessage(12345, slots, 1, "a1b2c3", Rejoin: true);
        yield return new LobbyMessage(slots, MatchMode.Defense);
        yield return new StartMessage(12345, slots, 0, "d4e5f6", Mode: MatchMode.Defense);
        yield return new InputMessage(77, commands);
        yield return new FrameMessage(78, commands, new[] { 2 }, Array.Empty<FactionReturn>());
        yield return new FrameMessage(79, Array.Empty<Command>(), Array.Empty<int>(), new[] { new FactionReturn(1, 120) });
        yield return new CaughtUpMessage(500);
        yield return new ChecksumMessage(80, 0xDEADBEEFCAFEUL);
        yield return new DesyncMessage(81);
        yield return new PauseMessage(true, "Аня");
        yield return new PauseMessage(false);
    }

    [TestCaseSource(nameof(Samples))]
    public void EveryMessageSurvivesARoundTrip(NetMessage message)
    {
        var bytes = NetCodec.Encode(message);
        var decoded = NetCodec.Decode(bytes);

        Assert.That(decoded.GetType(), Is.EqualTo(message.GetType()));
        Assert.That(NetCodec.Encode(decoded), Is.EqualTo(bytes), "re-encoding must give the same bytes");
    }

    [Test]
    public void TheModeTravelsWithTheStartAndOlderMessagesMeanVersus()
    {
        var slots = new[] { new NetSlot(NetSlotKind.Human, "Аня", 0, 0, 1) };
        var defense = NetCodec.Encode(new StartMessage(9, slots, 0, "tok", Mode: MatchMode.Defense));
        var versus = NetCodec.Encode(new StartMessage(9, slots, 0, "tok"));

        Assert.That(((StartMessage)NetCodec.Decode(defense)).Mode, Is.EqualTo(MatchMode.Defense));
        // A start saved before the modes existed has no mode byte at the end.
        Assert.That(((StartMessage)NetCodec.Decode(versus[..^1])).Mode, Is.EqualTo(MatchMode.Versus));
        Assert.That(((LobbyMessage)NetCodec.Decode(NetCodec.Encode(new LobbyMessage(slots, MatchMode.Defense)))).Mode, Is.EqualTo(MatchMode.Defense));
        defense[^1] = 77;
        Assert.Throws<FormatException>(() => NetCodec.Decode(defense), "unknown mode");
    }

    [Test]
    public void MalformedDataIsRejected()
    {
        var good = NetCodec.Encode(new InputMessage(5, new Command[] { new RecruitCommand(0, 1, "infantry") }));

        Assert.Throws<FormatException>(() => NetCodec.Decode(Array.Empty<byte>()));
        Assert.Throws<FormatException>(() => NetCodec.Decode(new byte[] { 200, 1, 2 }), "unknown tag");
        Assert.Throws<FormatException>(() => NetCodec.Decode(good[..^2]), "truncated");
        Assert.Throws<FormatException>(() => NetCodec.Decode(good.Concat(new byte[] { 0 }).ToArray()), "trailing bytes");
        // Input with a list length far beyond the limit.
        Assert.Throws<FormatException>(() => NetCodec.Decode(new byte[] { 6, 0, 0, 0, 0, 0xFF, 0xFF }));
        var nan = NetCodec.Encode(new InputMessage(1, new Command[] { new MoveCommand(0, new[] { 1 }, new Vector2(float.NaN, 1f)) }));
        Assert.Throws<FormatException>(() => NetCodec.Decode(nan), "non-finite position");
    }
}

public class LockstepHostTests
{
    [Test]
    public void FirstTicksNeedNoInput()
    {
        var host = new LockstepHost(new[] { 0, 1 });

        var frames = host.CollectReadyFrames();

        Assert.That(frames.Select(f => f.Tick), Is.EqualTo(Enumerable.Range(1, LockstepHost.InputDelay)));
        Assert.That(frames.All(f => f.Commands.Count == 0), Is.True);
    }

    [Test]
    public void WaitsForEveryHumanAndOrdersByFaction()
    {
        var host = new LockstepHost(new[] { 0, 1 });
        host.CollectReadyFrames();
        int tick = LockstepHost.InputDelay + 1;

        host.SubmitInput(1, new InputMessage(tick, new Command[] { new RecruitCommand(1, 5, "archer") }));
        Assert.That(host.CollectReadyFrames(), Is.Empty, "faction 0 has not sent its input yet");

        host.SubmitInput(0, new InputMessage(tick, new Command[] { new RecruitCommand(0, 4, "infantry") }));
        var frame = host.CollectReadyFrames().Single();
        Assert.That(frame.Tick, Is.EqualTo(tick));
        Assert.That(frame.Commands.Select(c => c.FactionId), Is.EqualTo(new[] { 0, 1 }));
    }

    [Test]
    public void SenderFactionOverridesWhatTheMessageClaims()
    {
        var host = new LockstepHost(new[] { 0, 1 });
        host.CollectReadyFrames();
        int tick = LockstepHost.InputDelay + 1;

        // Faction 1 tries to move faction 0's units by forging the faction id.
        host.SubmitInput(1, new InputMessage(tick, new Command[] { new MoveCommand(0, new[] { 1, 2 }, Vector2.One) }));
        host.SubmitInput(0, new InputMessage(tick, Array.Empty<Command>()));

        Assert.That(host.CollectReadyFrames().Single().Commands.Single().FactionId, Is.EqualTo(1));
    }

    [Test]
    public void DroppedPlayerIsHandedToABotAndNoLongerAwaited()
    {
        var host = new LockstepHost(new[] { 0, 1 });
        host.CollectReadyFrames();
        int tick = LockstepHost.InputDelay + 1;

        host.SubmitInput(0, new InputMessage(tick, Array.Empty<Command>()));
        host.DropFaction(1);
        var frame = host.CollectReadyFrames().Single();

        Assert.That(frame.Takeovers, Is.EqualTo(new[] { 1 }));
        Assert.That(host.Humans, Is.EqualTo(new[] { 0 }));
        host.SubmitInput(0, new InputMessage(tick + 1, Array.Empty<Command>()));
        Assert.That(host.CollectReadyFrames().Single().Takeovers, Is.Empty, "a takeover is announced once");
    }

    [Test]
    public void ReturningPlayerTakesTheFactionBackAtTheAnnouncedTick()
    {
        var host = new LockstepHost(new[] { 0, 1 });
        host.CollectReadyFrames();
        int tick = LockstepHost.InputDelay + 1;
        host.DropFaction(1);
        host.SubmitInput(0, new InputMessage(tick, Array.Empty<Command>()));
        host.CollectReadyFrames();

        host.RestoreFaction(1, fromTick: tick + 3);
        host.SubmitInput(1, new InputMessage(tick + 1, new Command[] { new RecruitCommand(1, 1, "infantry") })); // too early: ignored
        host.SubmitInput(0, new InputMessage(tick + 1, Array.Empty<Command>()));
        host.SubmitInput(0, new InputMessage(tick + 2, Array.Empty<Command>()));
        var frames = host.CollectReadyFrames();

        Assert.That(frames.Select(f => f.Tick), Is.EqualTo(new[] { tick + 1, tick + 2 }), "the bot's ticks do not wait for the player");
        Assert.That(frames[0].Returns, Is.EqualTo(new[] { new FactionReturn(1, tick + 3) }), "announced in the next frame");
        Assert.That(frames[0].Commands, Is.Empty);

        host.SubmitInput(0, new InputMessage(tick + 3, Array.Empty<Command>()));
        Assert.That(host.CollectReadyFrames(), Is.Empty, "from the return tick on, the player is awaited again");
        host.SubmitInput(1, new InputMessage(tick + 3, new Command[] { new RecruitCommand(1, 1, "infantry") }));
        Assert.That(host.CollectReadyFrames().Single().Commands.Single().FactionId, Is.EqualTo(1));
    }

    [Test]
    public void LateAndDuplicateInputsAreIgnored()
    {
        var host = new LockstepHost(new[] { 0 });
        host.CollectReadyFrames();
        int tick = LockstepHost.InputDelay + 1;

        host.SubmitInput(0, new InputMessage(1, new Command[] { new RecruitCommand(0, 1, "infantry") }));
        host.SubmitInput(0, new InputMessage(tick, Array.Empty<Command>()));
        host.SubmitInput(0, new InputMessage(tick, new Command[] { new RecruitCommand(0, 1, "infantry") }));

        Assert.That(host.CollectReadyFrames().Single().Commands, Is.Empty);
    }
}

/// <summary>Two players in one process, connected through simulated LAN links (0–50 ms each way).</summary>
public class LockstepEndToEndTests
{
    private sealed class Player
    {
        public required int Faction;
        public required Simulation Sim;
        public required List<BotBrain> Bots;
        public readonly LockstepPeer Peer = new();
        public readonly Dictionary<int, ulong> Checksums = new();
        public bool Connected = true;
    }

    private static Player CreatePlayer(int faction)
    {
        var map = TestSim.DefaultMap();
        var rules = TestSim.RealRules();
        var sim = new Simulation(map, rules, new MatchSetup(4, Seed: 21, Teams: new[] { 0, 1, 0, 1 }));
        // Factions 2 and 3 are bots on every machine.
        var bots = new List<BotBrain> { new(sim, 2, BotDifficulty.Normal, 23), new(sim, 3, BotDifficulty.Normal, 24) };
        return new Player { Faction = faction, Sim = sim, Bots = bots };
    }

    /// <summary>What every machine does with a frame: commands, bot takeovers and returns, then one tick.</summary>
    private static void Apply(Player p, FrameMessage frame, Dictionary<int, int> returnAt)
    {
        foreach (var ret in frame.Returns)
            returnAt[ret.Faction] = ret.FromTick;
        foreach (var (faction, from) in returnAt.Where(r => r.Value == frame.Tick).ToList())
        {
            p.Bots.RemoveAll(b => b.FactionId == faction);
            returnAt.Remove(faction);
        }
        foreach (var command in frame.Commands)
            p.Sim.Issue(command);
        foreach (int faction in frame.Takeovers)
            p.Bots.Add(new BotBrain(p.Sim, faction, BotDifficulty.Normal, 100 + faction));
        p.Sim.Step();
        foreach (var bot in p.Bots)
            bot.Update();
        p.Checksums[p.Sim.Tick] = p.Sim.ComputeChecksum();
    }

    [Test]
    public void MatchSaveRoundTripsAndSurvivesACutShortTail()
    {
        var start = new StartMessage(77, new[] { new NetSlot(NetSlotKind.Human, "Аня", 0, 0, 1), new NetSlot(NetSlotKind.Human, "Борис", 0, 1, 2) }, 0, "tok0");
        var players = new[] { new SavedPlayer(0, "tok0", "Аня"), new SavedPlayer(1, "tok1", "Борис") };
        var frames = Enumerable.Range(1, 5).Select(t => new FrameMessage(t, new Command[] { new RecruitCommand(0, 3, "infantry") }, Array.Empty<int>(), Array.Empty<FactionReturn>())).ToList();

        var stream = new MemoryStream();
        MatchSave.WriteHeader(stream, "1.0+x", start, players);
        MatchSave.AppendFrames(stream, frames.Take(3));
        MatchSave.AppendFrames(stream, frames.Skip(3));
        stream.Position = 0;
        var save = MatchSave.Read(stream)!;

        Assert.That(save.Version, Is.EqualTo("1.0+x"));
        Assert.That(save.Start.Seed, Is.EqualTo(77));
        Assert.That(save.Players, Is.EqualTo(players));
        Assert.That(save.Frames.Select(f => f.Tick), Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));

        // A crash in the middle of writing the last frame: the match resumes after the last whole one.
        var cut = new MemoryStream(stream.ToArray()[..^3]);
        Assert.That(MatchSave.Read(cut)!.Frames, Has.Count.EqualTo(4));
        Assert.That(MatchSave.Read(new MemoryStream(new byte[] { 1, 2, 3 })), Is.Null, "not a save");
    }

    [Test]
    public void HostResumedFromASaveReplaysTheMatchExactlyAndPlaysOn()
    {
        var rng = new Random(4);
        var host = new LockstepHost(new[] { 0, 1 });
        var history = new List<FrameMessage>();
        var a = CreatePlayer(0);
        var b = CreatePlayer(1);
        var returns = new Dictionary<int, int>();

        void Play(Player p)
        {
            if (p.Sim.Tick % 60 == 10)
            {
                var army = p.Sim.Units.Where(u => u.FactionId == p.Faction).Select(u => u.Id).ToList();
                p.Peer.QueueLocal(new MoveCommand(p.Faction, army, new Vector2(30 + rng.Next(30), 20 + rng.Next(20)), MoveMode.AttackMove));
            }
        }

        // Thirty seconds of a two-player match, every frame saved.
        while (a.Sim.Tick < 30 * Simulation.TicksPerSecond)
        {
            foreach (var frame in host.CollectReadyFrames())
            {
                history.Add(frame);
                a.Peer.ReceiveFrame(frame);
                b.Peer.ReceiveFrame(frame);
            }
            foreach (var p in new[] { a, b })
            {
                Play(p);
                if (p.Peer.HasFrame(p.Sim.Tick + 1))
                {
                    Apply(p, p.Peer.TakeFrame(p.Sim.Tick + 1), returns);
                    foreach (var input in p.Peer.TakeInputs(p.Sim.Tick))
                        host.SubmitInput(p.Faction, input);
                }
            }
        }
        int savedTicks = history.Count;
        int crashTick = a.Sim.Tick;

        // The host's game crashes. Restarted, it replays the save and takes up from the frame after the last.
        var resumed = CreatePlayer(0);
        var resumedReturns = new Dictionary<int, int>();
        foreach (var frame in history)
        {
            Apply(resumed, frame, resumedReturns);
            resumed.Peer.TakeInputs(resumed.Sim.Tick); // the past is decided: nothing to send
        }
        Assert.That(resumed.Checksums[crashTick], Is.EqualTo(a.Checksums[crashTick]), "the replay reaches the same state");

        var newHost = new LockstepHost(new[] { 0, 1 }, savedTicks + 1);
        newHost.DropFaction(1); // the other player has not come back yet: a bot plays them
        for (int step = 0; step < 20 * Simulation.TicksPerSecond; step++)
        {
            foreach (var frame in newHost.CollectReadyFrames())
                resumed.Peer.ReceiveFrame(frame);
            Play(resumed);
            if (!resumed.Peer.HasFrame(resumed.Sim.Tick + 1))
                continue;
            Apply(resumed, resumed.Peer.TakeFrame(resumed.Sim.Tick + 1), resumedReturns);
            foreach (var input in resumed.Peer.TakeInputs(resumed.Sim.Tick))
                newHost.SubmitInput(0, input);
        }

        Assert.That(resumed.Sim.Tick, Is.GreaterThan(crashTick + 19 * Simulation.TicksPerSecond), "the resumed match does not stall");
        Assert.That(resumed.Bots.Any(bot => bot.FactionId == 1), Is.True, "the absent player's faction is a bot's until they return");
    }

    [Test]
    public void DroppedPlayerRejoinsByReplayingTheMatchAndTakesBackControl()
    {
        var rng = new Random(9);
        var host = new LockstepHost(new[] { 0, 1 });
        var history = new List<FrameMessage>();
        var a = CreatePlayer(0);
        var b = CreatePlayer(1);
        var returnsA = new Dictionary<int, int>();
        var returnsB = new Dictionary<int, int>();
        bool caughtUpReported = false;
        int returnTick = 0;

        for (int now = 0; now < 120 * Simulation.TicksPerSecond; now++)
        {
            foreach (var frame in host.CollectReadyFrames())
            {
                history.Add(frame);
                a.Peer.ReceiveFrame(frame);
                if (b.Connected)
                    b.Peer.ReceiveFrame(frame);
                if (frame.Returns.Count > 0)
                    returnTick = frame.Returns[0].FromTick;
            }

            if (now == 30 * Simulation.TicksPerSecond)
            {
                b.Connected = false; // crash
                host.DropFaction(1);
            }
            if (now == 60 * Simulation.TicksPerSecond)
            {
                // The game is started again and rejoins: a fresh simulation that gets the whole history.
                b = CreatePlayer(1);
                returnsB.Clear();
                foreach (var frame in history)
                    b.Peer.ReceiveFrame(frame);
            }

            foreach (var p in new[] { a, b }.Where(p => p.Connected))
            {
                if (now % 60 == 10 && p.Sim.Tick > 0)
                {
                    var army = p.Sim.Units.Where(u => u.FactionId == p.Faction).Select(u => u.Id).ToList();
                    p.Peer.QueueLocal(new MoveCommand(p.Faction, army, new Vector2(30 + rng.Next(30), 20 + rng.Next(20)), MoveMode.AttackMove));
                }
                // A catching-up player runs as many ticks as there are frames; the others one or two.
                int budget = p == b && p.Peer.LatestFrameTick - p.Sim.Tick > 40 ? int.MaxValue : 2;
                for (int step = 0; step < budget && p.Peer.HasFrame(p.Sim.Tick + 1); step++)
                {
                    Apply(p, p.Peer.TakeFrame(p.Sim.Tick + 1), p == a ? returnsA : returnsB);
                    foreach (var input in p.Peer.TakeInputs(p.Sim.Tick))
                        host.SubmitInput(p.Faction, input);
                }
            }

            if (now > 60 * Simulation.TicksPerSecond && !caughtUpReported && b.Peer.LatestFrameTick - b.Sim.Tick <= 2)
            {
                caughtUpReported = true;
                host.RestoreFaction(1, host.NextFrameTick + 20);
            }
        }

        Assert.That(caughtUpReported, Is.True, "the rejoining player caught up");
        Assert.That(returnTick, Is.GreaterThan(60 * Simulation.TicksPerSecond));
        Assert.That(a.Sim.Tick, Is.GreaterThan(115 * Simulation.TicksPerSecond), "nobody stalled");
        var shared = a.Checksums.Keys.Intersect(b.Checksums.Keys).ToList();
        Assert.That(shared.Count, Is.GreaterThan(100 * Simulation.TicksPerSecond), "the replay covers the match from its start");
        foreach (int tick in shared)
            Assert.That(b.Checksums[tick], Is.EqualTo(a.Checksums[tick]), $"desync at tick {tick}");
        Assert.That(a.Bots.Any(bot => bot.FactionId == 1), Is.False, "after the return the faction is the player's again");
        Assert.That(b.Bots.Any(bot => bot.FactionId == 1), Is.False);
    }

    [Test]
    public void ChangingTheInputDelayKeepsExactlyOneInputPerTick()
    {
        var peer = new LockstepPeer();
        var sent = new List<InputMessage>();
        int tick = 0;
        void Run(int ticks)
        {
            for (int i = 0; i < ticks; i++)
                sent.AddRange(peer.TakeInputs(++tick));
        }

        Run(5);
        peer.InputDelay = 7; // worse link: jump ahead, filling the gap with empty inputs
        Run(5);
        peer.QueueLocal(new StopProbe(0));
        peer.InputDelay = 3; // better link: hold back until the ticks catch up
        Run(1);
        Assert.That(sent.Last().Commands, Is.Empty, "the command waits for the next input it can go into");
        Run(10);

        var ticks = sent.Select(i => i.Tick).ToList();
        Assert.That(ticks, Is.EqualTo(Enumerable.Range(LockstepHost.InputDelay + 1, ticks.Count)), "every tick once, in order");
        Assert.That(sent.Count(i => i.Commands.Count > 0), Is.EqualTo(1), "the command is sent exactly once");
        Assert.That(ticks[^1], Is.EqualTo(tick + 3), "back to the short delay");

        peer.InputDelay = 99;
        Assert.That(peer.InputDelay, Is.EqualTo(LockstepPeer.MaxInputDelay));
        peer.InputDelay = 0;
        Assert.That(peer.InputDelay, Is.EqualTo(LockstepHost.InputDelay));
    }

    private sealed record StopProbe(int FactionId) : Command(FactionId);

    [Test]
    public void PeersStayInSyncThroughLagCommandsAndADisconnect()
    {
        var rng = new Random(5);
        var host = new LockstepHost(new[] { 0, 1 });
        var players = new[] { CreatePlayer(0), CreatePlayer(1) };
        var toHost = new List<(int DeliverAt, int Faction, InputMessage Input)>();
        var toPeers = new List<(int DeliverAt, Player Player, FrameMessage Frame)>();

        for (int now = 0; now < 90 * Simulation.TicksPerSecond; now++)
        {
            foreach (var m in toHost.Where(m => m.DeliverAt <= now).ToList())
            {
                host.SubmitInput(m.Faction, m.Input);
                toHost.Remove(m);
            }
            foreach (var m in toPeers.Where(m => m.DeliverAt <= now).ToList())
            {
                m.Player.Peer.ReceiveFrame(m.Frame);
                toPeers.Remove(m);
            }
            foreach (var frame in host.CollectReadyFrames())
                foreach (var p in players.Where(p => p.Connected))
                    toPeers.Add((now + rng.Next(0, 2), p, frame));

            // Player 1 drops out after 40 seconds; the host replaces it with a bot.
            if (now == 40 * Simulation.TicksPerSecond)
            {
                players[1].Connected = false;
                host.DropFaction(1);
            }

            // Player 1's link gets better and worse: its input delay changes every few seconds.
            if (now % 100 == 50)
                players[1].Peer.InputDelay = LockstepHost.InputDelay + rng.Next(0, 6);

            foreach (var p in players.Where(p => p.Connected))
            {
                // Each player orders its army around every few seconds.
                if (now % 60 == 10)
                {
                    var army = p.Sim.Units.Where(u => u.FactionId == p.Faction).Select(u => u.Id).ToList();
                    p.Peer.QueueLocal(new MoveCommand(p.Faction, army, new Vector2(30 + rng.Next(30), 20 + rng.Next(20)), MoveMode.AttackMove));
                }

                for (int step = 0; step < 3 && p.Peer.HasFrame(p.Sim.Tick + 1); step++)
                {
                    var frame = p.Peer.TakeFrame(p.Sim.Tick + 1);
                    foreach (var command in frame.Commands)
                        p.Sim.Issue(command);
                    foreach (int faction in frame.Takeovers)
                        p.Bots.Add(new BotBrain(p.Sim, faction, BotDifficulty.Normal, 100 + faction));
                    p.Sim.Step();
                    foreach (var bot in p.Bots)
                        bot.Update();
                    p.Checksums[p.Sim.Tick] = p.Sim.ComputeChecksum();
                    foreach (var input in p.Peer.TakeInputs(p.Sim.Tick))
                        toHost.Add((now + rng.Next(0, 2), p.Faction, input));
                }
            }
        }

        var a = players[0];
        var b = players[1];
        Assert.That(a.Sim.Tick, Is.GreaterThan(80 * Simulation.TicksPerSecond), "the remaining player must not stall after the disconnect");
        var shared = a.Checksums.Keys.Intersect(b.Checksums.Keys).ToList();
        Assert.That(shared.Count, Is.GreaterThan(30 * Simulation.TicksPerSecond));
        foreach (int tick in shared)
            Assert.That(b.Checksums[tick], Is.EqualTo(a.Checksums[tick]), $"desync at tick {tick}");
        Assert.That(a.Bots.Any(bot => bot.FactionId == 1), Is.True, "the dropped faction is played by a bot");
    }
}

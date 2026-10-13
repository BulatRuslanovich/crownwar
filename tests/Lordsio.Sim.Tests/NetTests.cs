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
        };
        yield return new HelloMessage("1.0+abc", "Игрок");
        yield return new RejectMessage("NET_REJECT_VERSION");
        yield return new LobbyMessage(slots);
        yield return new SetTeamMessage(2);
        yield return new StartMessage(12345, slots, 1);
        yield return new InputMessage(77, commands);
        yield return new FrameMessage(78, commands, new[] { 2 });
        yield return new ChecksumMessage(80, 0xDEADBEEFCAFEUL);
        yield return new DesyncMessage(81);
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
        string root = TestMaps.ProjectRoot();
        var map = MapLoader.Parse(File.ReadAllText(Path.Combine(root, "Maps", "default.txt")));
        var rules = GameRules.Parse(File.ReadAllText(Path.Combine(root, "Data", "rules.json")));
        var sim = new Simulation(map, rules, new MatchSetup(4, Seed: 21, Teams: new[] { 0, 1, 0, 1 }));
        // Factions 2 and 3 are bots on every machine.
        var bots = new List<BotBrain> { new(sim, 2, BotDifficulty.Normal, 23), new(sim, 3, BotDifficulty.Normal, 24) };
        return new Player { Faction = faction, Sim = sim, Bots = bots };
    }

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
                    toHost.Add((now + rng.Next(0, 2), p.Faction, p.Peer.TakeInput(p.Sim.Tick)));
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

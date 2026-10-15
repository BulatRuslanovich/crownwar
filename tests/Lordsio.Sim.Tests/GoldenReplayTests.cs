using Lordsio.Sim.AI;
using Lordsio.Sim.Core;
using Lordsio.Sim.World;

namespace Lordsio.Sim.Tests;

/// <summary>
/// Pins the exact course of one bot match on a generated map. Refactors and optimisations of the simulation must
/// keep every fingerprint identical; a deliberate rule or behaviour change updates <see cref="Expected"/> on purpose.
/// The fingerprint covers more than <see cref="Simulation.ComputeChecksum"/> (cooldowns, path waypoints, guard posts,
/// bot modes, every event), so that a drift is caught where it starts rather than through its effects later.
/// </summary>
public class GoldenReplayTests
{
    private const int MapSeed = 20261015;
    private const int Interval = 1000;
    private const int Ticks = 12_000;

    /// <summary>Fingerprints at ticks 1000, 2000, ... and one more where the match ends (or at 10 minutes).</summary>
    private static readonly ulong[] Expected =
    {
        0xDFCF0C7F10D75778UL,
        0x6BCBA2DCA057BB45UL,
        0x5F9B572E01376FF7UL,
        0x9FD75A7799A7A66DUL,
        0x6BDAB31474A57660UL,
        0x8C71A046B1BDB9CEUL,
        0x530D95BD9926889AUL,
        0x45A075E90460A6F4UL,
        0x85EDC4ADEFB8E5F5UL,
    };

    [Test]
    [SetCulture("en-US")] // events are fingerprinted as text: keep the decimal point the same on every machine
    public void BotMatchOnAGeneratedMapReplaysExactly()
    {
        var sim = new Simulation(MapGenerator.Generate(MapSeed), TestSim.RealRules(), new MatchSetup(4, MapSeed));
        var difficulties = new[] { BotDifficulty.Hard, BotDifficulty.Normal, BotDifficulty.Normal, BotDifficulty.Easy };
        var bots = Enumerable.Range(0, 4).Select(i => new BotBrain(sim, i, difficulties[i], MapSeed + i)).ToList();

        var fingerprint = new StateFingerprint();
        var actual = new List<ulong>();
        while (sim.Tick < Ticks && !sim.IsOver)
        {
            TestSim.StepWithBots(sim, bots);
            foreach (var e in sim.TakeEvents())
                fingerprint.Add(e.ToString());
            if (sim.Tick % Interval == 0)
            {
                fingerprint.AddState(sim, bots);
                actual.Add(fingerprint.Value);
            }
        }
        // How it ended counts too: the tick of the last blow and who won.
        fingerprint.Add(sim.Tick);
        fingerprint.Add(sim.WinningTeam);
        fingerprint.AddState(sim, bots);
        actual.Add(fingerprint.Value);

        TestContext.Out.WriteLine(string.Join(",\n", actual.Select(v => $"        0x{v:X16}UL")));
        Assert.That(actual, Is.EqualTo(Expected), "the simulation no longer replays this match exactly");
    }

    /// <summary>FNV-1a over everything a player could observe, plus the hidden timers that drive it.</summary>
    private sealed class StateFingerprint
    {
        private const ulong Prime = 1099511628211UL;
        public ulong Value { get; private set; } = 14695981039346656037UL;

        public void Add(int data)
        {
            for (int i = 0; i < 4; i++)
            {
                Value ^= (byte)(data >> (8 * i));
                Value *= Prime;
            }
        }

        public void Add(float data) => Add(BitConverter.SingleToInt32Bits(data));

        public void Add(System.Numerics.Vector2 v)
        {
            Add(v.X);
            Add(v.Y);
        }

        public void Add(string text)
        {
            foreach (char c in text)
                Add(c);
        }

        public void AddState(Simulation sim, IReadOnlyList<BotBrain> bots)
        {
            ulong checksum = sim.ComputeChecksum();
            Add((int)checksum);
            Add((int)(checksum >> 32));
            foreach (var u in sim.Units)
            {
                Add(u.Id);
                Add(u.Position);
                Add(u.Health);
                Add((int)u.Order);
                Add(u.AttackCooldownTicks);
                Add(u.GuardPoint);
                Add(u.Destination ?? new(-1f, -1f));
                Add(u.PathIndex);
                foreach (var p in u.Path)
                    Add(p);
            }
            foreach (var b in sim.Buildings)
            {
                Add(b.Id);
                Add(b.Health);
                Add(b.Invested);
                Add(b.Target?.Id ?? 0);
                foreach (var item in b.Queue)
                {
                    Add(item.Type.Id);
                    Add(item.TicksLeft);
                }
            }
            foreach (var f in sim.Factions)
            {
                Add(f.Treasury.Gold);
                Add(f.IncomePerSecond);
                Add(f.UpkeepPerMinute);
                Add(f.ArmyCap);
                Add(f.Population);
                Add(f.CapitalId);
            }
            foreach (var bot in bots)
                Add((int)bot.Mode);
        }
    }
}

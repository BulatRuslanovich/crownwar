using Warcrown.Sim.AI;
using Warcrown.Sim.Core;
using Warcrown.Sim.World;

namespace Warcrown.Sim.Tests;

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
        0x10BDA8039D8E3F36UL,
        0xEF7CCEC7B53534A3UL,
        0xAA357D635D9FB4B3UL,
        0x762BA4C89519B738UL,
        0xEFD89B13A3C67E5EUL,
        0x50E90E2451A10952UL,
        0x9B8930306F995C03UL,
        0x090E3A3C510097E6UL,
        0x480EF5EE3B5A8959UL,
        0xC05EF27EC30D7945UL,
        0x750EE47FFDA0B804UL,
        0xEBEAF4FF727D5418UL,
        0x0135F59FDF182436UL,
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

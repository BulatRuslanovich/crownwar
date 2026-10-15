using System.Diagnostics;
using System.Numerics;
using Lordsio.Sim.Core;
using Lordsio.Sim.Units;
using Lordsio.Sim.World;

namespace Lordsio.Sim.Tests;

/// <summary>
/// Manual benchmarks (run with --filter "TestCategory=Benchmark"): tick cost with large armies.
/// They print timings instead of asserting tight limits, since machines differ.
/// </summary>
[Explicit("benchmarks are run on demand")]
[Category("Benchmark")]
public class PerformanceBenchmarks
{
    [TestCase(50)]
    [TestCase(150)]
    [TestCase(300)]
    [TestCase(400)]
    public void TwoArmiesClash(int perSide)
    {
        var map = TestSim.DefaultMap();
        var rules = TestSim.RealRules();
        var sim = new Simulation(map, rules, MatchSetup.Sandbox);

        // Two armies on open ground east of the river, marching into each other.
        var west = new Vector2(62f, 40f);
        var east = new Vector2(80f, 28f);
        var types = new[] { "infantry", "infantry", "archer", "heavy" };
        var a = Formation.ComputeSlots(sim.Map, west, perSide, 0.36f).Select((p, i) => sim.SpawnUnit(types[i % 4], 0, p).Id).ToList();
        var b = Formation.ComputeSlots(sim.Map, east, perSide, 0.36f).Select((p, i) => sim.SpawnUnit(types[i % 4], 1, p).Id).ToList();
        sim.Issue(new MoveCommand(0, a, east, MoveMode.AttackMove));
        sim.Issue(new MoveCommand(1, b, west, MoveMode.AttackMove));

        var ticks = new List<double>();
        for (int i = 0; i < 30 * Simulation.TicksPerSecond; i++)
        {
            long start = Stopwatch.GetTimestamp();
            sim.Step();
            ticks.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }

        // The first tick applies the two big move orders (formation + one A* per unit), separately reported.
        double orderTick = ticks[0];
        ticks.RemoveAt(0);
        ticks.Sort();
        TestContext.Out.WriteLine(
            $"{perSide * 2} units: order tick {orderTick:0.00} ms; then avg {ticks.Average():0.00} ms, " +
            $"p95 {ticks[(int)(ticks.Count * 0.95)]:0.00} ms, max {ticks[^1]:0.00} ms, survivors {sim.Units.Count}");
        Assert.That(ticks.Average(), Is.LessThan(Simulation.TickDuration * 1000), "a tick must fit its real-time budget");
    }

    /// <summary>A realistic load: four bots playing a whole match, ticks timed together with bot decisions.</summary>
    [TestCase(1)]
    [TestCase(2)]
    public void FourBotMatch(int seed)
    {
        var map = TestSim.DefaultMap();
        var rules = TestSim.RealRules();
        var sim = new Simulation(map, rules, new MatchSetup(4, seed));
        var bots = Enumerable.Range(0, 4).Select(i => new AI.BotBrain(sim, i, AI.BotDifficulty.Hard, i)).ToList();

        var ticks = new List<double>();
        int peakUnits = 0;
        while (!sim.IsOver && sim.Tick < 30 * 60 * Simulation.TicksPerSecond)
        {
            long start = Stopwatch.GetTimestamp();
            sim.Step();
            foreach (var bot in bots)
                bot.Update();
            ticks.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            peakUnits = Math.Max(peakUnits, sim.Units.Count);
        }

        ticks.Sort();
        TestContext.Out.WriteLine(
            $"seed {seed}: {sim.Tick / Simulation.TicksPerSecond / 60f:0.0} min, peak {peakUnits} units; " +
            $"avg {ticks.Average():0.000} ms, p99 {ticks[(int)(ticks.Count * 0.99)]:0.00} ms, max {ticks[^1]:0.00} ms");
    }
}

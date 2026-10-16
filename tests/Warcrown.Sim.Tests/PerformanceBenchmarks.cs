using System.Diagnostics;
using System.Numerics;
using Warcrown.Sim.Core;
using Warcrown.Sim.Units;
using Warcrown.Sim.World;

namespace Warcrown.Sim.Tests;

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
        long allocated = 0;
        for (int i = 0; i < 30 * Simulation.TicksPerSecond; i++)
        {
            long bytes = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            sim.Step();
            ticks.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            if (i > 0)
                allocated += GC.GetAllocatedBytesForCurrentThread() - bytes;
        }

        // The first tick applies the two big move orders (formation + one A* per unit), separately reported.
        double orderTick = ticks[0];
        ticks.RemoveAt(0);
        ticks.Sort();
        TestContext.Out.WriteLine(
            $"{perSide * 2} units: order tick {orderTick:0.00} ms; then avg {ticks.Average():0.00} ms, " +
            $"p95 {ticks[(int)(ticks.Count * 0.95)]:0.00} ms, max {ticks[^1]:0.00} ms, {allocated / 1024.0 / ticks.Count:0.0} KB/tick, " +
            $"survivors {sim.Units.Count}");
        Assert.That(ticks.Average(), Is.LessThan(Simulation.TickDuration * 1000), "a tick must fit its real-time budget");
    }

    /// <summary>
    /// The --demo-army stress scene on a generated map: two armies clash by a castle with a lake beside it, so chases
    /// and marches after a fight go a long way round. Reports the worst ticks, where hitches come from.
    /// </summary>
    [TestCase(150)]
    public void ArmiesClashOnAGeneratedMap(int perSide)
    {
        var sim = new Simulation(MapGenerator.Generate(5), TestSim.RealRules(), new MatchSetup(4, 5));
        var bots = Enumerable.Range(1, 3).Select(i => new AI.BotBrain(sim, i, AI.BotDifficulty.Normal, 5 + i)).ToList();
        var center = TestSim.CapitalOf(sim, 0).Position;
        var types = sim.Rules.Units.All.SelectMany(t => t.Id == "infantry" ? new[] { t.Id, t.Id } : new[] { t.Id }).ToArray();
        var west = sim.Map.FindNearestInComponent(Cell.FromPosition(center + new Vector2(-8f, 0f)), TileGrid.AnyComponent, 12)!.Value.Center;
        var east = sim.Map.FindNearestInComponent(Cell.FromPosition(center + new Vector2(8f, 0f)), TileGrid.AnyComponent, 12)!.Value.Center;
        var a = Formation.ComputeSlots(sim.Map, west, perSide, 0.36f).Select((p, i) => sim.SpawnUnit(types[i % types.Length], 0, p).Id).ToList();
        var b = Formation.ComputeSlots(sim.Map, east, perSide, 0.36f).Select((p, i) => sim.SpawnUnit(types[i % types.Length], 1, p).Id).ToList();
        sim.Issue(new MoveCommand(0, a, east, MoveMode.AttackMove));
        sim.Issue(new MoveCommand(1, b, west, MoveMode.AttackMove));

        var ticks = new List<(double Ms, int Tick)>();
        for (int i = 0; i < 40 * Simulation.TicksPerSecond; i++)
        {
            long start = Stopwatch.GetTimestamp();
            sim.Step();
            ticks.Add((Stopwatch.GetElapsedTime(start).TotalMilliseconds, sim.Tick));
            foreach (var bot in bots)
                bot.Update();
        }
        var worst = ticks.OrderByDescending(t => t.Ms).Take(6).Select(t => $"t{t.Tick} {t.Ms:0.0}");
        TestContext.Out.WriteLine($"{perSide * 2} units by a lake: avg {ticks.Average(t => t.Ms):0.00} ms; worst ticks (ms): {string.Join(", ", worst)}");
    }

    /// <summary>A realistic load: four bots playing a whole match, ticks timed together with bot decisions.</summary>
    [TestCase(1)]
    [TestCase(2)]
    public void FourBotMatch(int seed) =>
        TimeBotMatch($"default map, seed {seed}", new Simulation(TestSim.DefaultMap(), TestSim.RealRules(), new MatchSetup(4, seed)));

    /// <summary>The same on generated maps, the maps the game really plays on.</summary>
    [TestCase(20261015)]
    [TestCase(7)]
    public void FourBotMatchOnAGeneratedMap(int seed) =>
        TimeBotMatch($"generated map {seed}", new Simulation(MapGenerator.Generate(seed), TestSim.RealRules(), new MatchSetup(4, seed)));

    private static void TimeBotMatch(string label, Simulation sim)
    {
        var bots = Enumerable.Range(0, 4).Select(i => new AI.BotBrain(sim, i, AI.BotDifficulty.Hard, i)).ToList();

        var ticks = new List<double>();
        int peakUnits = 0;
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        while (!sim.IsOver && sim.Tick < 30 * 60 * Simulation.TicksPerSecond)
        {
            long start = Stopwatch.GetTimestamp();
            TestSim.StepWithBots(sim, bots);
            ticks.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            peakUnits = Math.Max(peakUnits, sim.Units.Count);
        }
        double allocatedKb = (GC.GetAllocatedBytesForCurrentThread() - allocatedBefore) / 1024.0;

        double total = ticks.Sum();
        ticks.Sort();
        TestContext.Out.WriteLine(
            $"{label}: {sim.Tick / Simulation.TicksPerSecond / 60f:0.0} min, peak {peakUnits} units; total {total / 1000:0.00} s, " +
            $"avg {ticks.Average():0.000} ms, p99 {ticks[(int)(ticks.Count * 0.99)]:0.00} ms, max {ticks[^1]:0.00} ms, " +
            $"{allocatedKb / ticks.Count:0.0} KB/tick");
    }
}

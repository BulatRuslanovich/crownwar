using Warcrown.Sim.AI;
using Warcrown.Sim.Core;
using Warcrown.Sim.World;

namespace Warcrown.Sim.Tests;

/// <summary>
/// Manual balance report for the Defense mode (run with --filter "TestCategory=Balance"): bots stand in for the
/// players on generated Defense maps with the real rules and hold the citadel for as long as they can. Prints how
/// many waves each team beats, how long it lasts and how often outposts fall, to tune the "defense" numbers.
/// </summary>
[Explicit("balance runs take a while")]
[Category("Balance")]
public class DefenseBalanceReport
{
    private const int CapMinutes = 75;

    private sealed record Result(float Minutes, int Cleared, int Launched, int OutpostsLost, int PeakHorde, List<float> ClearSeconds);

    private static Result Play(int seed, int players, BotDifficulty? difficulty)
    {
        var sim = new Simulation(MapGenerator.Generate(seed, players, MatchMode.Defense), TestSim.RealRules(),
            new MatchSetup(players, seed, Mode: MatchMode.Defense));
        var bots = difficulty is { } level
            ? Enumerable.Range(0, players).Select(i => new BotBrain(sim, i, level, seed * 31 + i)).ToList()
            : new List<BotBrain>(); // nobody defends: how long the citadel holds by itself
        var defense = sim.Defense!;
        int outpostsLost = 0, peak = 0;
        var started = new Dictionary<int, int>();
        var clearSeconds = new List<float>();
        while (!sim.IsOver && sim.Tick < CapMinutes * 60 * Simulation.TicksPerSecond)
        {
            sim.Step();
            foreach (var bot in bots)
                bot.Update();
            foreach (var e in sim.TakeEvents())
            {
                switch (e)
                {
                    case BuildingDestroyed d when defense.IsPlayer(d.FactionId) && sim.Rules.GetBuildingType(d.BuildingTypeId).IsCapital:
                        outpostsLost++;
                        break;
                    case WaveStarted w:
                        started[w.Wave] = sim.Tick;
                        break;
                    case WaveCleared c when started.TryGetValue(c.Wave, out int from):
                        clearSeconds.Add((sim.Tick - from) / (float)Simulation.TicksPerSecond);
                        break;
                }
            }
            if (sim.Tick % Simulation.TicksPerSecond == 0)
                peak = Math.Max(peak, sim.Units.Count(u => u.FactionId == defense.HordeFactionId));
        }
        return new Result(sim.Tick / (float)Simulation.TicksPerSecond / 60f, defense.WavesCleared, defense.WavesLaunched, outpostsLost, peak,
            clearSeconds);
    }

    [TestCase(1, BotDifficulty.Normal)]
    [TestCase(2, BotDifficulty.Normal)]
    [TestCase(3, BotDifficulty.Normal)]
    [TestCase(4, BotDifficulty.Normal)]
    [TestCase(2, BotDifficulty.Hard)]
    [TestCase(4, BotDifficulty.Hard)]
    public void HoldTheCitadel(int players, BotDifficulty? difficulty)
    {
        var results = Enumerable.Range(1, 4).Select(seed => Play(seed, players, difficulty)).ToList();
        foreach (var r in results)
            TestContext.Out.WriteLine($"{r.Minutes,5:0.0} min  beaten {r.Cleared,2} of {r.Launched,2}  outposts lost {r.OutpostsLost}  " +
                                      $"peak horde {r.PeakHorde,3}  cleared in {(r.ClearSeconds.Count > 0 ? r.ClearSeconds.Average() : 0f):0} s avg" +
                                      (r.Minutes >= CapMinutes ? "  (still standing)" : ""));
        var minutes = results.Select(r => r.Minutes).OrderBy(m => m).ToList();
        var waves = results.Select(r => r.Cleared).OrderBy(w => w).ToList();
        TestContext.Out.WriteLine($"{players} × {difficulty?.ToString() ?? "nobody"}: median {minutes[minutes.Count / 2]:0.0} min, waves beaten median {waves[waves.Count / 2]} " +
                                  $"(range {waves[0]}–{waves[^1]})");
    }

    /// <summary>Nobody defends: how long the citadel holds by itself against two players' waves.</summary>
    [Test]
    public void CitadelAlone() => HoldTheCitadel(2, null);
}

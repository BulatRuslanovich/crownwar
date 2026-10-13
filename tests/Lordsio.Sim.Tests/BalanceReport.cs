using Lordsio.Sim.AI;
using Lordsio.Sim.Core;
using Lordsio.Sim.World;

namespace Lordsio.Sim.Tests;

/// <summary>
/// Manual balance report (run with --filter "TestCategory=Balance"): plays series of bot matches on the
/// real map and rules and prints match length, army sizes, unit mix and win rates per difficulty.
/// The thresholds asserted are the balance goals of the game, so a rules change that breaks them shows up.
/// </summary>
[Explicit("balance runs take a while")]
[Category("Balance")]
public class BalanceReport
{
    private sealed record MatchResult(float Minutes, int Winner, int PeakUnits, Dictionary<string, int> Recruited);

    private static MatchResult Play(int seed, BotDifficulty[] difficulties)
    {
        string root = TestMaps.ProjectRoot();
        var map = MapLoader.Parse(File.ReadAllText(Path.Combine(root, "Maps", "default.txt")));
        var rules = GameRules.Parse(File.ReadAllText(Path.Combine(root, "Data", "rules.json")));
        var sim = new Simulation(map, rules, new MatchSetup(difficulties.Length, seed));
        var bots = difficulties.Select((d, i) => new BotBrain(sim, i, d, seed * 31 + i)).ToList();
        var recruited = new Dictionary<string, int>();
        int peak = 0;

        while (!sim.IsOver && sim.Tick < 40 * 60 * Simulation.TicksPerSecond)
        {
            sim.Step();
            foreach (var bot in bots)
                bot.Update();
            foreach (var e in sim.TakeEvents())
            {
                if (e is UnitRecruited r && sim.GetUnit(r.UnitId) is { } unit)
                    recruited[unit.Type.Id] = recruited.GetValueOrDefault(unit.Type.Id) + 1;
            }
            peak = Math.Max(peak, sim.Units.Count);
        }
        return new MatchResult(sim.Tick / (float)Simulation.TicksPerSecond / 60f, sim.WinningTeam, peak, recruited);
    }

    [Test]
    public void FreeForAll()
    {
        var difficulties = new[] { BotDifficulty.Hard, BotDifficulty.Normal, BotDifficulty.Normal, BotDifficulty.Easy };
        var results = Enumerable.Range(1, 12).Select(seed => Play(seed, difficulties)).ToList();

        foreach (var r in results)
            TestContext.Out.WriteLine($"{r.Minutes,5:0.0} min  winner {(r.Winner >= 0 ? difficulties[r.Winner].ToString() : "none"),-6}  peak {r.PeakUnits,3} units");
        var minutes = results.Select(r => r.Minutes).OrderBy(m => m).ToList();
        var mix = results.SelectMany(r => r.Recruited).GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.Sum(p => p.Value));
        int total = mix.Values.Sum();
        TestContext.Out.WriteLine($"median {minutes[minutes.Count / 2]:0.0} min, range {minutes[0]:0.0}–{minutes[^1]:0.0}; " +
                                  $"peak units median {results.Select(r => r.PeakUnits).OrderBy(p => p).ElementAt(results.Count / 2)}");
        TestContext.Out.WriteLine("recruited: " + string.Join(", ", mix.Select(p => $"{p.Key} {100f * p.Value / total:0}%")));
        TestContext.Out.WriteLine("wins: " + string.Join(", ", results.GroupBy(r => r.Winner >= 0 ? difficulties[r.Winner].ToString() : "none")
            .Select(g => $"{g.Key} {g.Count()}")));

        Assert.That(results.All(r => r.Winner >= 0), Is.True, "every match must end with a winner");
        Assert.That(minutes[minutes.Count / 2], Is.InRange(8f, 18f), "median match length goal");
    }

    [TestCase(BotDifficulty.Hard, BotDifficulty.Easy, 7)]
    [TestCase(BotDifficulty.Hard, BotDifficulty.Normal, 6)]
    [TestCase(BotDifficulty.Normal, BotDifficulty.Easy, 6)]
    public void HigherDifficultyWinsOneOnOne(BotDifficulty stronger, BotDifficulty weaker, int minWins)
    {
        var results = Enumerable.Range(1, 10).Select(seed => Play(seed, new[] { stronger, weaker })).ToList();
        int wins = results.Count(r => r.Winner == 0);

        TestContext.Out.WriteLine($"{stronger} vs {weaker}: won {wins}/10, minutes " +
                                  string.Join(" ", results.Select(r => r.Minutes.ToString("0.0"))));
        Assert.That(wins, Is.GreaterThanOrEqualTo(minWins), "difficulty must matter");
    }
}

using Warcrown.Sim.AI;
using Warcrown.Sim.Core;
using Warcrown.Sim.World;

namespace Warcrown.Sim.Tests;

/// <summary>
/// Manual balance report (run with --filter "TestCategory=Balance"): plays series of bot matches on
/// generated maps (the seed picks the map, as in the game) with the real rules and prints match length, army sizes, unit mix and win rates per difficulty.
/// The thresholds asserted are the balance goals of the game, so a rules change that breaks them shows up.
/// </summary>
[Explicit("balance runs take a while")]
[Category("Balance")]
public class BalanceReport
{
    private sealed record MatchResult(float Minutes, int Winner, int PeakUnits, Dictionary<string, int> Recruited);

    private static MatchResult Play(int seed, BotDifficulty[] difficulties)
    {
        var rules = TestSim.RealRules();
        var sim = new Simulation(MapGenerator.Generate(seed, difficulties.Length), rules, new MatchSetup(difficulties.Length, seed));
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
        Assert.That(results.Count(r => r.Winner == 0), Is.GreaterThanOrEqualTo(results.Count / 2), "the Hard bot should win most four-way matches");
    }

    /// <summary>
    /// Match length when all four sides are bots of one difficulty, as the main menu sets them up.
    /// The length goal applies to Normal, the default.
    /// </summary>
    [TestCase(BotDifficulty.Easy)]
    [TestCase(BotDifficulty.Normal)]
    [TestCase(BotDifficulty.Hard)]
    public void SameDifficultyFreeForAll(BotDifficulty difficulty)
    {
        var difficulties = Enumerable.Repeat(difficulty, 4).ToArray();
        var minutes = Enumerable.Range(1, 12).Select(seed => Play(seed, difficulties).Minutes).OrderBy(m => m).ToList();
        TestContext.Out.WriteLine($"{difficulty}: median {minutes[minutes.Count / 2]:0.0} min, range {minutes[0]:0.0}–{minutes[^1]:0.0}");
        if (difficulty == BotDifficulty.Normal)
            Assert.That(minutes[minutes.Count / 2], Is.InRange(8f, 18f), "median match length goal");
    }

    /// <summary>
    /// How fast bots grow their economy: income, mines and gold earned at a few moments of the match, next to
    /// a faction nobody plays (castle income only). Bots have no economic bonus; the gap comes from mines.
    /// </summary>
    [Test]
    public void EconomyTimeline()
    {
        var rules = TestSim.RealRules();
        var difficulties = new[] { BotDifficulty.Easy, BotDifficulty.Normal, BotDifficulty.Hard };
        int[] checkpoints = { 30, 60, 120, 180, 300 };
        const int seeds = 8;
        // [difficulty or idle][checkpoint] → (income, upkeep per minute, mines, earned) summed over seeds
        var sums = new (int Income, int Upkeep, int Mines, int Earned, int Upgrades)[difficulties.Length + 1, checkpoints.Length];

        for (int seed = 1; seed <= seeds; seed++)
        {
            var sim = new Simulation(MapGenerator.Generate(seed), rules, new MatchSetup(4, seed));
            var bots = difficulties.Select((d, i) => new BotBrain(sim, i, d, seed * 31 + i)).ToList();
            var earned = new int[4];
            int next = 0;
            while (next < checkpoints.Length)
            {
                sim.Step();
                foreach (var bot in bots)
                    bot.Update();
                foreach (var faction in sim.Factions)
                    earned[faction.Id] += faction.IsEliminated ? 0 : faction.IncomePerSecond;
                if (sim.Tick != checkpoints[next] * Simulation.TicksPerSecond)
                    continue;
                foreach (var faction in sim.Factions)
                {
                    int mines = sim.Buildings.Count(b => b.FactionId == faction.Id && b.IsAlive && b.IsComplete && b.Type.IsMine);
                    var old = sums[faction.Id, next];
                    sums[faction.Id, next] = (old.Income + faction.IncomePerSecond, old.Upkeep + faction.UpkeepPerMinute, old.Mines + mines,
                        old.Earned + earned[faction.Id] / Simulation.TicksPerSecond, old.Upgrades + sim.Buildings.Where(b => b.FactionId == faction.Id && b.IsAlive).Sum(b => b.Type.Tier - 1));
                }
                next++;
            }
        }

        string[] names = difficulties.Select(d => d.ToString()).Append("idle").ToArray();
        TestContext.Out.WriteLine($"average of {seeds} matches: income/s, upkeep/s, mines, gold earned so far (before upkeep), upgrades");
        for (int c = 0; c < checkpoints.Length; c++)
        {
            TestContext.Out.WriteLine($"{checkpoints[c] / 60}:{checkpoints[c] % 60:00}  " + string.Join("   ", names.Select((n, f) =>
                $"{n,-6} {sums[f, c].Income / (float)seeds,4:0.0}/s -{sums[f, c].Upkeep / 60f / seeds,3:0.0} {sums[f, c].Mines / (float)seeds,3:0.0} mines {sums[f, c].Earned / seeds,5}g {sums[f, c].Upgrades / (float)seeds,3:0.0}u")));
        }
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

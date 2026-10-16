using System.Numerics;
using Warcrown.Sim.Buildings;
using Warcrown.Sim.Combat;
using Warcrown.Sim.Core;
using Warcrown.Sim.Units;
using Warcrown.Sim.World;

namespace Warcrown.Sim.Tests;

/// <summary>
/// Balance bench for tuning Data/rules.json (not part of the normal run): equal-gold fights between every pair of
/// fighting units, the worth of monks in a fight, sieges of a tower and a defended castle, and four-bot matches with
/// what they trained. Prints tables (lines starting "BAL"); asserts nothing. Run with
/// <c>dotnet test tests/Warcrown.Sim.Tests --filter "FullyQualifiedName~BalanceBench" --logger "console;verbosity=detailed"</c>.
/// </summary>
[Explicit("Balance bench: prints tables, run by hand when tuning rules.json")]
public class BalanceBench
{
    private static GameRules Rules() =>
        TestSim.RealRules();

    /// <summary>Gold left standing on side A minus side B after the fight, as a share of the budget (+1: A won untouched).</summary>
    private static (float Score, float Seconds) Fight(GameRules rules, (string Type, int Count)[] a, (string Type, int Count)[] b, int budget)
    {
        var sim = new Simulation(TestMaps.Parse(TestMaps.Field(48, 24)), rules, MatchSetup.Sandbox);
        void Place(int faction, (string Type, int Count)[] army, Vector2 at, Vector2 toward)
        {
            var kinds = army.SelectMany(p => Enumerable.Repeat(p.Type, p.Count)).ToList();
            // Ranged and monks stand behind the melee line.
            kinds = kinds.OrderBy(k => rules.Units.Get(k).IsRanged || rules.Units.Get(k).IsHealer ? 1 : 0).ToList();
            var slots = Formation.ComputeSlots(sim.Map, at, kinds.Count, 0.4f);
            slots = slots.OrderBy(s => Vector2.Distance(s, toward)).ToList();
            var ids = kinds.Select((k, i) => sim.SpawnUnit(k, faction, slots[i]).Id).ToArray();
            sim.Issue(new MoveCommand(faction, ids, toward, MoveMode.AttackMove));
        }
        Place(0, a, new Vector2(16f, 12f), new Vector2(32f, 12f));
        Place(1, b, new Vector2(32f, 12f), new Vector2(16f, 12f));
        int t = 0;
        for (; t < 180 * Simulation.TicksPerSecond && sim.Units.Select(u => u.FactionId).Distinct().Count() > 1; t++)
            sim.Step();
        float Value(int f) => sim.Units.Where(u => u.FactionId == f).Sum(u => u.Type.Cost * (float)u.Health / u.MaxHealth);
        return ((Value(0) - Value(1)) / budget, t / (float)Simulation.TicksPerSecond);
    }

    [Test]
    public void Matchups()
    {
        var rules = Rules();
        var fighters = new[] { "infantry", "archer", "cavalry", "crossbowman", "heavy" };
        foreach (int budget in new[] { 600, 1500 })
        {
            TestContext.Out.WriteLine($"BAL budget {budget}: row vs column, + means row wins (share of budget left)");
            TestContext.Out.WriteLine("BAL " + "".PadRight(12) + string.Join("", fighters.Select(f => f.PadLeft(12))));
            foreach (var x in fighters)
            {
                var cells = fighters.Select(y =>
                {
                    if (x == y)
                        return "".PadLeft(12);
                    int nx = (int)MathF.Round(budget / (float)rules.Units.Get(x).Cost), ny = (int)MathF.Round(budget / (float)rules.Units.Get(y).Cost);
                    var (score, _) = Fight(rules, new[] { (x, nx) }, new[] { (y, ny) }, budget);
                    return $"{score:+0.00;-0.00}".PadLeft(12);
                });
                TestContext.Out.WriteLine("BAL " + x.PadRight(12) + string.Join("", cells));
            }
        }
    }

    [Test]
    public void MonksInALongFight()
    {
        var rules = Rules();
        // The same gold: a mixed army with two monks against the same mix without them (spent on more spearmen).
        int monk = rules.Units.Get("monk").Cost, inf = rules.Units.Get("infantry").Cost;
        int extra = (int)MathF.Round(2f * monk / inf);
        foreach (int scale in new[] { 1, 2 })
        {
            var with = new[] { ("infantry", 8 * scale), ("archer", 6 * scale), ("monk", 2 * scale) };
            var without = new[] { ("infantry", (8 + extra) * scale), ("archer", 6 * scale) };
            int budget = (8 * inf + 6 * rules.Units.Get("archer").Cost + 2 * monk) * scale;
            var (score, seconds) = Fight(rules, with, without, budget);
            TestContext.Out.WriteLine($"BAL monks x{scale}: with monks vs without = {score:+0.00;-0.00} after {seconds:0} s");
        }
    }

    [Test]
    public void Sieges()
    {
        var rules = Rules();
        foreach (var (kind, budget) in new[] { ("ram", 750), ("trebuchet", 700), ("infantry", 750), ("heavy", 750) })
        {
            foreach (var target in new[] { "tower", "castle" })
            {
                var sim = new Simulation(TestMaps.Parse(TestMaps.Field(48, 24), "start 34 11"), rules, new MatchSetup(1, ShuffleStarts: false));
                Building aim;
                if (target == "tower")
                {
                    sim.GetFaction(0)!.Treasury.Add(1000);
                    sim.SpawnUnit("infantry", 0, new Vector2(29.5f, 6.5f));
                    sim.Issue(new BuildCommand(0, "tower", new Cell(28, 4)));
                    for (int i = 0; i < 40 * Simulation.TicksPerSecond; i++) sim.Step();
                    foreach (var u in sim.Units.ToList()) CombatSystem.ApplyDamage(u, 9999, false, 1);
                    sim.Step();
                    aim = sim.Buildings.Single(b => b.Type.Id == "tower");
                }
                else
                {
                    aim = sim.Buildings.Single(b => b.Type.IsCapital);
                }
                int n = (int)MathF.Round(budget / (float)rules.Units.Get(kind).Cost);
                var slots = Formation.ComputeSlots(sim.Map, new Vector2(10f, 12f), n, 0.45f);
                var ids = slots.Select(s => sim.SpawnUnit(kind, 1, s).Id).ToArray();
                sim.Issue(new AttackCommand(1, ids, aim.Id));
                int t = 0;
                for (; t < 600 * Simulation.TicksPerSecond && aim.IsAlive && sim.Units.Any(u => u.FactionId == 1); t++)
                    sim.Step();
                TestContext.Out.WriteLine($"BAL siege {n} {kind} ({budget} g) vs {target}: {(aim.IsAlive ? $"stands at {aim.Health}/{aim.MaxHealth}" : "falls")} after {t / 20f:0} s, {sim.Units.Count(u => u.FactionId == 1)} left");
            }
        }
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    public void BotMatches(int seed)
    {
        var rules = Rules();
        var map = TestSim.DefaultMap();
        var sim = new Simulation(map, rules, new MatchSetup(4, seed));
        var difficulties = new[] { Warcrown.Sim.AI.BotDifficulty.Hard, Warcrown.Sim.AI.BotDifficulty.Normal, Warcrown.Sim.AI.BotDifficulty.Normal, Warcrown.Sim.AI.BotDifficulty.Easy };
        var bots = Enumerable.Range(0, 4).Select(i => new Warcrown.Sim.AI.BotBrain(sim, i, difficulties[i], seed * 10 + i)).ToList();
        var recruited = new Dictionary<string, int>();
        for (int t = 0; t < 90 * 60 * Simulation.TicksPerSecond && !sim.IsOver; t++)
        {
            sim.Step();
            foreach (var b in bots) b.Update();
            foreach (var e in sim.TakeEvents())
                if (e is UnitRecruited u && sim.GetUnit(u.UnitId) is { } unit)
                    recruited[unit.Type.Id] = recruited.GetValueOrDefault(unit.Type.Id) + 1;
        }
        TestContext.Out.WriteLine($"BAL bots seed {seed}: over={sim.IsOver} at {sim.Tick / 20f / 60f:0.0} min, winner {sim.WinningTeam}; "
            + string.Join(", ", recruited.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}")));
    }

    /// <summary>
    /// Every defensive building at each level against attackers of each kind worth twice what was spent on it: whether
    /// it stands, and how much gold the attackers lost (the defender's return on the investment).
    /// </summary>
    [Test]
    public void Defences()
    {
        var rules = Rules();
        var attackers = new[] { "infantry", "archer", "cavalry", "crossbowman", "heavy", "ram", "trebuchet" };
        TestContext.Out.WriteLine("BAL defence (attackers worth 2x the building): cells = attackers' gold lost / building's cost, '*' = building fell");
        TestContext.Out.WriteLine("BAL " + "".PadRight(12) + string.Join("", attackers.Select(a => a.PadLeft(12))));
        foreach (var kind in new[] { "tower", "catapult" })
        {
            var type = rules.GetBuildingType(kind);
            for (int tier = 1; tier <= type.MaxTier; tier++)
            {
                int invested = type.Cost;
                for (var t = type; t.Tier < tier; t = t.NextTier!)
                    invested += t.UpgradeCost;
                var cells = attackers.Select(a =>
                {
                    var (lost, fell) = Assault(rules, kind, tier, a, invested * 2);
                    return $"{lost / (float)invested:0.00}{(fell ? "*" : " ")}".PadLeft(12);
                });
                TestContext.Out.WriteLine("BAL " + $"{kind} t{tier}".PadRight(12) + string.Join("", cells));
            }
        }
    }

    private static (int AttackersLost, bool Fell) Assault(GameRules rules, string kind, int tier, string attacker, int budget)
    {
        var sim = new Simulation(TestMaps.Parse(TestMaps.Field(48, 24), "start 2 2"), rules, new MatchSetup(1, ShuffleStarts: false));
        var faction = sim.GetFaction(0)!;
        faction.Treasury.Add(5000);
        var builder = sim.SpawnUnit("infantry", 0, new Vector2(30.5f, 14.5f));
        sim.Issue(new BuildCommand(0, kind, new Cell(30, 11)));
        for (int i = 0; i < 40 * Simulation.TicksPerSecond; i++) sim.Step();
        var site = sim.Buildings.Single(b => b.Type.Id == kind);
        while (site.Type.Tier < tier)
        {
            sim.Issue(new UpgradeCommand(0, site.Id));
            for (int i = 0; i < 40 * Simulation.TicksPerSecond; i++) sim.Step();
        }
        foreach (var u in sim.Units.ToList())
            CombatSystem.ApplyDamage(u, 9999, false, 1);
        sim.Step();
        var type = rules.Units.Get(attacker);
        int n = Math.Max(1, (int)MathF.Round(budget / (float)type.Cost));
        var slots = Formation.ComputeSlots(sim.Map, new Vector2(14f, 12f), n, 0.45f);
        var ids = slots.Select(p => sim.SpawnUnit(attacker, 1, p).Id).ToArray();
        sim.Issue(new AttackCommand(1, ids, site.Id));
        for (int t = 0; t < 240 * Simulation.TicksPerSecond && site.IsAlive && sim.Units.Any(u => u.FactionId == 1); t++)
            sim.Step();
        int alive = sim.Units.Count(u => u.FactionId == 1);
        return ((n - alive) * type.Cost, !site.IsAlive);
    }
}

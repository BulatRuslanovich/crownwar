using System.Numerics;
using Lordsio.Sim.AI;
using Lordsio.Sim.Core;
using Lordsio.Sim.Units;
using Lordsio.Sim.World;

namespace Lordsio.Sim.Tests;

public class BotTests
{
    private static Simulation CreateDefaultMatch(int factions, int seed)
    {
        string root = TestMaps.ProjectRoot();
        var map = MapLoader.Parse(File.ReadAllText(Path.Combine(root, "Maps", "default.txt")));
        var rules = GameRules.Parse(File.ReadAllText(Path.Combine(root, "Data", "rules.json")));
        return new Simulation(map, rules, new MatchSetup(factions, seed));
    }

    private static void Run(Simulation sim, IReadOnlyList<BotBrain> bots, float seconds, Func<bool>? stop = null)
    {
        for (int i = 0; i < seconds * Simulation.TicksPerSecond && !sim.IsOver && !(stop?.Invoke() ?? false); i++)
        {
            sim.Step();
            foreach (var bot in bots)
                bot.Update();
        }
    }

    [TestCase(BotDifficulty.Easy)]
    [TestCase(BotDifficulty.Normal)]
    [TestCase(BotDifficulty.Hard)]
    public void BotBuildsAMineAndGrowsItsArmy(BotDifficulty difficulty)
    {
        var sim = CreateDefaultMatch(2, seed: 3);
        var bot = new BotBrain(sim, 1, difficulty, seed: 1);
        int startArmy = sim.Units.Count(u => u.FactionId == 1);

        Run(sim, new[] { bot }, 120f);

        var mines = sim.Buildings.Count(b => b.FactionId == 1 && b.Type.IsMine);
        Assert.That(mines, Is.GreaterThanOrEqualTo(1), "a bot must expand its economy");
        Assert.That(sim.GetFaction(1)!.IncomePerSecond, Is.GreaterThan(3));
        Assert.That(sim.Units.Count(u => u.FactionId == 1), Is.GreaterThan(startArmy), "a bot must recruit");
    }

    [Test]
    public void BotDefendsItsCastle()
    {
        var sim = CreateDefaultMatch(2, seed: 3);
        var bot = new BotBrain(sim, 1, BotDifficulty.Normal, seed: 1);
        var castle = sim.GetBuilding(sim.GetFaction(1)!.CapitalId)!;
        var raidPoint = sim.Map.FindNearestInComponent(Cell.FromPosition(castle.Position + new Vector2(0f, 6f)), TileGrid.AnyComponent, 8)!.Value.Center;
        var raiders = Formation.ComputeSlots(sim.Map, raidPoint, 3, 0.3f).Select(p => sim.SpawnUnit("infantry", 0, p)).ToList();
        // The raiders stand still; only the bot's reaction can bring them into a fight.
        sim.Issue(new MoveCommand(0, raiders.Select(r => r.Id).ToList(), raidPoint, MoveMode.Move));

        Run(sim, new[] { bot }, 2f);
        Assert.That(bot.Mode, Is.EqualTo(BotMode.Defend));

        Run(sim, new[] { bot }, 40f, () => raiders.All(r => !r.IsAlive));
        Assert.That(raiders.All(r => !r.IsAlive), Is.True, "the bot's army should wipe out the raid");
    }

    [Test]
    public void BotsOnlyChangeTheWorldThroughCommands()
    {
        // A bot that thinks but whose commands are thrown away must leave the world untouched.
        var sim = CreateDefaultMatch(2, seed: 3);
        var bot = new BotBrain(sim, 1, BotDifficulty.Hard, seed: 1);
        int gold = sim.GetFaction(1)!.Treasury.Gold;
        int buildings = sim.Buildings.Count;

        for (int i = 0; i < 10; i++)
            bot.Think();

        Assert.That(sim.GetFaction(1)!.Treasury.Gold, Is.EqualTo(gold));
        Assert.That(sim.Buildings.Count, Is.EqualTo(buildings));
        Assert.That(sim.Units.All(u => u.Order == UnitOrder.None), Is.True, "orders only take effect when the simulation steps");
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void FourBotsPlayAMatchToTheEnd(int seed)
    {
        var sim = CreateDefaultMatch(4, seed);
        var difficulties = new[] { BotDifficulty.Hard, BotDifficulty.Normal, BotDifficulty.Normal, BotDifficulty.Easy };
        var bots = Enumerable.Range(0, 4).Select(i => new BotBrain(sim, i, difficulties[i], seed * 10 + i)).ToList();

        Run(sim, bots, 30 * 60f);

        string summary = string.Join("; ", sim.Factions.Select(f =>
            $"f{f.Id} {(f.IsEliminated ? "out" : "alive")} gold {f.Treasury.Gold} income {f.IncomePerSecond} " +
            $"army {sim.Units.Count(u => u.FactionId == f.Id)} buildings {sim.Buildings.Count(b => b.FactionId == f.Id)} " +
            $"mode {bots[f.Id].Mode}"));
        TestContext.Out.WriteLine($"tick {sim.Tick} ({sim.Tick / Simulation.TicksPerSecond / 60f:0.0} min): {summary}");

        Assert.That(sim.IsOver, Is.True, $"no winner after 30 minutes: {summary}");
        Assert.That(sim.WinnerId, Is.InRange(0, 3));
    }
}

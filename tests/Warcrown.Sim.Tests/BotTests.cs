using System.Numerics;
using Warcrown.Sim.AI;
using Warcrown.Sim.Core;
using Warcrown.Sim.Units;
using Warcrown.Sim.World;
using static Warcrown.Sim.Tests.TestSim;

namespace Warcrown.Sim.Tests;

public class BotTests
{
    private static void Run(Simulation sim, IReadOnlyList<BotBrain> bots, float seconds, Func<bool>? stop = null)
    {
        for (int i = 0; i < seconds * Simulation.TicksPerSecond && !sim.IsOver && !(stop?.Invoke() ?? false); i++)
            StepWithBots(sim, bots);
    }

    [Test]
    public void BotsTakeTheFortsBetweenThem()
    {
        var sim = new Simulation(MapGenerator.Generate(5, 2), RealRules(), new MatchSetup(2, 5));
        var bots = new[] { new BotBrain(sim, 0, BotDifficulty.Hard, 1), new BotBrain(sim, 1, BotDifficulty.Normal, 2) };
        var taken = new List<FortCaptured>();

        for (int i = 0; i < 20 * 60 * Simulation.TicksPerSecond && !sim.IsOver && taken.Count == 0; i++)
        {
            StepWithBots(sim, bots);
            taken.AddRange(sim.TakeEvents().OfType<FortCaptured>().Where(e => e.FactionId >= 0));
        }

        Assert.That(taken, Is.Not.Empty, "a bot must go for a fort once its army can take one");
        Assert.That(sim.Buildings.Single(b => b.Id == taken[0].BuildingId).FactionId, Is.EqualTo(taken[0].FactionId));
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

        // Long matches are part of the game (castles fall to sieges, armies train in the castle): an hour is the bound.
        Run(sim, bots, 60 * 60f);

        string summary = string.Join("; ", sim.Factions.Select(f =>
            $"f{f.Id} {(f.IsEliminated ? "out" : "alive")} gold {f.Treasury.Gold} income {f.IncomePerSecond} " +
            $"army {sim.Units.Count(u => u.FactionId == f.Id)} buildings {sim.Buildings.Count(b => b.FactionId == f.Id)} " +
            $"mode {bots[f.Id].Mode}"));
        TestContext.Out.WriteLine($"tick {sim.Tick} ({sim.Tick / Simulation.TicksPerSecond / 60f:0.0} min): {summary}");

        Assert.That(sim.IsOver, Is.True, $"no winner after an hour: {summary}");
        Assert.That(sim.WinningTeam, Is.InRange(0, 3));
    }
}

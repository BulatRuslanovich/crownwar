using System.Numerics;
using Warcrown.Sim.AI;
using Warcrown.Sim.Core;
using Warcrown.Sim.World;

namespace Warcrown.Sim.Tests;

public class TeamTests
{
    private static readonly string[] Field = TestMaps.Field(24, 12);

    private static Simulation CreateSim(int[] teams, params string[] objects) =>
        new(TestMaps.Parse(Field, objects), TestMaps.Rules, new MatchSetup(teams.Length, ShuffleStarts: false, Teams: teams));

    private static void RunSeconds(Simulation sim, float seconds)
    {
        for (int i = 0; i < seconds * Simulation.TicksPerSecond && !sim.IsOver; i++)
            sim.Step();
    }

    [Test]
    public void AlliesNeverFight()
    {
        var sim = CreateSim(new[] { 0, 0 }, "start 1 1", "start 20 8");
        var a = sim.SpawnUnit("infantry", 0, new Vector2(10.5f, 5.5f));
        var b = sim.SpawnUnit("infantry", 1, new Vector2(11.2f, 5.5f));

        RunSeconds(sim, 5f);

        Assert.That((a.Health, b.Health), Is.EqualTo((100, 100)));
        Assert.That(sim.AreEnemies(0, 1), Is.False);
    }

    [Test]
    public void TowersShootOnlyEnemies()
    {
        var sim = CreateSim(new[] { 0, 0, 1 }, "start 1 1", "start 20 8", "start 20 1", "unit dummy 0 6 6");
        sim.Issue(new BuildCommand(0, "tower", new Cell(6, 4)));
        RunSeconds(sim, TestMaps.Tower.BuildTime + 0.2f);
        var ally = sim.SpawnUnit("dummy", 1, new Vector2(8.5f, 4.5f));
        var enemy = sim.SpawnUnit("dummy", 2, new Vector2(8.5f, 3.5f));

        RunSeconds(sim, 3f);

        Assert.That(ally.Health, Is.EqualTo(100));
        Assert.That(enemy.Health, Is.LessThan(100));
    }

    [Test]
    public void CannotOrderAttackOnAlly()
    {
        var sim = CreateSim(new[] { 0, 0 }, "start 1 1", "start 20 8");
        var unit = sim.SpawnUnit("infantry", 0, new Vector2(10.5f, 5.5f));
        var ally = sim.SpawnUnit("dummy", 1, new Vector2(14.5f, 5.5f));

        sim.Issue(new AttackCommand(0, new[] { unit.Id }, ally.Id));
        RunSeconds(sim, 3f);

        Assert.That(ally.Health, Is.EqualTo(100));
    }

    [Test]
    public void TeamWinsTogetherEvenAfterLosingAMember()
    {
        var sim = CreateSim(new[] { 0, 0, 1 }, "start 1 1", "start 20 1", "start 20 8");

        Combat.CombatSystem.ApplyDamage(sim.GetBuilding(sim.GetFaction(0)!.CapitalId)!, 100_000, ranged: false, attackerFactionId: 2);
        sim.Step();
        Assert.That(sim.GetFaction(0)!.IsEliminated, Is.True);
        Assert.That(sim.IsOver, Is.False, "faction 1 still fights for team 0");

        Combat.CombatSystem.ApplyDamage(sim.GetBuilding(sim.GetFaction(2)!.CapitalId)!, 100_000, ranged: false, attackerFactionId: 1);
        sim.Step();
        Assert.That(sim.IsOver, Is.True);
        Assert.That(sim.WinningTeam, Is.EqualTo(0));
        Assert.That(sim.TakeEvents().OfType<MatchEnded>().Single().WinningTeam, Is.EqualTo(0));
    }

    [Test]
    public void AllAlliedMatchHasNoAutomaticWinner()
    {
        var sim = CreateSim(new[] { 3, 3 }, "start 1 1", "start 20 8");

        RunSeconds(sim, 2f);

        Assert.That(sim.IsOver, Is.False);
    }
}

public class LockstepDeterminismTests
{
    private static (Simulation Sim, List<BotBrain> Bots) CreateBotMatch(int seed)
    {
        var sim = TestSim.CreateDefaultMatch(4, seed, teams: new[] { 0, 1, 0, 1 });
        var bots = Enumerable.Range(0, 4).Select(i => new BotBrain(sim, i, BotDifficulty.Hard, seed + i)).ToList();
        return (sim, bots);
    }

    [Test]
    public void IdenticalInputsGiveIdenticalChecksums()
    {
        var (a, botsA) = CreateBotMatch(seed: 9);
        var (b, botsB) = CreateBotMatch(seed: 9);

        for (int i = 0; i < 5 * 60 * Simulation.TicksPerSecond && !a.IsOver; i++)
        {
            TestSim.StepWithBots(a, botsA);
            TestSim.StepWithBots(b, botsB);
            if (i % Simulation.TicksPerSecond == 0)
                Assert.That(b.ComputeChecksum(), Is.EqualTo(a.ComputeChecksum()), $"desync at tick {a.Tick}");
        }
        Assert.That(b.Units.Count, Is.EqualTo(a.Units.Count));
    }

    [Test]
    public void ADifferentCommandChangesTheChecksum()
    {
        var (a, botsA) = CreateBotMatch(seed: 9);
        var (b, botsB) = CreateBotMatch(seed: 9);
        var unit = b.Units.First(u => u.FactionId == 0);

        b.Issue(new MoveCommand(0, new[] { unit.Id }, unit.Position + new Vector2(3f, 0f)));
        for (int i = 0; i < 10; i++)
        {
            TestSim.StepWithBots(a, botsA);
            TestSim.StepWithBots(b, botsB);
        }

        Assert.That(b.ComputeChecksum(), Is.Not.EqualTo(a.ComputeChecksum()));
    }
}

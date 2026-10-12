using System.Numerics;
using Lordsio.Sim.Core;
using Lordsio.Sim.World;

namespace Lordsio.Sim.Tests;

/// <summary>End-to-end scenarios on the real map and rules shipped with the game.</summary>
public class MatchScenarioTests
{
    private static Simulation CreateDefaultMatch()
    {
        string root = TestMaps.ProjectRoot();
        var map = MapLoader.Parse(File.ReadAllText(Path.Combine(root, "Maps", "default.txt")));
        var rules = GameRules.Parse(File.ReadAllText(Path.Combine(root, "Data", "rules.json")));
        return new Simulation(map, rules);
    }

    [Test]
    public void StormingTheEnemyCastleWinsTheMatch()
    {
        var sim = CreateDefaultMatch();
        var enemyCastle = sim.Settlements.Single(s => s.OwnerId == 1 && s.Type.IsCapital);
        var staging = enemyCastle.Cell.Center + new Vector2(-6f, 4f);
        var army = Enumerable.Range(0, 20)
            .Select(i => sim.SpawnUnit(i % 4 == 0 ? "archer" : "infantry", 0, staging + new Vector2(i % 5, i / 5) * 0.8f))
            .Select(u => u.Id).ToArray();

        sim.Issue(new MoveCommand(0, army, enemyCastle.Cell.Center, MoveMode.AttackMove));
        for (int i = 0; i < 120 * Simulation.TicksPerSecond && !sim.IsOver; i++)
            sim.Step();

        Assert.That(sim.IsOver, Is.True, "the castle should fall within two minutes");
        Assert.That(sim.WinnerId, Is.EqualTo(0));
        Assert.That(sim.GetFaction(1)!.IsEliminated, Is.True);
    }

    /// <summary>Roughly equal gold on both sides; the counter relationships the unit data is meant to create.</summary>
    [TestCase("heavy", 1, "archer", 2, Description = "arrow armor: heavies shrug off archers")]
    [TestCase("infantry", 2, "heavy", 1, Description = "cheap infantry swarm the slow heavy")]
    [TestCase("heavy", 1, "infantry", 1, Description = "one heavy beats one infantry")]
    public void UnitMatchups(string winnerType, int winnerCount, string loserType, int loserCount)
    {
        var rules = GameRules.Parse(File.ReadAllText(Path.Combine(TestMaps.ProjectRoot(), "Data", "rules.json")));
        var field = Enumerable.Repeat(new string('.', 30), 12).ToArray();
        var sim = new Simulation(TestMaps.Parse(field), rules);
        for (int i = 0; i < winnerCount; i++)
            sim.SpawnUnit(winnerType, 0, new Vector2(8.5f, 5.5f + i));
        for (int i = 0; i < loserCount; i++)
            sim.SpawnUnit(loserType, 1, new Vector2(13.5f, 5.5f + i));

        for (int i = 0; i < 90 * Simulation.TicksPerSecond && sim.Units.Select(u => u.FactionId).Distinct().Count() > 1; i++)
            sim.Step();

        Assert.That(sim.Units.Select(u => u.FactionId).Distinct(), Is.EqualTo(new[] { 0 }),
            $"{winnerCount} {winnerType} should beat {loserCount} {loserType}");
    }

    [Test]
    public void IdleMatchStaysStableForTenMinutes()
    {
        var sim = CreateDefaultMatch();
        int units = sim.Units.Count;

        for (int i = 0; i < 600 * Simulation.TicksPerSecond; i++)
            sim.Step();

        // Nobody moves on their own: garrisons and starting armies are out of each other's sight.
        Assert.That(sim.IsOver, Is.False);
        Assert.That(sim.Units, Has.Count.EqualTo(units));
        Assert.That(sim.Settlements.Count(s => s.OwnerId == 0), Is.EqualTo(1));
        Assert.That(sim.GetFaction(0)!.Treasury.Gold, Is.EqualTo(150 + 600 * 5));
    }
}

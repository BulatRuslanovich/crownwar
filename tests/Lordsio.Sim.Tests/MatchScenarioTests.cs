using System.Numerics;
using Lordsio.Sim.Core;
using Lordsio.Sim.Units;
using Lordsio.Sim.World;

namespace Lordsio.Sim.Tests;

/// <summary>End-to-end scenarios on the real map and rules shipped with the game.</summary>
public class MatchScenarioTests
{
    private static GameRules RealRules() =>
        GameRules.Parse(File.ReadAllText(Path.Combine(TestMaps.ProjectRoot(), "Data", "rules.json")));

    private static Simulation CreateDefaultMatch(int factions, int seed = 1)
    {
        var map = MapLoader.Parse(File.ReadAllText(Path.Combine(TestMaps.ProjectRoot(), "Maps", "default.txt")));
        return new Simulation(map, RealRules(), new MatchSetup(factions, seed));
    }

    [Test]
    public void CapitalsAreRandomButReproducible()
    {
        var a = CreateDefaultMatch(2, seed: 7).Buildings.Select(b => b.Anchor).ToList();
        var b = CreateDefaultMatch(2, seed: 7).Buildings.Select(b => b.Anchor).ToList();
        var layouts = Enumerable.Range(0, 20)
            .Select(seed => string.Join(";", CreateDefaultMatch(2, seed).Buildings.Select(x => x.Anchor)))
            .Distinct().Count();

        Assert.That(a, Is.EqualTo(b), "same seed, same layout");
        Assert.That(layouts, Is.GreaterThan(1), "different seeds shuffle the starts");
    }

    [Test]
    public void ExpandWithAMineAndWinBySiege()
    {
        var sim = CreateDefaultMatch(2);
        var mine = sim.Deposits.OrderBy(d => Vector2.Distance(d.Center, sim.GetBuilding(sim.GetFaction(0)!.CapitalId)!.Position)).First();
        var starters = sim.Units.Where(u => u.FactionId == 0).Select(u => u.Id).ToArray();

        // Walk the starting army to the nearest deposit and put a mine on it.
        sim.Issue(new MoveCommand(0, starters, mine.Center + new Vector2(0f, 2f)));
        for (int i = 0; i < 20 * Simulation.TicksPerSecond && sim.CanBuild(0, "mine", mine.Anchor) != BuildError.None; i++)
            sim.Step();
        sim.Issue(new BuildCommand(0, "mine", mine.Anchor));
        for (int i = 0; i < 12 * Simulation.TicksPerSecond; i++)
            sim.Step();
        Assert.That(sim.GetFaction(0)!.IncomePerSecond, Is.EqualTo(3 + mine.Yield), "castle base income plus the mine");

        // Then storm the enemy capital. The castle is a fortress (8000 hp, shoots back): swords barely scratch it,
        // so it takes a real army with siege engines — 24 units, a quarter of them rams, the rest heavies and infantry.
        var enemyCapital = sim.GetBuilding(sim.GetFaction(1)!.CapitalId)!;
        // Capitals sit at random corners, so snap the staging area to open ground near the enemy.
        var staging = sim.Map.FindNearestInComponent(Cell.FromPosition(enemyCapital.Position + new Vector2(0f, 6f)),
            TileGrid.AnyComponent, 10)!.Value.Center;
        var army = Formation.ComputeSlots(sim.Map, staging, 24, 0.4f)
            .Select((slot, i) => sim.SpawnUnit((i % 4) switch { 0 => "ram", 1 => "heavy", _ => "infantry" }, 0, slot))
            .Select(u => u.Id).ToArray();
        sim.Issue(new AttackCommand(0, army, enemyCapital.Id));
        for (int i = 0; i < 120 * Simulation.TicksPerSecond && !sim.IsOver; i++)
            sim.Step();

        Assert.That(sim.IsOver, Is.True, "the capital should fall within two minutes");
        Assert.That(sim.WinningTeam, Is.EqualTo(0));
    }

    /// <summary>Roughly equal gold on both sides; the counter relationships the unit data is meant to create.</summary>
    [TestCase("heavy", 1, "archer", 2, Description = "arrow armor: heavies shrug off archers")]
    [TestCase("infantry", 3, "heavy", 1, Description = "cheap infantry swarm the slow heavy")]
    [TestCase("infantry", 3, "cavalry", 1, Description = "spearmen bring down a rider")]
    [TestCase("cavalry", 1, "archer", 2, Description = "a rider runs down archers")]
    [TestCase("crossbowman", 2, "heavy", 1, Description = "bolts go through plate")]
    [TestCase("archer", 3, "infantry", 3, Description = "archers shoot down spearmen")]
    [TestCase("heavy", 1, "infantry", 1, Description = "one heavy beats one infantry")]
    public void UnitMatchups(string winnerType, int winnerCount, string loserType, int loserCount)
    {
        var sim = new Simulation(TestMaps.Parse(TestMaps.Field(30, 12)), RealRules(), MatchSetup.Sandbox);
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
    public void IdleFourPlayerMatchStaysStable()
    {
        var sim = CreateDefaultMatch(4);
        int units = sim.Units.Count;

        for (int i = 0; i < 300 * Simulation.TicksPerSecond; i++)
            sim.Step();

        // Starting armies stand by their castles, far out of each other's sight.
        Assert.That(sim.IsOver, Is.False);
        Assert.That(sim.Units, Has.Count.EqualTo(units));
        // Five minutes of castle income minus whatever upkeep of the starting army the allowance does not cover.
        Assert.That(sim.GetFaction(0)!.Treasury.Gold, Is.EqualTo(200 + 300 * 3 - 5 * sim.GetFaction(0)!.UpkeepPerMinute));
    }
}

using System.Numerics;
using Lordsio.Sim.Core;
using Lordsio.Sim.World;

namespace Lordsio.Sim.Tests;

public class VisibilityTests
{
    private static readonly string[] Field = TestMaps.Field(40, 20);

    /// <summary>Faction 0 and 1 (teams as given) with one infantry each; infantry sees 5 tiles in the test rules.</summary>
    private static Simulation CreateSim(params int[] teams) =>
        new(TestMaps.Parse(Field, "unit infantry 0 5 5", "unit infantry 1 30 5", "unit infantry 2 30 15"),
            TestMaps.Rules, new MatchSetup(0, ShuffleStarts: false, Teams: teams.Length > 0 ? teams : null)); // factions come from the units

    private static Visibility Update(Simulation sim, int team)
    {
        var fog = new Visibility(sim.Map.Width, sim.Map.Height);
        fog.Update(sim, team);
        return fog;
    }

    [Test]
    public void UnitsSeeWithinTheirSightRange()
    {
        var sim = CreateSim();
        var fog = Update(sim, team: 0);

        Assert.That(fog.IsVisible(new Cell(5, 5)), Is.True);
        Assert.That(fog.IsVisible(new Cell(9, 5)), Is.True, "4 tiles away");
        Assert.That(fog.IsVisible(new Cell(11, 5)), Is.False, "6 tiles away");
        Assert.That(fog.IsVisible(new Cell(9, 9)), Is.False, "diagonal beyond the circle");
        Assert.That(fog.IsVisible(new Cell(30, 5)), Is.False, "the enemy is far away");
    }

    [Test]
    public void AlliesShareSightEnemiesDoNot()
    {
        var sim = CreateSim(0, 0, 1);
        var fog = Update(sim, team: 0);

        Assert.That(fog.IsVisible(new Cell(30, 5)), Is.True, "the ally's surroundings");
        Assert.That(fog.IsVisible(new Cell(30, 15)), Is.False, "the enemy's surroundings");
    }

    [Test]
    public void ExploredCellsStayExploredWhenTheUnitLeaves()
    {
        var sim = CreateSim();
        var fog = new Visibility(sim.Map.Width, sim.Map.Height);
        fog.Update(sim, 0);
        int version = fog.Version;

        sim.Issue(new MoveCommand(0, new[] { sim.Units.First(u => u.FactionId == 0).Id }, new Vector2(20.5f, 15.5f)));
        for (int i = 0; i < 6 * Simulation.TicksPerSecond; i++)
            sim.Step();
        fog.Update(sim, 0);

        Assert.That(fog.Version, Is.GreaterThan(version));
        Assert.That(fog.IsVisible(new Cell(5, 5)), Is.False, "no longer in sight");
        Assert.That(fog.IsExplored(new Cell(5, 5)), Is.True, "but remembered");
        Assert.That(fog.IsExplored(new Cell(35, 2)), Is.False, "never seen");
    }

    [Test]
    public void UnchangedSightKeepsTheVersion()
    {
        var sim = CreateSim();
        var fog = new Visibility(sim.Map.Width, sim.Map.Height);
        fog.Update(sim, 0);
        int version = fog.Version;

        fog.Update(sim, 0);

        Assert.That(fog.Version, Is.EqualTo(version));
    }

    [Test]
    public void WatchtowerSeesFarOnlyOnceBuilt()
    {
        var watchtower = TestMaps.Barracks with
        {
            Id = "watchtower", Name = "Watchtower", Size = 3, Cost = 50, ArmyCap = 0, Recruits = Array.Empty<string>(), SightRange = 12f,
        };
        var rules = new GameRules(100, 4f, Array.Empty<StartingUnits>(), TestMaps.Rules.Units,
            new[] { TestMaps.Castle, TestMaps.Mine, TestMaps.Barracks, TestMaps.Tower, watchtower });
        var sim = new Simulation(TestMaps.Parse(Field, "start 1 1", "unit infantry 0 6 6"), rules, new MatchSetup(1, ShuffleStarts: false));
        var far = new Cell(17, 9); // 11 tiles east of the tower's center, out of the infantry's sight

        sim.Issue(new BuildCommand(0, "watchtower", new Cell(5, 8)));
        sim.Step();
        Assert.That(Update(sim, 0).IsVisible(far), Is.False, "a construction site only looks around itself");

        for (int i = 0; i < (watchtower.BuildTime + 0.2f) * Simulation.TicksPerSecond; i++)
            sim.Step();
        Assert.That(sim.Buildings.Single(b => b.Type.Id == "watchtower").IsComplete, Is.True);
        Assert.That(Update(sim, 0).IsVisible(far), Is.True);
    }

    [Test]
    public void WatchtowerRulesMatchTheDesign()
    {
        var rules = TestSim.RealRules();
        var watchtower = rules.GetBuildingType("watchtower");
        Assert.That((watchtower.Size, watchtower.MaxHealth, watchtower.Cost), Is.EqualTo((3, 2000, 300)));
        Assert.That(watchtower.Attack, Is.Null, "it does not shoot");
        Assert.That(watchtower.EffectiveSight, Is.GreaterThan(rules.GetBuildingType("castle").EffectiveSight));
    }

    [Test]
    public void BuildingsSeeAtLeastAsFarAsTheyShoot()
    {
        Assert.That(TestMaps.Tower.EffectiveSight, Is.EqualTo(TestMaps.Tower.Attack!.Range + 1f));
        Assert.That(TestMaps.Barracks.EffectiveSight, Is.EqualTo(TestMaps.Barracks.Size / 2f + 3f));
        Assert.That((TestMaps.Castle with { SightRange = 9f }).EffectiveSight, Is.EqualTo(9f));

        var sim = TestMaps.WithCapitals(1, Field, "start 1 1");
        var fog = Update(sim, 0);
        Assert.That(fog.IsVisible(new Cell(5, 2)), Is.True, "around the castle");
    }

    [Test]
    public void RevealAllShowsEverything()
    {
        var sim = CreateSim();
        var fog = Update(sim, 0);

        fog.RevealAll();

        Assert.That(fog.IsVisible(new Cell(39, 19)), Is.True);
        Assert.That(fog.IsExplored(new Cell(39, 19)), Is.True);
    }

    [Test]
    public void FootprintIsVisibleIfAnyCellIs()
    {
        var sim = CreateSim();
        var fog = Update(sim, 0);

        Assert.That(fog.IsAnyVisible(new Cell(9, 4), 3), Is.True);
        Assert.That(fog.IsAnyVisible(new Cell(12, 4), 3), Is.False);
    }
}

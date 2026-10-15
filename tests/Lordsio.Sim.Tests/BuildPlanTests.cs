using System.Numerics;
using Lordsio.Sim.Combat;
using Lordsio.Sim.Core;
using Lordsio.Sim.Units;
using Lordsio.Sim.World;
using static Lordsio.Sim.Tests.TestSim;

namespace Lordsio.Sim.Tests;

/// <summary>Ordering a building where no troops stand: the nearest free soldier walks over and puts it up.</summary>
public class BuildPlanTests
{
    /// <summary>Faction 0: capital at (1,1), one spearman at (4,8); plenty of gold; the site at (24,2) is far from everyone.</summary>
    private static Simulation CreateSim(params string[] extra)
    {
        var sim = TestMaps.WithCapitals(1, TestMaps.Field(30, 12), new[] { "start 1 1", "unit infantry 0 4 8" }.Concat(extra).ToArray());
        sim.GetFaction(0)!.Treasury.Add(1000);
        return sim;
    }

    private static readonly Cell Far = new(24, 2);

    private static Unit Spearman(Simulation sim) => sim.Units.Single(u => u.Type.Id == "infantry");

    [Test]
    public void TheNearestSoldierWalksOverAndPutsItUp()
    {
        var sim = CreateSim();
        int gold = sim.GetFaction(0)!.Treasury.Gold;
        int cost = sim.Rules.GetBuildingType("barracks").Cost;
        Assert.That(sim.CanBuild(0, "barracks", Far), Is.EqualTo(BuildError.NoUnitsNearby));
        Assert.That(sim.FindBuilder(0, "barracks", Far), Is.SameAs(Spearman(sim)));

        sim.Issue(new BuildCommand(0, "barracks", Far));
        sim.Step();
        Assert.That(sim.GetFaction(0)!.Treasury.Gold, Is.EqualTo(gold - cost).Within(1), "paid at once (a tick of income aside)");
        Assert.That(sim.TakeEvents().OfType<BuildPlanned>().Single().BuilderId, Is.EqualTo(Spearman(sim).Id));
        Assert.That(sim.BuildPlans, Has.Count.EqualTo(1));
        Assert.That(Spearman(sim).State, Is.EqualTo(UnitState.Moving));

        int before = 0;
        for (int i = 0; i < 20 * Simulation.TicksPerSecond && sim.BuildPlans.Count > 0; i++)
        {
            before = sim.GetFaction(0)!.Treasury.Gold;
            sim.Step();
        }

        var site = sim.Buildings.Single(b => b.Type.Id == "barracks");
        Assert.That(site.Anchor, Is.EqualTo(Far));
        Assert.That(site.Invested, Is.EqualTo(cost));
        Assert.That(sim.GetFaction(0)!.Treasury.Gold, Is.GreaterThanOrEqualTo(before), "not paid a second time when laid out");
        Assert.That(Spearman(sim).State, Is.EqualTo(UnitState.Idle), "the builder stops where he laid it out");
        Assert.That(Vector2.Distance(Spearman(sim).Position, site.Position), Is.LessThanOrEqualTo(sim.Rules.BuildRadius + 0.5f));
    }

    [Test]
    public void LosingTheBuilderGivesTheGoldBack()
    {
        var sim = CreateSim();
        int gold = sim.GetFaction(0)!.Treasury.Gold;
        sim.Issue(new BuildCommand(0, "barracks", Far));
        sim.Step();
        CombatSystem.ApplyDamage(Spearman(sim), 999, ranged: false, attackerFactionId: 1);
        sim.Step();

        var cancelled = sim.TakeEvents().OfType<BuildPlanCancelled>().Single();
        Assert.That(cancelled.Refund, Is.EqualTo(sim.Rules.GetBuildingType("barracks").Cost));
        Assert.That(sim.GetFaction(0)!.Treasury.Gold, Is.EqualTo(gold).Within(2), "all of it back (a few ticks of income aside)");
        Assert.That(sim.BuildPlans, Is.Empty);
    }

    [Test]
    public void SendingTheBuilderElsewhereCallsItOff()
    {
        var sim = CreateSim();
        int gold = sim.GetFaction(0)!.Treasury.Gold;
        sim.Issue(new BuildCommand(0, "barracks", Far));
        sim.Step();
        sim.Issue(new MoveCommand(0, new[] { Spearman(sim).Id }, new Vector2(4.5f, 10.5f)));
        sim.Step();

        Assert.That(sim.TakeEvents().OfType<BuildPlanCancelled>(), Has.Exactly(1).Items);
        Assert.That(sim.GetFaction(0)!.Treasury.Gold, Is.EqualTo(gold).Within(2), "all of it back (a few ticks of income aside)");
        Assert.That(sim.BuildPlans, Is.Empty);
    }

    [Test]
    public void APlannedSiteIsTakenAndBusyBuildersAreNotSentTwice()
    {
        var sim = CreateSim();
        sim.Issue(new BuildCommand(0, "barracks", Far));
        sim.Step();
        sim.TakeEvents();

        Assert.That(sim.CanBuild(0, "tower", new Cell(25, 3)), Is.EqualTo(BuildError.Occupied), "over the planned footprint");
        Assert.That(sim.FindBuilder(0, "tower", new Cell(20, 8)), Is.Null, "the only soldier is already on his way");
        sim.Issue(new BuildCommand(0, "tower", new Cell(20, 8)));
        sim.Step();
        Assert.That(sim.TakeEvents().OfType<BuildRejected>().Single().Reason, Is.EqualTo(BuildError.NoUnitsNearby));
    }

    [Test]
    public void SiegeEnginesDoNotBuild()
    {
        var rules = TestSim.RealRules();
        var sim = new Simulation(TestMaps.Parse(TestMaps.Field(30, 12), "start 1 1"), rules, new MatchSetup(1, ShuffleStarts: false));
        foreach (var unit in sim.Units.ToList())
            CombatSystem.ApplyDamage(unit, unit.MaxHealth, ranged: false, attackerFactionId: 1);
        sim.Step();
        sim.SpawnUnit("ram", 0, new Vector2(4.5f, 8.5f));

        Assert.That(sim.FindBuilder(0, "barracks", Far), Is.Null);
    }

    [Test]
    public void PlansAreInTheChecksum()
    {
        var planned = CreateSim();
        var idle = CreateSim();
        planned.Issue(new BuildCommand(0, "barracks", Far));
        planned.Step();
        idle.Step();
        Assert.That(planned.ComputeChecksum(), Is.Not.EqualTo(idle.ComputeChecksum()));
    }
}

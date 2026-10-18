using Warcrown.Sim.Buildings;
using Warcrown.Sim.Core;
using Warcrown.Sim.World;
using static Warcrown.Sim.Tests.TestSim;

namespace Warcrown.Sim.Tests;

/// <summary>Doctrines of the shipped rules: a one-time choice at two barracks that opens its kinds and changes every recruit.</summary>
public class DoctrineTests
{
    private static readonly GameRules Rules = RealRules();

    private static Simulation CreateSim()
    {
        var sim = new Simulation(TestMaps.Parse(TestMaps.Field(40, 16), "start 2 2", "start 34 11"), Rules,
            new MatchSetup(2, ShuffleStarts: false));
        sim.GetFaction(0)!.Treasury.Add(4000);
        return sim;
    }

    private static void BuildBarracks(Simulation sim, params Cell[] anchors)
    {
        foreach (var anchor in anchors)
            sim.Issue(new BuildCommand(0, "barracks", anchor));
        RunSeconds(sim, Rules.GetBuildingType("barracks").BuildTime + 0.5f);
    }

    private static DoctrineError Choose(Simulation sim, string doctrine)
    {
        sim.TakeEvents();
        sim.Issue(new ChooseDoctrineCommand(0, doctrine));
        sim.Step();
        var events = sim.TakeEvents();
        return events.OfType<DoctrineRejected>().SingleOrDefault()?.Reason ?? DoctrineError.None;
    }

    [Test]
    public void ShippedRulesOfferAFastAndAStrongDoctrine()
    {
        var raid = Rules.FindDoctrine("raid")!;
        var fortress = Rules.FindDoctrine("fortress")!;
        Assert.That(Rules.DoctrineBarracks, Is.EqualTo(2));
        Assert.That(raid.SpeedFactor, Is.GreaterThan(1f));
        Assert.That((raid.HealthFactor, raid.DamageFactor, raid.CostFactor), Is.EqualTo((0.85f, 0.85f, 0.8f)), "fast and cheap, but frail");
        Assert.That(fortress.SpeedFactor, Is.LessThan(1f));
        Assert.That(fortress.HealthFactor > 1f && fortress.DamageFactor > 1f && fortress.CostFactor > 1f, "strong but slow and dear");
        Assert.That(Rules.Units.All.Where(t => t.Doctrine == "raid").Select(t => t.Id), Is.EquivalentTo(new[] { "cavalry", "griffin" }));
        Assert.That(Rules.Units.All.Where(t => t.Doctrine == "fortress").Select(t => t.Id),
            Is.EquivalentTo(new[] { "crossbowman", "heavy", "trebuchet", "dragon" }));
    }

    [Test]
    public void ChosenOnceAndOnlyWithEnoughBarracks()
    {
        var sim = CreateSim();
        Assert.That(Choose(sim, "raid"), Is.EqualTo(DoctrineError.NeedsBarracks));
        BuildBarracks(sim, new Cell(6, 2), new Cell(6, 5));
        Assert.That(Choose(sim, "cavalry"), Is.EqualTo(DoctrineError.UnknownDoctrine));
        Assert.That(Choose(sim, "fortress"), Is.EqualTo(DoctrineError.None));
        Assert.That(sim.GetFaction(0)!.Doctrine, Is.EqualTo("fortress"));
        Assert.That(Choose(sim, "raid"), Is.EqualTo(DoctrineError.AlreadyChosen), "for the whole match");
        Assert.That(sim.GetFaction(1)!.Doctrine, Is.Null, "every faction chooses its own");
    }

    [Test]
    public void RecruitsFollowTheDoctrineAndVeteransOfBeforeStayAsTheyWere()
    {
        var sim = CreateSim();
        var castle = CapitalOf(sim, 0);
        var before = sim.Units.First(u => u.FactionId == 0);
        BuildBarracks(sim, new Cell(6, 2), new Cell(6, 5));
        Choose(sim, "raid");

        var infantry = Rules.Units.Get("infantry");
        var raider = sim.UnitTypeFor(0, "infantry");
        Assert.That(raider.Id, Is.EqualTo("infantry"), "the same kind: counters and looks stay");
        Assert.That(raider.Speed, Is.EqualTo(infantry.Speed * 1.2f));
        Assert.That(raider.MaxHealth, Is.EqualTo(60), "70 × 0.85");
        Assert.That(raider.Cost, Is.EqualTo(40), "50 × 0.8");
        Assert.That(sim.UnitTypeFor(1, "infantry"), Is.SameAs(infantry), "the other faction trains them as the rules say");

        int gold = sim.GetFaction(0)!.Treasury.Gold;
        sim.Issue(new RecruitCommand(0, castle.Id, "infantry"));
        sim.Step();
        Assert.That(gold - sim.GetFaction(0)!.Treasury.Gold, Is.InRange(35, 40), "paid the raiders' price (less a tick of income)");
        RunSeconds(sim, raider.BuildTime + 0.2f);
        var recruit = sim.GetUnit(sim.TakeEvents().OfType<UnitRecruited>().Last().UnitId)!;
        Assert.That(recruit.Type, Is.SameAs(raider));
        Assert.That(before.Type, Is.SameAs(infantry), "trained before the choice");
    }

    [Test]
    public void TheChoiceIsPartOfTheChecksum()
    {
        var a = CreateSim();
        var b = CreateSim();
        BuildBarracks(a, new Cell(6, 2), new Cell(6, 5));
        BuildBarracks(b, new Cell(6, 2), new Cell(6, 5));
        a.Issue(new ChooseDoctrineCommand(0, "raid"));
        b.Issue(new ChooseDoctrineCommand(0, "fortress"));
        a.Step();
        b.Step();
        Assert.That(a.ComputeChecksum(), Is.Not.EqualTo(b.ComputeChecksum()));
    }
}

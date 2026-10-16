using System.Numerics;
using Warcrown.Sim.AI;
using Warcrown.Sim.Core;
using Warcrown.Sim.Units;
using static Warcrown.Sim.Tests.TestSim;

namespace Warcrown.Sim.Tests;

/// <summary>Peace and war between factions: offers, answers, the war warning, and what peace means in a fight.</summary>
public class DiplomacyTests
{
    /// <summary>Three factions with capitals far apart, and a spearman of factions 0 and 1 face to face in the middle.</summary>
    private static Simulation CreateSim(IReadOnlyList<int>? teams = null) =>
        new(TestMaps.Parse(TestMaps.Field(32, 14), "start 1 1", "start 27 1", "start 14 9",
                "unit infantry 0 14 4", "unit infantry 1 15 4"),
            TestMaps.Rules, new MatchSetup(3, ShuffleStarts: false, Teams: teams));

    private static void MakePeace(Simulation sim, int a, int b)
    {
        sim.Issue(new ProposePeaceCommand(a, b));
        sim.Step();
        sim.Issue(new AnswerPeaceCommand(b, a, Accept: true));
        sim.Step();
    }

    private static Unit Spearman(Simulation sim, int faction) => sim.Units.Single(u => u.FactionId == faction && u.Type.Id == "infantry");

    [Test]
    public void PeaceTakesAnOfferAndAnAcceptance()
    {
        var sim = CreateSim();
        sim.Issue(new ProposePeaceCommand(0, 1));
        sim.Step();
        Assert.That(sim.StanceBetween(0, 1), Is.EqualTo(Stance.War), "an offer alone changes nothing");
        Assert.That(sim.HasPeaceOffer(0, 1), Is.True);
        Assert.That(sim.TakeEvents().OfType<PeaceOffered>().Single().OtherFactionId, Is.EqualTo(1));

        sim.Issue(new AnswerPeaceCommand(1, 0, Accept: true));
        sim.Step();
        Assert.That(sim.StanceBetween(0, 1), Is.EqualTo(Stance.Peace));
        Assert.That(sim.StanceBetween(1, 0), Is.EqualTo(Stance.Peace), "peace is mutual");
        Assert.That(sim.AreEnemies(0, 1), Is.False);
        Assert.That(sim.AreEnemies(0, 2), Is.True, "the third faction is still at war with both");
        Assert.That(sim.TakeEvents().OfType<PeaceMade>().Single(), Is.EqualTo(new PeaceMade(0, 1)));
    }

    [Test]
    public void AnOfferRunsOutOrCanBeRefused()
    {
        var sim = CreateSim();
        sim.Issue(new ProposePeaceCommand(0, 1));
        RunSeconds(sim, TestMaps.Rules.PeaceOfferSeconds + 0.5f);
        Assert.That(sim.HasPeaceOffer(0, 1), Is.False);
        Assert.That(sim.TakeEvents().OfType<PeaceRefused>().Single(), Is.EqualTo(new PeaceRefused(1, 0, Expired: true)));

        sim.Issue(new ProposePeaceCommand(0, 1));
        sim.Step();
        sim.Issue(new AnswerPeaceCommand(1, 0, Accept: false));
        sim.Step();
        Assert.That(sim.StanceBetween(0, 1), Is.EqualTo(Stance.War));
        Assert.That(sim.TakeEvents().OfType<PeaceRefused>().Single().Expired, Is.False);

        sim.Issue(new AnswerPeaceCommand(1, 0, Accept: true));
        sim.Step();
        Assert.That(sim.StanceBetween(0, 1), Is.EqualTo(Stance.War), "a refused offer cannot be accepted afterwards");
        Assert.That(sim.TakeEvents().OfType<DiplomacyRejected>().Single().Reason, Is.EqualTo(DiplomacyError.NoOffer));
    }

    [Test]
    public void OfferingBackAcceptsAnOfferAlreadyMade()
    {
        var sim = CreateSim();
        sim.Issue(new ProposePeaceCommand(0, 1));
        sim.Step();
        sim.Issue(new ProposePeaceCommand(1, 0));
        sim.Step();
        Assert.That(sim.StanceBetween(0, 1), Is.EqualTo(Stance.Peace));
    }

    [Test]
    public void AtPeaceTroopsDoNotFightNorCanBeOrderedTo()
    {
        var sim = CreateSim();
        MakePeace(sim, 0, 1);
        var mine = Spearman(sim, 0);
        var theirs = Spearman(sim, 1);
        sim.Issue(new AttackCommand(0, new[] { mine.Id }, theirs.Id));
        RunSeconds(sim, 6f);

        Assert.That(mine.Health, Is.EqualTo(mine.MaxHealth));
        Assert.That(theirs.Health, Is.EqualTo(theirs.MaxHealth));
    }

    [Test]
    public void MakingPeaceStopsAFightAndArrowsInFlightFallHarmlessly()
    {
        var sim = CreateSim();
        var mine = Spearman(sim, 0);
        var theirs = Spearman(sim, 1);
        var archer = sim.SpawnUnit("archer", 0, new Vector2(10.5f, 4.5f));
        sim.Issue(new AttackCommand(0, new[] { archer.Id }, theirs.Id));
        for (int i = 0; i < 10 * Simulation.TicksPerSecond && sim.Projectiles.Count == 0; i++)
            sim.Step();
        Assert.That(sim.Projectiles, Is.Not.Empty, "the archer has loosed an arrow");

        MakePeace(sim, 0, 1);
        int health = theirs.Health;
        RunSeconds(sim, 4f);

        Assert.That(theirs.Health, Is.EqualTo(health), "nothing more lands after the treaty");
        Assert.That(mine.Target?.FactionId, Is.Not.EqualTo(1), "no longer fighting the new friend (the third faction is fair game)");
        Assert.That(archer.Target?.FactionId, Is.Not.EqualTo(1));
        Assert.That(archer.Order, Is.Not.EqualTo(UnitOrder.AttackUnit), "the order to attack a friend is dropped");
    }

    [Test]
    public void WarComesOnlyAfterTheWarning()
    {
        var sim = CreateSim();
        MakePeace(sim, 0, 1);
        sim.TakeEvents();
        sim.Issue(new DeclareWarCommand(1, 0));
        sim.Step();
        Assert.That(sim.TakeEvents().OfType<WarDeclared>().Single(), Is.EqualTo(new WarDeclared(1, 0, TestMaps.Rules.WarWarningSeconds)));
        Assert.That(sim.SecondsToWar(0, 1), Is.EqualTo(TestMaps.Rules.WarWarningSeconds).Within(0.1f));

        RunSeconds(sim, TestMaps.Rules.WarWarningSeconds - 1f);
        Assert.That(sim.StanceBetween(0, 1), Is.EqualTo(Stance.Peace), "still at peace during the warning");
        Assert.That(Spearman(sim, 0).Health, Is.EqualTo(Spearman(sim, 0).MaxHealth));

        RunSeconds(sim, 1.5f);
        Assert.That(sim.StanceBetween(0, 1), Is.EqualTo(Stance.War));
        Assert.That(sim.SecondsToWar(0, 1), Is.Null);
        Assert.That(sim.TakeEvents().OfType<WarBegan>().Single(), Is.EqualTo(new WarBegan(1, 0)));
        RunSeconds(sim, 4f);
        Assert.That(Spearman(sim, 0).Health, Is.LessThan(Spearman(sim, 0).MaxHealth), "the neighbours fight again");
    }

    [Test]
    public void AlliesCannotChangeTheirTiesAndWrongMovesAreRejected()
    {
        var sim = CreateSim(teams: new[] { 0, 0, 1 });
        sim.Issue(new DeclareWarCommand(0, 1));
        sim.Issue(new ProposePeaceCommand(0, 0));
        sim.Issue(new DeclareWarCommand(0, 2));
        sim.Step();
        var reasons = sim.TakeEvents().OfType<DiplomacyRejected>().Select(r => r.Reason).ToList();

        Assert.That(reasons, Is.EqualTo(new[] { DiplomacyError.Allies, DiplomacyError.Self, DiplomacyError.AlreadyAtWar }));
        Assert.That(sim.StanceBetween(0, 1), Is.EqualTo(Stance.Ally));
    }

    [Test]
    public void TreatiesAreInTheChecksum()
    {
        var atWar = CreateSim();
        var atPeace = CreateSim();
        atWar.Step();
        atWar.Step();
        MakePeace(atPeace, 0, 2);

        Assert.That(atPeace.Tick, Is.EqualTo(atWar.Tick));
        Assert.That(atPeace.ComputeChecksum(), Is.Not.EqualTo(atWar.ComputeChecksum()));
    }

    [Test]
    public void BotsTurnPeaceDown()
    {
        var sim = CreateSim();
        var bot = new BotBrain(sim, 1, BotDifficulty.Normal, seed: 1);
        sim.Issue(new ProposePeaceCommand(0, 1));
        for (int i = 0; i < 5 * Simulation.TicksPerSecond; i++)
        {
            sim.Step();
            bot.Update();
        }

        Assert.That(sim.StanceBetween(0, 1), Is.EqualTo(Stance.War));
        Assert.That(sim.TakeEvents().OfType<PeaceRefused>().Single(), Is.EqualTo(new PeaceRefused(1, 0, Expired: false)));
    }
}

using Lordsio.Sim.Core;
using Lordsio.Sim.Economy;
using Lordsio.Sim.World;

namespace Lordsio.Sim.Tests;

public class TreasuryTests
{
    [TestCase(7, 1, 7)]
    [TestCase(5, 3, 15)]
    [TestCase(1, 10, 10)]
    [TestCase(33, 2, 66)]
    public void IncomeAccruesExactlyPerSecond(int perSecond, int seconds, int expected)
    {
        var treasury = new Treasury(0);
        for (int i = 0; i < seconds * Simulation.TicksPerSecond; i++)
            treasury.AccrueTick(perSecond, Simulation.TicksPerSecond);

        Assert.That(treasury.Gold, Is.EqualTo(expected));
    }

    [Test]
    public void CannotSpendMoreThanAvailable()
    {
        var treasury = new Treasury(40);

        Assert.That(treasury.TrySpend(50), Is.False);
        Assert.That(treasury.Gold, Is.EqualTo(40));
    }

    [Test]
    public void SpendingExactBalanceLeavesZero()
    {
        var treasury = new Treasury(50);

        Assert.That(treasury.TrySpend(50), Is.True);
        Assert.That(treasury.Gold, Is.Zero);
    }

    [Test]
    public void NegativeAmountsAreRejected()
    {
        var treasury = new Treasury(10);
        Assert.Throws<ArgumentOutOfRangeException>(() => treasury.TrySpend(-5));
        Assert.Throws<ArgumentOutOfRangeException>(() => treasury.Add(-5));
        Assert.That(treasury.Gold, Is.EqualTo(10));
    }
}

public class RecruitmentTests
{
    private static readonly string[] Field =
    {
        "............",
        "............",
        "............",
        "............",
        "............",
        "............",
    };

    private static Simulation CreateSim(params string[] objects) =>
        new(TestMaps.Parse(Field, objects), TestMaps.Rules);

    private static void RunTicks(Simulation sim, int ticks)
    {
        for (int i = 0; i < ticks; i++)
            sim.Step();
    }

    private static int BuildTicks => (int)(TestMaps.Rules.Units.Get("infantry").BuildTime * Simulation.TicksPerSecond);

    [Test]
    public void StartingGoldAndCastleIncome()
    {
        var sim = CreateSim("settlement castle 0 2 2");
        var faction = sim.GetFaction(0)!;
        Assert.That(faction.Treasury.Gold, Is.EqualTo(100));
        Assert.That(faction.IncomePerSecond, Is.EqualTo(10));
        Assert.That(faction.ArmyCap, Is.EqualTo(5));

        RunTicks(sim, 3 * Simulation.TicksPerSecond);

        Assert.That(faction.Treasury.Gold, Is.EqualTo(130));
    }

    [Test]
    public void IncomeAddsUpOverOwnedSettlements()
    {
        var sim = CreateSim("settlement castle 0 2 2", "settlement outpost 0 8 2", "settlement outpost -1 8 4");

        Assert.That(sim.GetFaction(0)!.IncomePerSecond, Is.EqualTo(13));
    }

    [Test]
    public void RecruitSpendsGoldAndSpawnsUnitAfterBuildTime()
    {
        var sim = CreateSim("settlement castle 0 2 2");
        var castle = sim.Settlements[0];
        var faction = sim.GetFaction(0)!;

        sim.Issue(new RecruitCommand(0, castle.Id, "infantry"));
        sim.Step();
        Assert.That(castle.Queue, Has.Count.EqualTo(1));
        Assert.That(faction.Population, Is.EqualTo(1));
        Assert.That(sim.Units, Is.Empty);
        // 100 start - 50 cost + income of the first tick (10 / 20 per tick rounds down to 0).
        Assert.That(faction.Treasury.Gold, Is.EqualTo(50));

        RunTicks(sim, BuildTicks - 1);

        Assert.That(sim.Units, Has.Count.EqualTo(1));
        Assert.That(castle.Queue, Is.Empty);
        var unit = sim.Units[0];
        Assert.That(unit.FactionId, Is.EqualTo(0));
        Assert.That(sim.Map.IsWalkable(Cell.FromPosition(unit.Position)), Is.True);
        Assert.That(System.Numerics.Vector2.Distance(unit.Position, castle.Cell.Center), Is.LessThan(3f));
        Assert.That(sim.TakeEvents().OfType<UnitRecruited>().Single().UnitId, Is.EqualTo(unit.Id));
    }

    [Test]
    public void CannotRecruitWithoutEnoughGold()
    {
        var sim = CreateSim("settlement castle 0 2 2");
        int castle = sim.Settlements[0].Id;

        for (int i = 0; i < 3; i++)
            sim.Issue(new RecruitCommand(0, castle, "infantry"));
        sim.Step();

        Assert.That(sim.Settlements[0].Queue, Has.Count.EqualTo(2));
        Assert.That(sim.GetFaction(0)!.Treasury.Gold, Is.Zero);
        var rejected = sim.TakeEvents().OfType<RecruitRejected>().Single();
        Assert.That(rejected.Reason, Is.EqualTo(RecruitError.NotEnoughGold));
    }

    [Test]
    public void ArmyCapCountsUnitsInProduction()
    {
        // Cap 5: four living units plus one in the queue fill it.
        var sim = CreateSim("settlement castle 0 2 2", "unit infantry 0 8 3 4");
        int castle = sim.Settlements[0].Id;

        sim.Issue(new RecruitCommand(0, castle, "infantry"));
        sim.Issue(new RecruitCommand(0, castle, "infantry"));
        sim.Step();

        Assert.That(sim.Settlements[0].Queue, Has.Count.EqualTo(1));
        Assert.That(sim.GetFaction(0)!.Treasury.Gold, Is.EqualTo(50));
        Assert.That(sim.TakeEvents().OfType<RecruitRejected>().Single().Reason, Is.EqualTo(RecruitError.ArmyCapReached));
        Assert.That(sim.CanRecruit(0, castle, "infantry"), Is.EqualTo(RecruitError.ArmyCapReached));
    }

    [Test]
    public void CannotRecruitInSomeoneElsesSettlement()
    {
        var sim = CreateSim("settlement castle 0 2 2", "settlement castle 1 9 2");
        int enemyCastle = sim.Settlements[1].Id;

        sim.Issue(new RecruitCommand(0, enemyCastle, "infantry"));
        sim.Step();

        Assert.That(sim.Settlements[1].Queue, Is.Empty);
        Assert.That(sim.GetFaction(0)!.Treasury.Gold, Is.EqualTo(100));
        Assert.That(sim.TakeEvents().OfType<RecruitRejected>().Single().Reason, Is.EqualTo(RecruitError.NotOwner));
    }

    [TestCase("outpost", "infantry")]
    [TestCase("castle", "dragon")]
    public void CannotRecruitTypeTheSettlementDoesNotTrain(string settlementType, string unitType)
    {
        var sim = CreateSim($"settlement {settlementType} 0 2 2");

        sim.Issue(new RecruitCommand(0, sim.Settlements[0].Id, unitType));
        sim.Step();

        Assert.That(sim.Settlements[0].Queue, Is.Empty);
        Assert.That(sim.TakeEvents().OfType<RecruitRejected>().Single().Reason, Is.EqualTo(RecruitError.CannotRecruitType));
    }

    [Test]
    public void QueueHasALimit()
    {
        var sim = CreateSim("settlement castle 0 2 2", "settlement castle 0 9 2");
        sim.GetFaction(0)!.Treasury.Add(1000);
        int castle = sim.Settlements[0].Id;

        for (int i = 0; i < Territory.Settlement.MaxQueueLength + 1; i++)
            sim.Issue(new RecruitCommand(0, castle, "infantry"));
        sim.Step();

        Assert.That(sim.Settlements[0].Queue, Has.Count.EqualTo(Territory.Settlement.MaxQueueLength));
        Assert.That(sim.TakeEvents().OfType<RecruitRejected>().Single().Reason, Is.EqualTo(RecruitError.QueueFull));
    }

    [Test]
    public void QueueTrainsOneUnitAtATime()
    {
        var sim = CreateSim("settlement castle 0 2 2");
        int castle = sim.Settlements[0].Id;
        sim.Issue(new RecruitCommand(0, castle, "infantry"));
        sim.Issue(new RecruitCommand(0, castle, "infantry"));

        RunTicks(sim, BuildTicks);
        Assert.That(sim.Units, Has.Count.EqualTo(1));

        RunTicks(sim, BuildTicks);
        Assert.That(sim.Units, Has.Count.EqualTo(2));
        Assert.That(System.Numerics.Vector2.Distance(sim.Units[0].Position, sim.Units[1].Position), Is.GreaterThanOrEqualTo(0.6f - 1e-3f));
    }
}

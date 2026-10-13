using Lordsio.Sim.Core;
using Lordsio.Sim.Units;
using Lordsio.Sim.World;

namespace Lordsio.Sim.Tests;

public class UpkeepAndDepositTests
{
    private const int ReserveSeconds = 10;
    private const int RegenSeconds = 5;

    /// <summary>A unit that only costs upkeep: 120 gold a minute, i.e. 2 a second.</summary>
    private static readonly UnitType Porter = TestMaps.Dummy with { Id = "porter", Name = "Porter", Upkeep = 120 };

    private static readonly GameRules Rules = new(
        startingGold: 100,
        buildRadius: 4f,
        startingUnits: Array.Empty<StartingUnits>(),
        new UnitCatalog(new[] { TestMaps.Infantry, TestMaps.Dummy, Porter }),
        new[] { TestMaps.Castle, TestMaps.Mine, TestMaps.Barracks, TestMaps.Tower },
        depositReserveSeconds: ReserveSeconds,
        depositRegenSeconds: RegenSeconds);

    private static Simulation CreateSim(params string[] extra) =>
        new(TestMaps.Parse(TestMaps.Field(20, 10), new[] { "start 1 1", "gold 8 1 4", "unit dummy 0 8 4" }.Concat(extra).ToArray()),
            Rules, new MatchSetup(1, ShuffleStarts: false));

    private static void RunSeconds(Simulation sim, float seconds)
    {
        for (int i = 0; i < (int)(seconds * Simulation.TicksPerSecond); i++)
            sim.Step();
    }

    /// <summary>Builds the mine and runs until it is complete.</summary>
    private static GoldDeposit BuildMine(Simulation sim)
    {
        sim.Issue(new BuildCommand(0, "mine", new Cell(8, 1)));
        RunSeconds(sim, TestMaps.Mine.BuildTime + 0.05f);
        Assert.That(sim.Buildings.Single(b => b.Type.IsMine).IsComplete, Is.True);
        sim.TakeEvents();
        return sim.Deposits[0];
    }

    [Test]
    public void DepositHoldsYieldTimesReserveSeconds()
    {
        var deposit = CreateSim().Deposits[0];

        Assert.That(deposit.Capacity, Is.EqualTo(4 * ReserveSeconds));
        Assert.That(deposit.Reserve, Is.EqualTo(deposit.Capacity));
        Assert.That(deposit.IsExhausted, Is.False);
    }

    [Test]
    public void DepositIsUntouchedWithoutAWorkingMine()
    {
        var sim = CreateSim();
        RunSeconds(sim, ReserveSeconds * 2);

        Assert.That(sim.Deposits[0].Reserve, Is.EqualTo(sim.Deposits[0].Capacity));
    }

    [Test]
    public void MiningDrainsReserveUntilExhaustedThenIncomeStops()
    {
        var sim = CreateSim();
        var deposit = BuildMine(sim);
        Assert.That(sim.GetFaction(0)!.IncomePerSecond, Is.EqualTo(14));

        RunSeconds(sim, ReserveSeconds / 2f);
        Assert.That(deposit.Reserve, Is.EqualTo(deposit.Capacity / 2).Within(1));

        RunSeconds(sim, ReserveSeconds / 2f + 0.1f);
        Assert.That(deposit.IsExhausted, Is.True);
        Assert.That(deposit.Reserve, Is.Zero);
        Assert.That(sim.GetFaction(0)!.IncomePerSecond, Is.EqualTo(10), "only the castle earns now");
        Assert.That(sim.TakeEvents().OfType<DepositExhausted>().Single(), Is.EqualTo(new DepositExhausted(0, deposit.Id)));
    }

    [Test]
    public void ExhaustedDepositRefillsAfterRegenTimeAndMineResumes()
    {
        var sim = CreateSim();
        var deposit = BuildMine(sim);
        RunSeconds(sim, ReserveSeconds + 0.1f);
        Assert.That(deposit.IsExhausted, Is.True);
        sim.TakeEvents();

        RunSeconds(sim, RegenSeconds - 0.5f);
        Assert.That(deposit.IsExhausted, Is.True);
        Assert.That(deposit.RegenProgress, Is.GreaterThan(0.8f));

        RunSeconds(sim, 0.5f);
        Assert.That(deposit.IsExhausted, Is.False);
        Assert.That(deposit.Reserve, Is.EqualTo(deposit.Capacity));
        Assert.That(sim.GetFaction(0)!.IncomePerSecond, Is.EqualTo(14));
        Assert.That(sim.TakeEvents().OfType<DepositRestored>().Single(), Is.EqualTo(new DepositRestored(0, deposit.Id)));
    }

    [Test]
    public void GoldMinedEqualsReserve()
    {
        var sim = CreateSim();
        var deposit = BuildMine(sim);
        int before = sim.GetFaction(0)!.Treasury.Gold;

        // Exactly the reserve's worth of mining: castle income plus the whole reserve.
        RunSeconds(sim, ReserveSeconds);

        Assert.That(sim.GetFaction(0)!.Treasury.Gold - before, Is.EqualTo(10 * ReserveSeconds + deposit.Capacity).Within(1));
    }

    [Test]
    public void DestroyedMineLeavesTheDepositState()
    {
        var sim = CreateSim();
        var deposit = BuildMine(sim);
        RunSeconds(sim, ReserveSeconds / 2f);
        int reserve = deposit.Reserve;

        Combat.CombatSystem.ApplyDamage(sim.Buildings.Single(b => b.Type.IsMine), 1000, ranged: false, attackerFactionId: 1);
        RunSeconds(sim, 2);

        Assert.That(deposit.MineId, Is.Zero);
        Assert.That(deposit.Reserve, Is.EqualTo(reserve), "a deposit without a mine keeps its reserve");
    }

    [Test]
    public void UpkeepIsPaidFromIncome()
    {
        var sim = CreateSim("unit porter 0 12 6 2");
        var faction = sim.GetFaction(0)!;
        Assert.That(faction.UpkeepPerMinute, Is.EqualTo(240));
        Assert.That(faction.NetPerMinute, Is.EqualTo(10 * 60 - 240));

        RunSeconds(sim, 3);

        Assert.That(faction.Treasury.Gold, Is.EqualTo(100 + 3 * 10 - 3 * 4));
    }

    [Test]
    public void AllowanceCoversASmallArmy()
    {
        var rules = new GameRules(100, 4f, Array.Empty<StartingUnits>(), Rules.Units, Rules.BuildingTypes, upkeepAllowance: 180);
        var sim = new Simulation(TestMaps.Parse(TestMaps.Field(20, 10), "start 1 1", "unit porter 0 12 6 2"),
            rules, new MatchSetup(1, ShuffleStarts: false));
        var faction = sim.GetFaction(0)!;

        Assert.That(faction.ArmyUpkeepPerMinute, Is.EqualTo(240));
        Assert.That(faction.UpkeepPerMinute, Is.EqualTo(60), "only the part above the allowance is paid");

        Combat.CombatSystem.ApplyDamage(sim.Units.First(), 1000, ranged: false, attackerFactionId: 1);
        sim.Step();
        Assert.That(faction.UpkeepPerMinute, Is.Zero);
    }

    [Test]
    public void UpkeepAboveIncomeDrainsGoldToZeroWithoutDebt()
    {
        var sim = CreateSim("unit porter 0 12 6 10"); // 20 gold/s upkeep against 10 income: −10/s
        var faction = sim.GetFaction(0)!;

        RunSeconds(sim, 10);
        Assert.That(faction.Treasury.Gold, Is.EqualTo(0));
        RunSeconds(sim, 10);
        Assert.That(faction.Treasury.Gold, Is.EqualTo(0));

        // The army is gone: income flows again from zero, with no debt to repay first.
        foreach (var porter in sim.Units.Where(u => u.Type == Porter).ToList())
            Combat.CombatSystem.ApplyDamage(porter, 1000, ranged: false, attackerFactionId: 1);
        RunSeconds(sim, 2.05f);
        Assert.That(faction.UpkeepPerMinute, Is.Zero);
        Assert.That(faction.Treasury.Gold, Is.EqualTo(20));
    }

    [Test]
    public void DepositsAreFingerprintedForLockstep()
    {
        var a = CreateSim();
        var b = CreateSim();
        BuildMine(a);
        BuildMine(b);
        Assert.That(a.ComputeChecksum(), Is.EqualTo(b.ComputeChecksum()));

        for (int i = 0; i < Simulation.TicksPerSecond; i++)
            a.Deposits[0].Mine(Simulation.TicksPerSecond); // one second of mining on one side only
        Assert.That(a.ComputeChecksum(), Is.Not.EqualTo(b.ComputeChecksum()));
    }
}

public class TreasuryAccrualTests
{
    [Test]
    public void NegativeRateDrainsExactlyPerPeriod()
    {
        var treasury = new Economy.Treasury(100);
        for (int i = 0; i < 2 * 1200; i++)
            treasury.AccrueTick(-30, 1200);

        Assert.That(treasury.Gold, Is.EqualTo(40));
    }

    [Test]
    public void GoldNeverGoesNegative()
    {
        var treasury = new Economy.Treasury(5);
        for (int i = 0; i < 100; i++)
            treasury.AccrueTick(-10, 20);

        Assert.That(treasury.Gold, Is.Zero);
    }
}

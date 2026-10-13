using System.Numerics;
using Lordsio.Sim.Buildings;
using Lordsio.Sim.Core;
using Lordsio.Sim.World;

namespace Lordsio.Sim.Tests;

public class BuildingPlacementTests
{
    private static readonly string[] Field =
    {
        "....................",
        "....................",
        "....................",
        "....................",
        "......~~............",
        "......~~....==......",
        "............==......",
        "....................",
        "....................",
        "....................",
    };

    /// <summary>Faction 0: capital at (1,1), one infantry at (11,2); deposits at (14,1) and (3,7).</summary>
    private static Simulation CreateSim() =>
        TestMaps.WithCapitals(1, Field, "start 1 1", "gold 14 1", "gold 3 7", "unit infantry 0 11 2");

    [Test]
    public void ValidPlacementNearOwnUnit()
    {
        var sim = CreateSim();
        Assert.That(sim.CanBuild(0, "barracks", new Cell(9, 4)), Is.EqualTo(BuildError.None));
        Assert.That(sim.CanBuild(0, "tower", new Cell(10, 0)), Is.EqualTo(BuildError.None));
    }

    [TestCase("castle", 9, 4, BuildError.NotBuildable)]
    [TestCase("palace", 9, 4, BuildError.UnknownType)]
    [TestCase("barracks", 19, 4, BuildError.OutOfBounds)]
    [TestCase("barracks", 6, 3, BuildError.BlockedTerrain)]
    [TestCase("barracks", 11, 5, BuildError.BlockedTerrain)]
    [TestCase("barracks", 2, 2, BuildError.Occupied)]
    [TestCase("tower", 14, 1, BuildError.OnDeposit)]
    [TestCase("mine", 9, 4, BuildError.NeedsDeposit)]
    [TestCase("mine", 13, 1, BuildError.NeedsDeposit)]
    [TestCase("barracks", 17, 8, BuildError.NoUnitsNearby)]
    public void InvalidPlacements(string type, int x, int y, BuildError expected)
    {
        Assert.That(CreateSim().CanBuild(0, type, new Cell(x, y)), Is.EqualTo(expected));
    }

    [Test]
    public void MineGoesExactlyOnADeposit()
    {
        var sim = CreateSim();
        Assert.That(sim.CanBuild(0, "mine", new Cell(14, 1)), Is.EqualTo(BuildError.None));
    }

    [Test]
    public void CannotBuildWithoutGold()
    {
        var sim = CreateSim();
        sim.GetFaction(0)!.Treasury.TrySpend(sim.GetFaction(0)!.Treasury.Gold - 10);

        Assert.That(sim.CanBuild(0, "tower", new Cell(10, 0)), Is.EqualTo(BuildError.NotEnoughGold));
    }

    [Test]
    public void BuildCommandPaysAndPlacesSolidSite()
    {
        var sim = CreateSim();
        sim.Issue(new BuildCommand(0, "tower", new Cell(10, 0)));
        sim.Step();

        var tower = sim.Buildings.Single(b => b.Type.Id == "tower");
        Assert.That(tower.IsComplete, Is.False);
        Assert.That(tower.Health, Is.LessThan(tower.MaxHealth / 5), "a site starts nearly defenceless");
        Assert.That(sim.Map.IsWalkable(new Cell(10, 0)), Is.False, "buildings are solid");
        // 100 - 60 cost + the first tick's share of 10 g/s income (rounds down to 0).
        Assert.That(sim.GetFaction(0)!.Treasury.Gold, Is.EqualTo(40));
        Assert.That(sim.TakeEvents().OfType<BuildingPlaced>().Single().BuildingId, Is.EqualTo(tower.Id));
    }

    [Test]
    public void RejectedBuildKeepsGold()
    {
        var sim = CreateSim();
        sim.Issue(new BuildCommand(0, "barracks", new Cell(17, 8)));
        sim.Step();

        Assert.That(sim.Buildings.Count, Is.EqualTo(1), "only the capital");
        Assert.That(sim.GetFaction(0)!.Treasury.Gold, Is.EqualTo(100));
        Assert.That(sim.TakeEvents().OfType<BuildRejected>().Single().Reason, Is.EqualTo(BuildError.NoUnitsNearby));
    }

    [Test]
    public void BuildingLimitCountsSitesAndFreesUpWhenOneFalls()
    {
        var rules = new GameRules(100, 4f, Array.Empty<StartingUnits>(), TestMaps.Rules.Units,
            new[] { TestMaps.Castle, TestMaps.Mine, TestMaps.Barracks, TestMaps.Tower with { MaxCount = 2 } });
        var sim = new Simulation(TestMaps.Parse(Field, "start 1 1", "gold 14 1", "gold 3 7", "unit infantry 0 11 2"),
            rules, new MatchSetup(1, ShuffleStarts: false));
        sim.GetFaction(0)!.Treasury.Add(500);

        // Both orders in one tick: the second site already counts against the first.
        sim.Issue(new BuildCommand(0, "tower", new Cell(10, 0)));
        sim.Issue(new BuildCommand(0, "tower", new Cell(12, 0)));
        sim.Issue(new BuildCommand(0, "tower", new Cell(10, 3)));
        sim.Step();

        Assert.That(sim.CountBuildings(0, "tower"), Is.EqualTo(2));
        Assert.That(sim.TakeEvents().OfType<BuildRejected>().Single().Reason, Is.EqualTo(BuildError.LimitReached));
        Assert.That(sim.CanBuild(0, "tower", new Cell(10, 3)), Is.EqualTo(BuildError.LimitReached));

        for (int i = 0; i < (TestMaps.Tower.BuildTime + 0.2f) * Simulation.TicksPerSecond; i++)
            sim.Step();
        sim.Buildings.First(b => b.Type.Id == "tower").Health = 0;
        sim.Step();
        Assert.That(sim.CountBuildings(0, "tower"), Is.EqualTo(1));
        Assert.That(sim.CanBuild(0, "tower", new Cell(10, 3)), Is.EqualTo(BuildError.None));
    }

    [Test]
    public void TowersAreLimitedToTwentyPerPlayer()
    {
        var rules = GameRules.Parse(File.ReadAllText(Path.Combine(TestMaps.ProjectRoot(), "Data", "rules.json")));
        Assert.That(rules.GetBuildingType("tower").MaxCount, Is.EqualTo(20));
        Assert.That(rules.GetBuildingType("barracks").MaxCount, Is.EqualTo(0), "other buildings are unlimited");
    }

    [Test]
    public void UnitsStandingOnTheSiteStepAside()
    {
        var sim = CreateSim();
        var unit = sim.Units.Single();
        sim.Issue(new BuildCommand(0, "barracks", Cell.FromPosition(unit.Position)));
        sim.Step();

        Assert.That(sim.Buildings.Count, Is.EqualTo(2));
        Assert.That(sim.Map.IsWalkable(Cell.FromPosition(unit.Position)), Is.True);
    }

    [Test]
    public void UnitsWalkAroundBuildings()
    {
        // A 2×2 barracks blocks the straight line; the march must go around it.
        var sim = CreateSim();
        var unit = sim.Units.Single();
        sim.Issue(new BuildCommand(0, "barracks", new Cell(12, 1)));
        sim.Step();
        sim.Issue(new MoveCommand(0, new[] { unit.Id }, new Vector2(17.5f, 2.5f)));

        for (int i = 0; i < 6 * Simulation.TicksPerSecond; i++)
        {
            sim.Step();
            Assert.That(sim.Map.IsWalkable(Cell.FromPosition(unit.Position)), Is.True, $"tick {sim.Tick}: inside a building");
        }
        Assert.That(Vector2.Distance(unit.Position, new Vector2(17.5f, 2.5f)), Is.LessThan(0.01f));
    }
}

public class ConstructionAndEconomyTests
{
    private static readonly string[] Field = TestMaps.Field(20, 10);

    private static Simulation CreateSim() =>
        TestMaps.WithCapitals(1, Field, "start 1 1", "gold 8 1 4", "unit dummy 0 8 4");

    private static void RunTicks(Simulation sim, int ticks)
    {
        for (int i = 0; i < ticks; i++)
            sim.Step();
    }

    private static int Ticks(float seconds) => (int)(seconds * Simulation.TicksPerSecond);

    [Test]
    public void CapitalProvidesBaseIncomeAndCap()
    {
        var sim = CreateSim();
        var faction = sim.GetFaction(0)!;
        Assert.That(faction.IncomePerSecond, Is.EqualTo(10));
        Assert.That(faction.ArmyCap, Is.EqualTo(5));

        RunTicks(sim, Ticks(3));

        Assert.That(faction.Treasury.Gold, Is.EqualTo(130));
    }

    [Test]
    public void MineEarnsDepositYieldOnlyWhenComplete()
    {
        var sim = CreateSim();
        sim.Issue(new BuildCommand(0, "mine", new Cell(8, 1)));
        sim.Step();
        var mine = sim.Buildings.Single(b => b.Type.IsMine);
        Assert.That(sim.GetFaction(0)!.IncomePerSecond, Is.EqualTo(10), "a site earns nothing");
        Assert.That(sim.Deposits[0].MineId, Is.EqualTo(mine.Id));

        RunTicks(sim, Ticks(TestMaps.Mine.BuildTime));

        Assert.That(mine.IsComplete, Is.True);
        Assert.That(mine.Health, Is.EqualTo(mine.MaxHealth), "construction restores full health");
        Assert.That(sim.GetFaction(0)!.IncomePerSecond, Is.EqualTo(14));
        Assert.That(sim.TakeEvents().OfType<BuildingCompleted>().Single().BuildingId, Is.EqualTo(mine.Id));
    }

    [Test]
    public void DamageDuringConstructionIsKept()
    {
        var sim = CreateSim();
        sim.Issue(new BuildCommand(0, "mine", new Cell(8, 1)));
        sim.Step();
        var mine = sim.Buildings.Single(b => b.Type.IsMine);
        Combat.CombatSystem.ApplyDamage(mine, 5, ranged: false, attackerFactionId: 1);

        RunTicks(sim, Ticks(TestMaps.Mine.BuildTime));

        Assert.That(mine.Health, Is.EqualTo(mine.MaxHealth - 5));
    }

    [Test]
    public void DestroyedMineStopsIncomeAndFreesDeposit()
    {
        var sim = CreateSim();
        sim.Issue(new BuildCommand(0, "mine", new Cell(8, 1)));
        RunTicks(sim, Ticks(TestMaps.Mine.BuildTime) + 1);
        var mine = sim.Buildings.Single(b => b.Type.IsMine);

        Combat.CombatSystem.ApplyDamage(mine, 1000, ranged: false, attackerFactionId: 1);
        sim.Step();

        Assert.That(sim.GetBuilding(mine.Id), Is.Null);
        Assert.That(sim.GetFaction(0)!.IncomePerSecond, Is.EqualTo(10));
        Assert.That(sim.Deposits[0].MineId, Is.Zero);
        Assert.That(sim.Map.IsWalkable(new Cell(8, 1)), Is.True, "the footprint is free again");
        var destroyed = sim.TakeEvents().OfType<BuildingDestroyed>().Single();
        Assert.That((destroyed.BuildingId, destroyed.KillerFactionId), Is.EqualTo((mine.Id, 1)));
        Assert.That(sim.CanBuild(0, "mine", new Cell(8, 1)), Is.EqualTo(BuildError.None), "the deposit can be mined again");
    }

    [Test]
    public void BarracksRaisesCapAndTrainsUnits()
    {
        var sim = CreateSim();
        sim.GetFaction(0)!.Treasury.Add(200);
        sim.Issue(new BuildCommand(0, "barracks", new Cell(10, 5)));
        sim.Step();
        var barracks = sim.Buildings.Single(b => b.Type.Id == "barracks");
        Assert.That(sim.CanRecruit(0, barracks.Id, "infantry"), Is.EqualTo(RecruitError.NotComplete));

        RunTicks(sim, Ticks(TestMaps.Barracks.BuildTime));
        Assert.That(sim.GetFaction(0)!.ArmyCap, Is.EqualTo(10));

        sim.Issue(new RecruitCommand(0, barracks.Id, "infantry"));
        RunTicks(sim, Ticks(TestMaps.Infantry.BuildTime) + 1);

        var recruit = sim.Units.Single(u => u.Type.Id == "infantry");
        Assert.That(recruit.FactionId, Is.EqualTo(0));
        Assert.That(barracks.SurfaceDistance(recruit.Position), Is.LessThan(2f), "trained units appear by their building");
    }
}

public class RecruitmentTests
{
    private static Simulation CreateSim(params string[] extra) =>
        TestMaps.WithCapitals(2, TestMaps.Field(20, 10), new[] { "start 1 1", "start 16 6" }.Concat(extra).ToArray());

    private static int Capital(Simulation sim, int faction) => sim.GetFaction(faction)!.CapitalId;

    [Test]
    public void RecruitSpendsGoldAndSpawnsAfterTrainingTime()
    {
        var sim = CreateSim();
        sim.Issue(new RecruitCommand(0, Capital(sim, 0), "infantry"));
        sim.Step();
        Assert.That(sim.GetFaction(0)!.Treasury.Gold, Is.EqualTo(50));
        Assert.That(sim.GetFaction(0)!.Population, Is.EqualTo(1));

        for (int i = 1; i < TestMaps.Infantry.BuildTime * Simulation.TicksPerSecond; i++)
            sim.Step();

        Assert.That(sim.Units.Count(u => u.FactionId == 0), Is.EqualTo(1));
        Assert.That(sim.TakeEvents().OfType<UnitRecruited>().Single().FactionId, Is.EqualTo(0));
    }

    [Test]
    public void CannotRecruitWithoutGold()
    {
        var sim = CreateSim();
        for (int i = 0; i < 3; i++)
            sim.Issue(new RecruitCommand(0, Capital(sim, 0), "infantry"));
        sim.Step();

        Assert.That(sim.GetBuilding(Capital(sim, 0))!.Queue, Has.Count.EqualTo(2));
        Assert.That(sim.GetFaction(0)!.Treasury.Gold, Is.Zero);
        Assert.That(sim.TakeEvents().OfType<RecruitRejected>().Single().Reason, Is.EqualTo(RecruitError.NotEnoughGold));
    }

    [Test]
    public void ArmyCapCountsUnitsInTraining()
    {
        var sim = CreateSim("unit dummy 0 8 4 4");
        sim.Issue(new RecruitCommand(0, Capital(sim, 0), "infantry"));
        sim.Issue(new RecruitCommand(0, Capital(sim, 0), "infantry"));
        sim.Step();

        Assert.That(sim.GetBuilding(Capital(sim, 0))!.Queue, Has.Count.EqualTo(1));
        Assert.That(sim.TakeEvents().OfType<RecruitRejected>().Single().Reason, Is.EqualTo(RecruitError.ArmyCapReached));
    }

    [Test]
    public void CannotRecruitInEnemyBuilding()
    {
        var sim = CreateSim();
        sim.Issue(new RecruitCommand(0, Capital(sim, 1), "infantry"));
        sim.Step();

        Assert.That(sim.GetFaction(0)!.Treasury.Gold, Is.EqualTo(100));
        Assert.That(sim.TakeEvents().OfType<RecruitRejected>().Single().Reason, Is.EqualTo(RecruitError.NotOwner));
    }

    [Test]
    public void QueueHasALimit()
    {
        var sim = CreateSim();
        sim.GetFaction(0)!.Treasury.Add(1000);
        // Raise the cap above the queue limit so only the queue can refuse.
        sim.SpawnUnit("dummy", 0, new Vector2(8.5f, 4.5f));
        sim.Issue(new BuildCommand(0, "barracks", new Cell(8, 6)));
        for (int i = 0; i < 3 * Simulation.TicksPerSecond; i++)
            sim.Step();
        sim.TakeEvents();

        for (int i = 0; i < Building.MaxQueueLength + 1; i++)
            sim.Issue(new RecruitCommand(0, Capital(sim, 0), "infantry"));
        sim.Step();

        Assert.That(sim.GetBuilding(Capital(sim, 0))!.Queue, Has.Count.EqualTo(Building.MaxQueueLength));
        Assert.That(sim.TakeEvents().OfType<RecruitRejected>().Single().Reason, Is.EqualTo(RecruitError.QueueFull));
    }
}

public class TreasuryTests
{
    [TestCase(7, 1, 7)]
    [TestCase(5, 3, 15)]
    [TestCase(33, 2, 66)]
    public void IncomeAccruesExactlyPerSecond(int perSecond, int seconds, int expected)
    {
        var treasury = new Economy.Treasury(0);
        for (int i = 0; i < seconds * Simulation.TicksPerSecond; i++)
            treasury.AccrueTick(perSecond, Simulation.TicksPerSecond);

        Assert.That(treasury.Gold, Is.EqualTo(expected));
    }

    [Test]
    public void CannotSpendMoreThanAvailable()
    {
        var treasury = new Economy.Treasury(40);

        Assert.That(treasury.TrySpend(50), Is.False);
        Assert.That(treasury.Gold, Is.EqualTo(40));
        Assert.That(treasury.TrySpend(40), Is.True);
        Assert.That(treasury.Gold, Is.Zero);
    }

    [Test]
    public void NegativeAmountsAreRejected()
    {
        var treasury = new Economy.Treasury(10);
        Assert.Throws<ArgumentOutOfRangeException>(() => treasury.TrySpend(-5));
        Assert.Throws<ArgumentOutOfRangeException>(() => treasury.Add(-5));
    }
}

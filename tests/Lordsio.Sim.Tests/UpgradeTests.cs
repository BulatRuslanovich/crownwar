using Lordsio.Sim.Buildings;
using Lordsio.Sim.Core;
using Lordsio.Sim.Net;
using Lordsio.Sim.World;

namespace Lordsio.Sim.Tests;

/// <summary>Building levels: archer towers and mines can be upgraded twice, one building at a time.</summary>
public class UpgradeTests
{
    private const float UpgradeSeconds = 2f;

    private static readonly BuildingType Tower3 = TestMaps.Tower with
    {
        Tier = 3, MaxHealth = 300, Attack = TestMaps.Tower.Attack! with { Damage = 30, Range = 5f },
    };
    private static readonly BuildingType Tower2 = TestMaps.Tower with
    {
        Tier = 2, MaxHealth = 200, Attack = TestMaps.Tower.Attack! with { Damage = 20 },
        NextTier = Tower3, UpgradeCost = 80, UpgradeTime = UpgradeSeconds,
    };
    private static readonly BuildingType Tower = TestMaps.Tower with { NextTier = Tower2, UpgradeCost = 50, UpgradeTime = UpgradeSeconds };

    private static readonly BuildingType Mine3 = TestMaps.Mine with { Tier = 3, MaxHealth = 300, YieldBonus = 4 };
    private static readonly BuildingType Mine2 = TestMaps.Mine with
    {
        Tier = 2, MaxHealth = 200, YieldBonus = 2, NextTier = Mine3, UpgradeCost = 80, UpgradeTime = UpgradeSeconds,
    };
    private static readonly BuildingType Mine = TestMaps.Mine with { NextTier = Mine2, UpgradeCost = 50, UpgradeTime = UpgradeSeconds };

    private static readonly GameRules Rules = new(
        startingGold: 1000,
        buildRadius: 6f,
        startingUnits: Array.Empty<StartingUnits>(),
        TestMaps.Rules.Units,
        new[] { TestMaps.Castle, Mine, TestMaps.Barracks, Tower },
        depositReserveSeconds: 100f,
        depositRegenSeconds: 10f);

    /// <summary>Faction 0 (capital at 1,1) with troops at 8,5 and a deposit at 8,1; faction 1 far east.</summary>
    private static Simulation CreateSim() =>
        new(TestMaps.Parse(TestMaps.Field(30, 10), "start 1 1", "start 26 6", "gold 8 1", "unit infantry 0 8 5 2"),
            Rules, new MatchSetup(2, ShuffleStarts: false));

    private static void RunSeconds(Simulation sim, float seconds)
    {
        for (int i = 0; i < (int)MathF.Round(seconds * Simulation.TicksPerSecond); i++)
            sim.Step();
    }

    private static Building Build(Simulation sim, string typeId, int x, int y)
    {
        sim.Issue(new BuildCommand(0, typeId, new Cell(x, y)));
        sim.Step();
        var building = sim.Buildings.Single(b => b.Type.Id == typeId && b.FactionId == 0);
        RunSeconds(sim, Rules.GetBuildingType(typeId).BuildTime + 0.1f);
        Assert.That(building.IsComplete, Is.True);
        sim.TakeEvents();
        return building;
    }

    private static void Upgrade(Simulation sim, Building building)
    {
        sim.Issue(new UpgradeCommand(0, building.Id));
        RunSeconds(sim, UpgradeSeconds + 0.1f);
    }

    [Test]
    public void UpgradeCostsGoldTakesTimeAndRaisesTheLevel()
    {
        var sim = CreateSim();
        var tower = Build(sim, "tower", 11, 5);
        var treasury = sim.GetFaction(0)!.Treasury;
        int gold = treasury.Gold;

        sim.Issue(new UpgradeCommand(0, tower.Id));
        sim.Step();
        Assert.That(gold - treasury.Gold, Is.EqualTo(50).Within(1), "paid up front (plus a tick of income)");
        Assert.That(tower.IsUpgrading, Is.True);
        Assert.That(sim.TakeEvents().OfType<BuildingUpgradeStarted>().Single().BuildingId, Is.EqualTo(tower.Id));

        RunSeconds(sim, UpgradeSeconds - 0.5f);
        Assert.That(tower.Type.Tier, Is.EqualTo(1), "still the old level while the work goes on");
        Assert.That(tower.UpgradeProgress, Is.InRange(0.5f, 1f));

        RunSeconds(sim, 0.6f);
        Assert.That(tower.Type.Tier, Is.EqualTo(2));
        Assert.That(tower.IsUpgrading, Is.False);
        Assert.That((tower.MaxHealth, tower.Type.Attack!.Damage), Is.EqualTo((200, 20)));
        var done = sim.TakeEvents().OfType<BuildingUpgraded>().Single();
        Assert.That((done.BuildingId, done.Tier), Is.EqualTo((tower.Id, 2)));
    }

    [Test]
    public void DamageTakenIsKeptAndTheNewHealthIsAddedOnTop()
    {
        var sim = CreateSim();
        var tower = Build(sim, "tower", 11, 5);
        tower.Health -= 40;

        Upgrade(sim, tower);

        Assert.That(tower.Health, Is.EqualTo(TestMaps.Tower.MaxHealth - 40 + (200 - TestMaps.Tower.MaxHealth)));
    }

    [Test]
    public void MineLevelsAddGoldPerSecondWithoutDrainingTheDepositFaster()
    {
        var sim = CreateSim();
        var mine = Build(sim, "mine", 8, 1);
        var deposit = mine.Deposit!;
        Assert.That(mine.Income, Is.EqualTo(deposit.Yield));

        Upgrade(sim, mine);
        Assert.That(mine.Income, Is.EqualTo(deposit.Yield + 2));
        Upgrade(sim, mine);
        Assert.That(mine.Income, Is.EqualTo(deposit.Yield + 4));
        sim.Step();
        Assert.That(sim.GetFaction(0)!.IncomePerSecond, Is.EqualTo(TestMaps.Castle.Income + deposit.Yield + 4));

        int reserve = deposit.Reserve;
        RunSeconds(sim, 10f);
        Assert.That(reserve - deposit.Reserve, Is.EqualTo(10 * deposit.Yield).Within(deposit.Yield), "only the deposit's own yield drains it");
    }

    [Test]
    public void TwoLevelsAtMostThenTheBuildingIsMaxed()
    {
        var sim = CreateSim();
        var tower = Build(sim, "tower", 11, 5);

        Upgrade(sim, tower);
        Upgrade(sim, tower);

        Assert.That(tower.Type.Tier, Is.EqualTo(3));
        Assert.That(tower.Type.Attack!.Range, Is.EqualTo(5f));
        Assert.That(tower.Type.MaxTier, Is.EqualTo(3));
        Assert.That(sim.CanUpgrade(0, tower.Id), Is.EqualTo(UpgradeError.MaxTier));
    }

    [Test]
    public void InvalidUpgradesAreRejectedAndCostNothing()
    {
        var sim = CreateSim();
        var tower = Build(sim, "tower", 11, 5);
        var barracks = Build(sim, "barracks", 11, 7);
        var enemyCastle = sim.GetBuilding(sim.GetFaction(1)!.CapitalId)!;

        Assert.That(sim.CanUpgrade(0, barracks.Id), Is.EqualTo(UpgradeError.MaxTier), "only towers and mines have levels");
        Assert.That(sim.CanUpgrade(0, sim.GetFaction(0)!.CapitalId), Is.EqualTo(UpgradeError.MaxTier));
        Assert.That(sim.CanUpgrade(0, enemyCastle.Id), Is.EqualTo(UpgradeError.NotOwner));
        Assert.That(sim.CanUpgrade(0, 9999), Is.EqualTo(UpgradeError.UnknownBuilding));

        sim.Issue(new UpgradeCommand(0, tower.Id));
        sim.Step();
        Assert.That(sim.CanUpgrade(0, tower.Id), Is.EqualTo(UpgradeError.InProgress));

        sim.Issue(new BuildCommand(0, "tower", new Cell(13, 5)));
        sim.Step();
        var site = sim.Buildings.Single(b => b.Type.Id == "tower" && !b.IsComplete);
        Assert.That(sim.CanUpgrade(0, site.Id), Is.EqualTo(UpgradeError.NotComplete));

        var treasury = sim.GetFaction(0)!.Treasury;
        treasury.TrySpend(treasury.Gold);
        RunSeconds(sim, UpgradeSeconds + 0.1f);
        treasury.TrySpend(treasury.Gold);
        Assert.That(sim.CanUpgrade(0, tower.Id), Is.EqualTo(UpgradeError.NotEnoughGold));

        sim.TakeEvents();
        sim.Issue(new UpgradeCommand(0, tower.Id));
        sim.Step();
        Assert.That(sim.TakeEvents().OfType<UpgradeRejected>().Single().Reason, Is.EqualTo(UpgradeError.NotEnoughGold));
        Assert.That(tower.Type.Tier, Is.EqualTo(2));
    }

    [Test]
    public void UpgradesAreFingerprintedForLockstep()
    {
        var a = CreateSim();
        var b = CreateSim();
        var towerA = Build(a, "tower", 11, 5);
        Build(b, "tower", 11, 5);
        a.Issue(new UpgradeCommand(0, towerA.Id));
        a.Step();
        b.Step();

        Assert.That(a.ComputeChecksum(), Is.Not.EqualTo(b.ComputeChecksum()));
    }

    [Test]
    public void UpgradeCommandSurvivesTheNetworkCodec()
    {
        var input = new InputMessage(42, new Command[] { new UpgradeCommand(3, 77) });

        var decoded = (InputMessage)NetCodec.Decode(NetCodec.Encode(input));

        Assert.That(decoded.Commands.Single(), Is.EqualTo(new UpgradeCommand(3, 77)));
    }

    [Test]
    public void TiersInTheRulesFileCarryOverWhatTheyDoNotChange()
    {
        string json = File.ReadAllText(Path.Combine(TestMaps.ProjectRoot(), "Data", "rules.json"));
        var rules = GameRules.Parse(json);

        var tower = rules.GetBuildingType("tower");
        Assert.That(tower.MaxTier, Is.EqualTo(3));
        Assert.That(tower.NextTier!.Attack!.Cooldown, Is.EqualTo(tower.Attack!.Cooldown), "unchanged stats carry over");
        Assert.That(tower.NextTier.Attack.Range, Is.EqualTo(tower.Attack.Range));
        Assert.That(tower.NextTier.NextTier!.Attack!.Range, Is.GreaterThan(tower.Attack.Range));
        Assert.That(tower.NextTier.Id, Is.EqualTo("tower"));

        var mine = rules.GetBuildingType("mine");
        Assert.That(mine.MaxTier, Is.EqualTo(3));
        Assert.That(new[] { mine.YieldBonus, mine.NextTier!.YieldBonus, mine.NextTier.NextTier!.YieldBonus }, Is.Ordered.Ascending);
        Assert.That(new[] { mine.MaxHealth, mine.NextTier.MaxHealth, mine.NextTier.NextTier.MaxHealth }, Is.Ordered.Ascending);
        Assert.That(mine.UpgradeCost, Is.GreaterThan(0));

        foreach (string id in new[] { "castle", "barracks", "watchtower" })
            Assert.That(rules.GetBuildingType(id).MaxTier, Is.EqualTo(1), $"{id} has no levels");
    }

    [Test]
    public void RulesRejectLevelsThatChangeTheFootprint()
    {
        var broken = Tower with { NextTier = Tower2 with { Size = 3 } };
        Assert.Throws<ArgumentException>(() =>
            new GameRules(0, 4f, Array.Empty<StartingUnits>(), Rules.Units, new[] { TestMaps.Castle, broken }));
    }
}

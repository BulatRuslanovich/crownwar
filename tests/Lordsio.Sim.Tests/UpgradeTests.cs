using Lordsio.Sim.Core;
using Lordsio.Sim.Net;
using Lordsio.Sim.Units;
using Lordsio.Sim.World;

namespace Lordsio.Sim.Tests;

public class UpgradeTests
{
    private const float ResearchSeconds = 2f;

    private static readonly UnitType Soldier = TestMaps.Infantry with { Upkeep = 120 };

    private static UpgradeType Upgrade(string id, int cost, params UpgradeEffect[] effects) =>
        new(id, id, cost, ResearchSeconds, effects);

    private static UpgradeEffect Effect(UpgradeStat stat, float amount, params string[] targets) => new(stat, amount, targets);

    private static readonly UpgradeType[] Upgrades =
    {
        Upgrade("blades", 50, Effect(UpgradeStat.Damage, 25, "infantry")),
        Upgrade("mail", 50, Effect(UpgradeStat.Health, 50, "infantry"), Effect(UpgradeStat.Range, 1, "infantry")),
        Upgrade("towers", 50, Effect(UpgradeStat.Damage, 40, "tower"), Effect(UpgradeStat.Health, 50, "tower")),
        Upgrade("barracks", 50, Effect(UpgradeStat.ArmyCap, 5, "barracks")),
        Upgrade("mines", 50, Effect(UpgradeStat.MineYield, 2)),
        Upgrade("supply", 50, Effect(UpgradeStat.Upkeep, -50)),
        new("steel", "steel", 50, ResearchSeconds, new[] { Effect(UpgradeStat.Damage, 25, "infantry") }, Requires: "blades"),
    };

    private static readonly GameRules Rules = new(
        startingGold: 1000,
        buildRadius: 6f,
        startingUnits: Array.Empty<StartingUnits>(),
        new UnitCatalog(new[] { Soldier, TestMaps.Dummy }),
        new[] { TestMaps.Castle, TestMaps.Mine, TestMaps.Barracks, TestMaps.Tower },
        upgrades: Upgrades);

    private static Simulation CreateSim(params string[] extra) =>
        new(TestMaps.Parse(TestMaps.Field(20, 10), new[] { "start 1 1", "gold 8 1", "unit infantry 0 8 5 2" }.Concat(extra).ToArray()),
            Rules, new MatchSetup(1, ShuffleStarts: false));

    private static void RunSeconds(Simulation sim, float seconds)
    {
        for (int i = 0; i < (int)MathF.Round(seconds * Simulation.TicksPerSecond); i++)
            sim.Step();
    }

    private static void Research(Simulation sim, string id)
    {
        sim.Issue(new ResearchCommand(0, id));
        RunSeconds(sim, ResearchSeconds + 0.1f);
        Assert.That(sim.GetFaction(0)!.Technology.Has(id), Is.True, $"{id} should be researched");
    }

    [Test]
    public void ResearchCostsGoldTakesTimeAndAnnouncesItself()
    {
        var sim = CreateSim();
        var faction = sim.GetFaction(0)!;
        int gold = faction.Treasury.Gold;

        sim.Issue(new ResearchCommand(0, "blades"));
        sim.Step();
        Assert.That(faction.Treasury.Gold, Is.EqualTo(gold - 50 + 0).Within(1), "paid up front (plus one tick of income)");
        Assert.That(faction.Technology.Current?.Id, Is.EqualTo("blades"));
        Assert.That(sim.TakeEvents().OfType<ResearchStarted>().Single().UpgradeId, Is.EqualTo("blades"));

        RunSeconds(sim, ResearchSeconds - 0.2f);
        Assert.That(faction.Technology.Has("blades"), Is.False);
        Assert.That(faction.Technology.ResearchProgress, Is.GreaterThan(0.8f));

        RunSeconds(sim, 0.3f);
        Assert.That(faction.Technology.Has("blades"), Is.True);
        Assert.That(faction.Technology.Current, Is.Null);
        Assert.That(sim.TakeEvents().OfType<ResearchCompleted>().Single().UpgradeId, Is.EqualTo("blades"));
    }

    [Test]
    public void UnitUpgradesApplyToExistingAndNewUnits()
    {
        var sim = CreateSim();
        var veteran = sim.Units.First();
        Combat.CombatSystem.ApplyDamage(veteran, 30, ranged: false, attackerFactionId: 1);

        Research(sim, "blades");
        Research(sim, "mail");

        Assert.That(veteran.Type.Damage, Is.EqualTo(Soldier.Damage * 125 / 100));
        Assert.That(veteran.MaxHealth, Is.EqualTo(Soldier.MaxHealth * 150 / 100));
        Assert.That(veteran.Health, Is.EqualTo(Soldier.MaxHealth - 30 + Soldier.MaxHealth / 2), "existing wounds stay, the bonus is added");
        Assert.That(veteran.Type.AttackRange, Is.EqualTo(Soldier.AttackRange + 1f));
        Assert.That(veteran.Type.SightRange, Is.EqualTo(Soldier.SightRange + 1f), "sight keeps covering the range");

        var recruit = sim.SpawnUnit("infantry", 0, new System.Numerics.Vector2(10.5f, 8.5f));
        Assert.That(recruit.Type, Is.EqualTo(veteran.Type));
        Assert.That(recruit.Health, Is.EqualTo(recruit.MaxHealth));
    }

    [Test]
    public void UpgradesDoNotTouchOtherFactions()
    {
        var sim = new Simulation(TestMaps.Parse(TestMaps.Field(20, 10), "start 1 1", "start 16 6", "unit infantry 1 12 8"),
            Rules, new MatchSetup(2, ShuffleStarts: false));
        sim.Issue(new ResearchCommand(0, "blades"));
        RunSeconds(sim, ResearchSeconds + 0.1f);

        Assert.That(sim.Units.Single(u => u.FactionId == 1).Type.Damage, Is.EqualTo(Soldier.Damage));
        Assert.That(sim.GetFaction(1)!.Technology.Unit("infantry").Damage, Is.EqualTo(Soldier.Damage));
    }

    [Test]
    public void PercentagesOfOneStatAddUpOnTheBaseValue()
    {
        var sim = CreateSim();
        Research(sim, "blades");
        Research(sim, "steel");

        Assert.That(sim.Units.First().Type.Damage, Is.EqualTo((int)MathF.Round(Soldier.Damage * 1.5f)));
    }

    [Test]
    public void BuildingUpgradesApplyToTowersAndBarracks()
    {
        var sim = CreateSim();
        sim.Issue(new BuildCommand(0, "tower", new Cell(11, 5)));
        sim.Issue(new BuildCommand(0, "barracks", new Cell(5, 5)));
        RunSeconds(sim, 3);
        var tower = sim.Buildings.Single(b => b.Type.Id == "tower");
        int capBefore = sim.GetFaction(0)!.ArmyCap;

        Research(sim, "towers");
        Research(sim, "barracks");

        Assert.That(tower.Type.Attack!.Damage, Is.EqualTo(TestMaps.Tower.Attack!.Damage * 140 / 100));
        Assert.That(tower.MaxHealth, Is.EqualTo(TestMaps.Tower.MaxHealth * 150 / 100));
        Assert.That(tower.Health, Is.EqualTo(tower.MaxHealth));
        Assert.That(sim.GetFaction(0)!.ArmyCap, Is.EqualTo(capBefore + 5));

        // Buildings placed later come with the upgrades.
        sim.Issue(new BuildCommand(0, "tower", new Cell(13, 5)));
        sim.Step();
        Assert.That(sim.Buildings.Last().Type.Attack!.Damage, Is.EqualTo(tower.Type.Attack.Damage));
    }

    [Test]
    public void MineYieldBonusAddsToEveryWorkingMine()
    {
        var sim = CreateSim();
        sim.Issue(new BuildCommand(0, "mine", new Cell(8, 1)));
        RunSeconds(sim, TestMaps.Mine.BuildTime + 0.1f);
        int income = sim.GetFaction(0)!.IncomePerSecond;

        Research(sim, "mines");

        Assert.That(sim.GetFaction(0)!.IncomePerSecond, Is.EqualTo(income + 2));
    }

    [Test]
    public void UpkeepUpgradeCutsArmyUpkeep()
    {
        var sim = CreateSim();
        Assert.That(sim.GetFaction(0)!.UpkeepPerMinute, Is.EqualTo(240));

        Research(sim, "supply");

        Assert.That(sim.GetFaction(0)!.UpkeepPerMinute, Is.EqualTo(120));
    }

    [TestCase("nope", ResearchError.UnknownUpgrade)]
    [TestCase("steel", ResearchError.MissingRequirement)]
    public void InvalidResearchIsRejected(string id, ResearchError expected)
    {
        var sim = CreateSim();
        Assert.That(sim.CanResearch(0, id), Is.EqualTo(expected));
        sim.Issue(new ResearchCommand(0, id));
        sim.Step();
        Assert.That(sim.TakeEvents().OfType<ResearchRejected>().Single().Reason, Is.EqualTo(expected));
    }

    [Test]
    public void OneResearchAtATimeAndNeverTwice()
    {
        var sim = CreateSim();
        sim.Issue(new ResearchCommand(0, "blades"));
        sim.Step();
        Assert.That(sim.CanResearch(0, "mail"), Is.EqualTo(ResearchError.ResearchInProgress));

        RunSeconds(sim, ResearchSeconds);
        Assert.That(sim.CanResearch(0, "blades"), Is.EqualTo(ResearchError.AlreadyResearched));
        Assert.That(sim.CanResearch(0, "mail"), Is.EqualTo(ResearchError.None));
    }

    [Test]
    public void ResearchNeedsGoldAndACapital()
    {
        var poor = new Simulation(TestMaps.Parse(TestMaps.Field(20, 10), "start 1 1"),
            new GameRules(10, 4f, Array.Empty<StartingUnits>(), Rules.Units, Rules.BuildingTypes, upgrades: Upgrades),
            new MatchSetup(1, ShuffleStarts: false));
        Assert.That(poor.CanResearch(0, "blades"), Is.EqualTo(ResearchError.NotEnoughGold));

        var homeless = new Simulation(TestMaps.Parse(TestMaps.Field(20, 10), "unit infantry 0 5 5"), Rules, MatchSetup.Sandbox);
        Assert.That(homeless.CanResearch(0, "blades"), Is.EqualTo(ResearchError.NoCapital));
    }

    [Test]
    public void ResearchIsFingerprintedForLockstep()
    {
        var a = CreateSim();
        var b = CreateSim();
        a.Issue(new ResearchCommand(0, "blades"));
        a.Step();
        b.Step();

        Assert.That(a.ComputeChecksum(), Is.Not.EqualTo(b.ComputeChecksum()));
    }

    [Test]
    public void ResearchCommandSurvivesTheNetworkCodec()
    {
        var input = new InputMessage(42, new Command[] { new ResearchCommand(3, "deep_mines") });

        var decoded = (InputMessage)NetCodec.Decode(NetCodec.Encode(input));

        Assert.That(decoded.Commands.Single(), Is.EqualTo(new ResearchCommand(3, "deep_mines")));
    }

    [Test]
    public void RulesRejectBadUpgrades()
    {
        GameRules Make(params UpgradeType[] upgrades) =>
            new(0, 4f, Array.Empty<StartingUnits>(), Rules.Units, Rules.BuildingTypes, upgrades: upgrades);

        Assert.Throws<ArgumentException>(() => Make(Upgrade("x", 10, Effect(UpgradeStat.Damage, 10, "dragon"))));
        Assert.Throws<ArgumentException>(() => Make(new UpgradeType("x", "x", 10, 1f, Array.Empty<UpgradeEffect>(), Requires: "y")));
        Assert.Throws<ArgumentException>(() => Make(Upgrade("x", 10), Upgrade("x", 10)));
    }

    [Test]
    public void ShippedRulesHaveUpgrades()
    {
        var rules = GameRules.Parse(File.ReadAllText(Path.Combine(TestMaps.ProjectRoot(), "Data", "rules.json")));

        Assert.That(rules.Upgrades, Has.Count.GreaterThanOrEqualTo(5));
        Assert.That(rules.FindUpgrade("steel_weapons")!.Requires, Is.EqualTo("forged_blades"));
    }
}

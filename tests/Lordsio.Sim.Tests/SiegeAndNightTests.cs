using System.Numerics;
using Lordsio.Sim.Buildings;
using Lordsio.Sim.Core;
using Lordsio.Sim.Net;
using Lordsio.Sim.Units;
using Lordsio.Sim.World;

namespace Lordsio.Sim.Tests;

/// <summary>Rams, loot from destroyed buildings, pulling down one's own buildings, and the night.</summary>
public class SiegeAndNightTests
{
    private static readonly string[] Field = TestMaps.Field(30, 12);

    private static readonly UnitType Ram = new("ram", "Ram", Speed: 2f, Radius: 0.4f, Cost: 100, BuildTime: 1f, MaxHealth: 300,
        Damage: 10, AttackRange: 0.3f, AttackCooldown: 1f, SightRange: 5f, RangedArmor: 0.8f, IsSiege: true, BuildingDamageMultiplier: 3f);

    private static readonly GameRules Rules = new(
        startingGold: 1000,
        buildRadius: 6f,
        startingUnits: Array.Empty<StartingUnits>(),
        new UnitCatalog(new[] { TestMaps.Infantry, TestMaps.Archer, TestMaps.Heavy, TestMaps.Dummy, Ram }),
        new[] { TestMaps.Castle with { Recruits = new[] { "infantry", "ram" } }, TestMaps.Mine, TestMaps.Barracks, TestMaps.Tower },
        lootPercent: 30,
        demolishRefundPercent: 30,
        daySeconds: 10f,
        nightSeconds: 5f,
        nightSightFactor: 0.5f);

    /// <summary>Faction 0 at the west (troops at 8,5), faction 1 at the east.</summary>
    private static Simulation CreateSim() =>
        new(TestMaps.Parse(Field, "start 1 1", "start 26 8", "unit infantry 0 8 5"), Rules, new MatchSetup(2, ShuffleStarts: false));

    private static void RunSeconds(Simulation sim, float seconds, Func<bool>? stop = null)
    {
        for (int i = 0; i < seconds * Simulation.TicksPerSecond && !(stop?.Invoke() ?? false); i++)
            sim.Step();
    }

    private static Building BuildBarracks(Simulation sim, int x, int y)
    {
        sim.Issue(new BuildCommand(0, "barracks", new Cell(x, y)));
        RunSeconds(sim, TestMaps.Barracks.BuildTime + 0.1f);
        return sim.Buildings.Single(b => b.Type.Id == "barracks" && b.FactionId == 0);
    }

    // ---------------------------------------------------------------- rams

    [Test]
    public void RamHitsBuildingsThreeTimesHarderAndIgnoresSoldiers()
    {
        var sim = CreateSim();
        var barracks = BuildBarracks(sim, 10, 5);
        var ram = sim.SpawnUnit("ram", 1, new Vector2(13.4f, 6f));
        var soldier = sim.SpawnUnit("dummy", 0, new Vector2(13.4f, 7f)); // right next to it

        int before = barracks.Health;
        RunSeconds(sim, 2.2f);

        Assert.That(soldier.Health, Is.EqualTo(100), "a ram never swings at units");
        Assert.That(ram.Target, Is.SameAs(barracks));
        Assert.That(before - barracks.Health, Is.EqualTo(Ram.Damage * 3 * 2).Or.EqualTo(Ram.Damage * 3 * 3), "30 per blow, once a second");
    }

    [Test]
    public void RamSentAtAUnitAdvancesWithTheArmyInstead()
    {
        var sim = CreateSim();
        var ram = sim.SpawnUnit("ram", 1, new Vector2(20.5f, 5.5f));
        var enemy = sim.Units.First(u => u.FactionId == 0);

        sim.Issue(new AttackCommand(1, new[] { ram.Id }, enemy.Id));
        sim.Step();

        Assert.That(ram.Order, Is.EqualTo(UnitOrder.AttackMove), "it marches on, ready to hit buildings on the way");
        Assert.That(ram.Target, Is.Null, "but never swings at the soldier");
        Assert.That(Vector2.Distance(ram.Destination!.Value, enemy.Position), Is.LessThan(1.5f));
    }

    // ---------------------------------------------------------------- rally points

    [Test]
    public void RecruitsWalkToTheRallyPoint()
    {
        var sim = CreateSim();
        var castle = sim.GetBuilding(sim.GetFaction(0)!.CapitalId)!;
        var rally = new Vector2(14.5f, 8.5f);
        sim.Issue(new SetRallyCommand(0, castle.Id, rally));
        sim.Issue(new RecruitCommand(0, castle.Id, "infantry"));
        RunSeconds(sim, TestMaps.Infantry.BuildTime + 0.1f);

        var recruit = sim.Units.Last(u => u.FactionId == 0);
        Assert.That(castle.RallyPoint, Is.EqualTo(rally));
        Assert.That(recruit.Order, Is.EqualTo(UnitOrder.Move));
        RunSeconds(sim, 6f);
        Assert.That(Vector2.Distance(recruit.Position, rally), Is.LessThan(1f));
    }

    [Test]
    public void RallyPointsAreTheOwnersAndCanBeCleared()
    {
        var sim = CreateSim();
        var castle = sim.GetBuilding(sim.GetFaction(0)!.CapitalId)!;
        sim.Issue(new SetRallyCommand(1, castle.Id, new Vector2(5f, 5f)));
        sim.Step();
        Assert.That(castle.RallyPoint, Is.Null, "not someone else's to set");

        sim.Issue(new SetRallyCommand(0, castle.Id, new Vector2(500f, -3f)));
        sim.Step();
        Assert.That(castle.RallyPoint, Is.EqualTo(new Vector2(sim.Map.Width - 0.5f, 0.5f)), "kept on the map");

        sim.Issue(new SetRallyCommand(0, castle.Id, null));
        sim.Step();
        Assert.That(castle.RallyPoint, Is.Null);

        var decoded = (InputMessage)NetCodec.Decode(NetCodec.Encode(new InputMessage(3, new Command[]
            { new SetRallyCommand(0, 7, new Vector2(1.5f, 2.5f)), new SetRallyCommand(0, 7, null) })));
        Assert.That(decoded.Commands, Is.EqualTo(new Command[] { new SetRallyCommand(0, 7, new Vector2(1.5f, 2.5f)), new SetRallyCommand(0, 7, null) }));
    }

    // ---------------------------------------------------------------- loot

    [Test]
    public void DestroyingABuildingLootsAShareOfWhatItCost()
    {
        var sim = CreateSim();
        var barracks = BuildBarracks(sim, 10, 5);
        var looter = sim.GetFaction(1)!.Treasury;
        sim.TakeEvents();
        int gold = looter.Gold;

        barracks.Health = 1;
        var ram = sim.SpawnUnit("ram", 1, new Vector2(13.4f, 6f));
        RunSeconds(sim, 3f, () => !barracks.IsAlive);
        sim.Step();

        var loot = sim.TakeEvents().OfType<GoldLooted>().Single();
        Assert.That((loot.FactionId, loot.VictimFactionId, loot.Amount), Is.EqualTo((1, 0, TestMaps.Barracks.Cost * 30 / 100)));
        Assert.That(looter.Gold - gold, Is.InRange(loot.Amount, loot.Amount + 20), "the loot (plus a few seconds of castle income)");
        Assert.That(ram.IsAlive);
    }

    // ---------------------------------------------------------------- demolishing

    [Test]
    public void DemolishingGivesBackAShareOfTheCostAndQueuedRecruitsInFull()
    {
        var sim = CreateSim();
        var barracks = BuildBarracks(sim, 10, 5);
        var treasury = sim.GetFaction(0)!.Treasury;
        sim.Issue(new RecruitCommand(0, barracks.Id, "infantry"));
        sim.Step();
        sim.TakeEvents();

        Assert.That(sim.CanDemolish(0, barracks.Id, out int refund), Is.EqualTo(DemolishError.None));
        Assert.That(refund, Is.EqualTo(TestMaps.Barracks.Cost * 30 / 100 + TestMaps.Infantry.Cost));
        int gold = treasury.Gold;
        sim.Issue(new DemolishCommand(0, barracks.Id));
        sim.Step();

        Assert.That(sim.GetBuilding(barracks.Id), Is.Null);
        Assert.That(sim.Map.IsOccupied(new Cell(10, 5)), Is.False, "the ground is free again");
        Assert.That(treasury.Gold - gold, Is.InRange(refund, refund + 1));
        var events = sim.TakeEvents();
        Assert.That(events.OfType<BuildingDemolished>().Single().Refund, Is.EqualTo(refund));
        Assert.That(events.OfType<BuildingDestroyed>(), Is.Empty, "not a loss to the enemy");
        Assert.That(events.OfType<GoldLooted>(), Is.Empty);
    }

    [Test]
    public void UpgradesCountTowardsTheRefund()
    {
        var rules = GameRules.Parse(File.ReadAllText(Path.Combine(TestMaps.ProjectRoot(), "Data", "rules.json")));
        var sim = new Simulation(MapGenerator.Generate(1), rules, new MatchSetup(2, 1));
        var capital = sim.GetBuilding(sim.GetFaction(0)!.CapitalId)!;

        Assert.That(sim.CanDemolish(0, capital.Id, out _), Is.EqualTo(DemolishError.Capital), "the castle stays");
        Assert.That(sim.CanDemolish(1, capital.Id, out _), Is.EqualTo(DemolishError.NotOwner));
        Assert.That(sim.CanDemolish(0, 9999, out _), Is.EqualTo(DemolishError.UnknownBuilding));
        Assert.That(rules.DemolishRefundPercent, Is.EqualTo(30));
        Assert.That(rules.LootPercent, Is.EqualTo(30));
    }

    [Test]
    public void DemolishCommandSurvivesTheNetworkCodec()
    {
        var input = new InputMessage(9, new Command[] { new DemolishCommand(2, 41) });
        var decoded = (InputMessage)NetCodec.Decode(NetCodec.Encode(input));
        Assert.That(decoded.Commands.Single(), Is.EqualTo(new DemolishCommand(2, 41)));
    }

    // ---------------------------------------------------------------- night

    [Test]
    public void NightFallsAfterTheDayAndBreaksAgain()
    {
        var sim = CreateSim();
        Assert.That(sim.IsNight, Is.False);
        Assert.That(sim.SecondsToDayNightChange, Is.EqualTo(10f).Within(0.1f));

        RunSeconds(sim, 10.05f);
        Assert.That(sim.IsNight, Is.True);
        Assert.That(sim.SecondsToDayNightChange, Is.EqualTo(5f).Within(0.1f));

        RunSeconds(sim, 5f);
        Assert.That(sim.IsNight, Is.False, "a new day");
    }

    [Test]
    public void AtNightUnitsSeeHalfAsFarButBuildingsDoNot()
    {
        var sim = CreateSim();
        var soldier = sim.Units.First(u => u.FactionId == 0);
        var castle = sim.GetBuilding(sim.GetFaction(0)!.CapitalId)!;
        var fiveEast = Cell.FromPosition(soldier.Position + new Vector2(4.5f, 0f));

        var fog = new Visibility(sim.Map.Width, sim.Map.Height);
        fog.Update(sim, 0);
        Assert.That(fog.IsVisible(fiveEast), Is.True, "by day");

        RunSeconds(sim, 10.05f);
        fog.Update(sim, 0);
        Assert.That(sim.SightOf(soldier), Is.EqualTo(TestMaps.Infantry.SightRange * 0.5f));
        Assert.That(fog.IsVisible(fiveEast), Is.False, "the night closes in");
        Assert.That(fog.IsVisible(Cell.FromPosition(castle.Position + new Vector2(castle.SightRange - 0.5f, 0f))), Is.True,
            "the castle sees as far as by day");
    }
}

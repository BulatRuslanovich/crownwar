using System.Numerics;
using Warcrown.Sim.Buildings;
using Warcrown.Sim.Core;
using Warcrown.Sim.Units;
using Warcrown.Sim.World;
using static Warcrown.Sim.Tests.TestSim;

namespace Warcrown.Sim.Tests;

/// <summary>
/// Flyers (griffins, dragons): they cross water, mountains and buildings in a straight line, and only arrows and bolts
/// reach them — archers, crossbowmen, archer towers and castles; swords, rams and catapult stones do not.
/// </summary>
public class FlyingTests
{
    /// <summary>A river (column 10) and a ridge (column 14) that walkers cannot cross.</summary>
    private static readonly string[] Divided = Enumerable.Repeat("..........~...^.....", 10).ToArray();

    /// <summary>A flying target that never fights back.</summary>
    private static readonly UnitType Kite = new("kite", "Kite", Speed: 5f, Radius: 0.35f, Cost: 100, BuildTime: 1f,
        MaxHealth: 100, Damage: 0, AttackRange: 0f, AttackCooldown: 1f, SightRange: 0f, IsFlying: true);

    private static readonly UnitType Griffin = new("griffin", "Griffin", Speed: 5f, Radius: 0.35f, Cost: 100, BuildTime: 1f,
        MaxHealth: 100, Damage: 10, AttackRange: 0.2f, AttackCooldown: 1f, SightRange: 5f, IsFlying: true);

    private static readonly UnitType Dragon = new("dragon", "Dragon", Speed: 3f, Radius: 0.45f, Cost: 300, BuildTime: 1f,
        MaxHealth: 400, Damage: 20, AttackRange: 2f, AttackCooldown: 1f, SightRange: 6f, ProjectileSpeed: 10f,
        SplashRadius: 1.2f, IsFlying: true);

    private static readonly UnitType Bowman = TestMaps.Archer with { HitsAir = true };

    private static readonly BuildingType Tower = TestMaps.Tower with
    {
        Attack = TestMaps.Tower.Attack! with { HitsAir = true },
    };

    private static readonly BuildingType Catapult = new("catapult", "Catapult", Size: 2, Cost: 60, BuildTime: 2f, MaxHealth: 100,
        RangedArmor: 0.5f, Income: 0, ArmyCap: 0, Recruits: Array.Empty<string>(),
        Attack: new BuildingAttack(Damage: 10, Range: 5f, Cooldown: 1f, ProjectileSpeed: 10f, SplashRadius: 1.5f));

    private static readonly GameRules Rules = new(200, 6f, Array.Empty<StartingUnits>(),
        new UnitCatalog(new[] { TestMaps.Infantry, Bowman, TestMaps.Dummy, Kite, Griffin, Dragon }),
        new[] { TestMaps.Castle, TestMaps.Mine, TestMaps.Barracks, Tower, Catapult });

    private static Simulation Sandbox(string[] rows, params string[] objects) =>
        new(TestMaps.Parse(rows, objects), Rules, MatchSetup.Sandbox);

    private static Simulation WithCapitals(int factions, string[] rows, params string[] objects) =>
        new(TestMaps.Parse(rows, objects), Rules, new MatchSetup(factions, ShuffleStarts: false));

    private static void Move(Simulation sim, Unit unit, Vector2 to, MoveMode mode = MoveMode.Move) =>
        sim.Issue(new MoveCommand(unit.FactionId, new[] { unit.Id }, to, mode));

    [Test]
    public void FlyerCrossesRiverAndRidgeInAStraightLineWhileWalkersCannot()
    {
        var sim = Sandbox(Divided);
        var griffin = sim.SpawnUnit("griffin", 0, new Vector2(3.5f, 5.5f));
        var soldier = sim.SpawnUnit("infantry", 0, new Vector2(3.5f, 2.5f));
        var target = new Vector2(17.5f, 5.5f);
        Move(sim, griffin, target);
        Move(sim, soldier, target with { Y = 2.5f });
        sim.Step();
        Assert.That(griffin.Path, Has.Count.EqualTo(1), "one straight leg, no route around");

        RunSeconds(sim, 3.5f); // 14 tiles at 5 tiles/s
        Assert.That(Vector2.Distance(griffin.Position, target), Is.LessThan(0.5f));
        Assert.That(soldier.Position.X, Is.LessThan(10f), "the river stops the soldier");
    }

    [Test]
    public void FlyerHoversOverWaterWhereItWasSent()
    {
        var sim = Sandbox(Divided);
        var griffin = sim.SpawnUnit("griffin", 0, new Vector2(8.5f, 5.5f));
        Move(sim, griffin, new Vector2(10.5f, 5.5f));
        RunSeconds(sim, 2f);
        Assert.That(Cell.FromPosition(griffin.Position), Is.EqualTo(new Cell(10, 5)));
        Assert.That(griffin.Order, Is.EqualTo(UnitOrder.None));
    }

    [Test]
    public void MeleeSoldiersNeitherPickNorHitAFlyer()
    {
        var sim = Sandbox(TestMaps.Field(12, 8));
        var soldier = sim.SpawnUnit("infantry", 0, new Vector2(4.5f, 4.5f));
        var kite = sim.SpawnUnit("kite", 1, new Vector2(5.2f, 4.5f));
        RunSeconds(sim, 3f);
        Assert.That(kite.Health, Is.EqualTo(kite.MaxHealth));
        Assert.That(soldier.Target, Is.Null);
    }

    [Test]
    public void ArchersShootFlyers()
    {
        var sim = Sandbox(TestMaps.Field(12, 8));
        sim.SpawnUnit("archer", 0, new Vector2(2.5f, 4.5f));
        var kite = sim.SpawnUnit("kite", 1, new Vector2(5.5f, 4.5f));
        RunSeconds(sim, 2.5f);
        Assert.That(kite.Health, Is.LessThan(kite.MaxHealth));
    }

    [Test]
    public void ArcherTowerShootsFlyers()
    {
        var sim = WithCapitals(1, TestMaps.Field(24, 12), "start 1 1", "unit dummy 0 6 6");
        sim.Issue(new BuildCommand(0, "tower", new Cell(8, 3)));
        RunSeconds(sim, Tower.BuildTime + 0.2f);
        var kite = sim.SpawnUnit("kite", 1, new Vector2(11.5f, 3.5f));
        RunSeconds(sim, 2f);
        Assert.That(kite.Health, Is.LessThan(kite.MaxHealth));
    }

    [Test]
    public void CatapultStoneSparesFlyersEvenInsideTheBlast()
    {
        var sim = WithCapitals(1, TestMaps.Field(24, 12), "start 1 1", "unit dummy 0 6 6");
        sim.Issue(new BuildCommand(0, "catapult", new Cell(6, 3)));
        RunSeconds(sim, Catapult.BuildTime + 0.2f);
        var kite = sim.SpawnUnit("kite", 1, new Vector2(11.5f, 4.5f)); // nearer, but out of the catapult's reach
        var walker = sim.SpawnUnit("dummy", 1, new Vector2(11.5f, 5.3f));
        RunSeconds(sim, 2f);
        Assert.That(walker.Health, Is.LessThan(walker.MaxHealth), "the stone falls on the walker");
        Assert.That(kite.Health, Is.EqualTo(kite.MaxHealth), "and passes the flyer by");
    }

    [Test]
    public void DragonFireBurnsTheBuildingAndTheGroundAroundButNotFlyers()
    {
        // Faction 1's castle (2×2) at 10,4: its center is 11,5.
        var sim = WithCapitals(2, TestMaps.Field(24, 12), "start 1 1", "start 10 4");
        var castle = CapitalOf(sim, 1);
        var dragon = sim.SpawnUnit("dragon", 0, new Vector2(6.5f, 5f));
        var walker = sim.SpawnUnit("dummy", 1, new Vector2(12.4f, 6.4f));
        var kite = sim.SpawnUnit("kite", 1, new Vector2(9.7f, 6.4f));
        sim.Issue(new AttackCommand(0, new[] { dragon.Id }, castle.Id));
        sim.TakeEvents();
        RunSeconds(sim, 3f);

        var events = sim.TakeEvents();
        Assert.That(events.OfType<ProjectileFired>().Any(e => e.Fire), Is.True);
        Assert.That(events.OfType<SplashLanded>().Any(e => e.Fire), Is.True);
        Assert.That(castle.Health, Is.LessThan(castle.MaxHealth));
        Assert.That(walker.Health, Is.LessThan(walker.MaxHealth));
        Assert.That(kite.Health, Is.EqualTo(kite.MaxHealth));
    }

    [Test]
    public void SoldiersSentAtAFlyerAdvanceOnItWhileArchersAttackIt()
    {
        var sim = Sandbox(TestMaps.Field(20, 8));
        var soldier = sim.SpawnUnit("infantry", 0, new Vector2(2.5f, 4.5f));
        var archer = sim.SpawnUnit("archer", 0, new Vector2(2.5f, 3.5f));
        var kite = sim.SpawnUnit("kite", 1, new Vector2(16.5f, 4.5f));
        sim.Issue(new AttackCommand(0, new[] { soldier.Id, archer.Id }, kite.Id));
        sim.Step();
        Assert.That(soldier.Order, Is.EqualTo(UnitOrder.AttackMove));
        Assert.That(archer.Order, Is.EqualTo(UnitOrder.AttackUnit));
        Assert.That(archer.Target, Is.SameAs(kite));
    }

    [Test]
    public void AirAndGroundDoNotPushEachOther()
    {
        var sim = Sandbox(TestMaps.Field(12, 8));
        var soldier = sim.SpawnUnit("infantry", 0, new Vector2(5.5f, 4.5f));
        var griffin = sim.SpawnUnit("griffin", 0, new Vector2(5.5f, 4.5f));
        RunTicks(sim, 10);
        Assert.That(soldier.Position, Is.EqualTo(new Vector2(5.5f, 4.5f)));
        Assert.That(griffin.Position, Is.EqualTo(new Vector2(5.5f, 4.5f)));
    }

    [Test]
    public void FlyersOfAGroupSpreadOut()
    {
        var sim = Sandbox(Divided);
        var flock = Enumerable.Range(0, 5).Select(i => sim.SpawnUnit("griffin", 0, new Vector2(2.5f, 1.5f + i))).ToList();
        sim.Issue(new MoveCommand(0, flock.Select(g => g.Id).ToList(), new Vector2(10.5f, 5.5f)));
        RunSeconds(sim, 4f);
        for (int i = 0; i < flock.Count; i++)
            for (int j = i + 1; j < flock.Count; j++)
                Assert.That(Vector2.Distance(flock[i].Position, flock[j].Position), Is.GreaterThan(0.5f));
    }

    [Test]
    public void ArcherWalksToTheBankToShootAFlyerOverTheRiver()
    {
        var sim = Sandbox(Divided);
        var archer = sim.SpawnUnit("archer", 0, new Vector2(4.5f, 5.5f));
        var kite = sim.SpawnUnit("kite", 1, new Vector2(11.5f, 5.5f));
        Move(sim, kite, new Vector2(10.5f, 5.5f)); // over the water, 6 tiles off: in sight once the archer looks, not in reach
        RunTicks(sim, 5);
        Move(sim, archer, new Vector2(5.5f, 5.5f));
        RunSeconds(sim, 5f);
        Assert.That(kite.Health, Is.LessThan(kite.MaxHealth));
        Assert.That(archer.Position.X, Is.LessThan(10f), "it stays on its bank");
    }

    [Test]
    public void ShippedRulesLetOnlyArrowsAndBoltsReachFlyers()
    {
        var rules = RealRules();
        var flyers = rules.Units.All.Where(u => u.IsFlying).Select(u => u.Id).Order().ToList();
        Assert.That(flyers, Is.EqualTo(new[] { "dragon", "griffin" }));
        var antiAir = rules.Units.All.Where(u => u.HitsAir).Select(u => u.Id).Order().ToList();
        Assert.That(antiAir, Is.EqualTo(new[] { "archer", "crossbowman" }));
        foreach (var id in new[] { "tower", "castle", "citadel" })
            Assert.That(rules.GetBuildingType(id).Attack!.HitsAir, Is.True, id);
        Assert.That(rules.GetBuildingType("catapult").Attack!.HitsAir, Is.False);
        var tower = rules.GetBuildingType("tower");
        for (var level = tower; level != null; level = level.NextTier)
            Assert.That(level.Attack!.HitsAir, Is.True, $"tower level {level.Tier}");
    }

    [Test]
    public void OnlyAMissileMaySplash()
    {
        var melee = TestMaps.Infantry with { SplashRadius = 1f };
        Assert.Throws<ArgumentException>(() => new UnitCatalog(new[] { melee }));
    }
}

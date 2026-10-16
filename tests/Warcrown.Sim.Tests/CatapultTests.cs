using System.Numerics;
using Warcrown.Sim.Buildings;
using Warcrown.Sim.Core;
using Warcrown.Sim.World;
using static Warcrown.Sim.Tests.TestSim;

namespace Warcrown.Sim.Tests;

/// <summary>The catapult: a defensive building whose stone hurts every enemy unit around the spot it lands on.</summary>
public class CatapultTests
{
    private static readonly string[] Field = TestMaps.Field(24, 12);

    private static readonly BuildingType Catapult = new("catapult", "Catapult", Size: 2, Cost: 60, BuildTime: 2f, MaxHealth: 100,
        RangedArmor: 0.5f, Income: 0, ArmyCap: 0, Recruits: Array.Empty<string>(),
        Attack: new BuildingAttack(Damage: 10, Range: 5f, Cooldown: 1f, ProjectileSpeed: 10f, SplashRadius: 1.5f));

    private static readonly GameRules Rules = new(100, 6f, Array.Empty<StartingUnits>(), TestMaps.Rules.Units,
        new[] { TestMaps.Castle, TestMaps.Mine, TestMaps.Barracks, TestMaps.Tower, Catapult });

    private static Simulation CreateSim() =>
        new(TestMaps.Parse(Field, "start 1 1", "unit dummy 0 6 6"), Rules, new MatchSetup(1, ShuffleStarts: false));

    private static Building BuildCatapult(Simulation sim)
    {
        sim.Issue(new BuildCommand(0, "catapult", new Cell(6, 3)));
        RunSeconds(sim, Catapult.BuildTime + 0.2f);
        return sim.Buildings.Single(b => b.Type.Id == "catapult");
    }

    [Test]
    public void StoneHurtsEveryEnemyAroundTheTargetButSparesFriendsAndTheFarAway()
    {
        var sim = CreateSim();
        BuildCatapult(sim);
        // The catapult (centre 7,4) reaches 5 tiles past its walls: a pack of three at x≈11.
        var pack = new[]
        {
            sim.SpawnUnit("dummy", 1, new Vector2(11.5f, 4.5f)),
            sim.SpawnUnit("dummy", 1, new Vector2(11.5f, 5.5f)),
            sim.SpawnUnit("dummy", 1, new Vector2(12.4f, 4.9f)),
        };
        var friend = sim.SpawnUnit("dummy", 0, new Vector2(12f, 4f));
        var straggler = sim.SpawnUnit("dummy", 1, new Vector2(11.5f, 9.5f)); // in range, but 5 tiles from the pack
        sim.TakeEvents();

        // The first stone (cooldown 1 s, flight under a second) lands within two seconds.
        RunSeconds(sim, 1.6f);

        var events = sim.TakeEvents();
        Assert.That(events.OfType<SplashLanded>().Count(), Is.GreaterThanOrEqualTo(1));
        Assert.That(pack.All(u => u.Health < 100), Is.True, "everyone in the pack is hit by the same stone");
        Assert.That(friend.Health, Is.EqualTo(100), "no friendly fire");
        Assert.That(straggler.Health, Is.EqualTo(100), "outside the splash");
        Assert.That(events.OfType<AttackLanded>().Where(h => h.Splash).Select(h => h.TargetId).Distinct().Count(), Is.EqualTo(3));
    }

    [Test]
    public void ShippedCatapultHitsWeakerAndCloserThanTheArcherTowerAtEveryLevel()
    {
        var rules = TestSim.RealRules();
        var catapult = rules.GetBuildingType("catapult");
        var tower = rules.GetBuildingType("tower");

        Assert.That(catapult.Cost, Is.EqualTo(300));
        Assert.That(catapult.MaxTier, Is.EqualTo(3), "two upgrades");
        Assert.That(tower.Attack!.Range, Is.GreaterThan(7f), "archer towers reach further than before (7)");
        for (BuildingType? c = catapult, t = tower; c != null && t != null; c = c.NextTier, t = t.NextTier)
        {
            Assert.That(c.Attack!.IsSplash, Is.True);
            Assert.That(t.Attack!.IsSplash, Is.False);
            Assert.That(c.Attack.Damage, Is.LessThan(t.Attack.Damage), $"level {c.Tier}: weaker hits");
            Assert.That(c.Attack.Range, Is.LessThan(t.Attack.Range), $"level {c.Tier}: shorter reach");
        }
        Assert.That(catapult.NextTier!.NextTier!.Attack!.SplashRadius, Is.GreaterThan(catapult.Attack!.SplashRadius));
    }
}

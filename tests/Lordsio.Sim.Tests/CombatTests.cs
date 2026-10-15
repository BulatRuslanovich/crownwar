using System.Numerics;
using Lordsio.Sim.Combat;
using Lordsio.Sim.Core;
using Lordsio.Sim.Units;
using Lordsio.Sim.World;
using static Lordsio.Sim.Tests.TestSim;

namespace Lordsio.Sim.Tests;

public class CombatTests
{
    private static readonly string[] Field =
    {
        "....................",
        "....................",
        "....................",
        "....................",
        "....................",
        "....................",
        "....................",
        "....................",
        "....................",
        "....................",
    };

    private static Simulation EmptySim() => new(TestMaps.Parse(Field), TestMaps.Rules, MatchSetup.Sandbox);

    [Test]
    public void MeleeDamageAndDeathFollowCooldown()
    {
        var sim = EmptySim();
        sim.SpawnUnit("infantry", 0, new Vector2(2.5f, 2.5f));
        var dummy = sim.SpawnUnit("dummy", 1, new Vector2(3.1f, 2.5f));

        // 100 hp / 10 damage = 10 hits, one per second; the first lands within the first scan interval.
        RunSeconds(sim, 8.5f);
        Assert.That(dummy.IsAlive, Is.True);
        Assert.That(dummy.Health, Is.EqualTo(10).Or.EqualTo(20));

        RunSeconds(sim, 2f);
        Assert.That(dummy.IsAlive, Is.False);
        Assert.That(sim.GetUnit(dummy.Id), Is.Null, "dead units are removed");
        var died = sim.TakeEvents().OfType<UnitDied>().Single();
        Assert.That(died.UnitId, Is.EqualTo(dummy.Id));
        Assert.That(died.KillerFactionId, Is.EqualTo(0));
    }

    [Test]
    public void AUnitKilledEarlierInATickDoesNotStrikeBack()
    {
        var sim = EmptySim();
        var first = sim.SpawnUnit("infantry", 0, new Vector2(2.5f, 2.5f));
        var second = sim.SpawnUnit("infantry", 1, new Vector2(3.1f, 2.5f));
        first.Health = second.Health = 10; // one blow kills either
        sim.Issue(new AttackCommand(0, new[] { first.Id }, second.Id));
        sim.Issue(new AttackCommand(1, new[] { second.Id }, first.Id));

        sim.Step(); // both are in reach and ready: the first in line strikes first

        Assert.That(second.IsAlive, Is.False);
        Assert.That(first.IsAlive, Is.True, "the dead do not fight on until the end of the tick");
    }

    [Test]
    public void ATowerDestroyedEarlierInATickDoesNotShoot()
    {
        var sim = TestMaps.WithCapitals(2, TestMaps.Field(20, 10), "start 1 1", "start 16 6", "unit infantry 0 8 6");
        sim.Issue(new BuildCommand(0, "tower", new Cell(8, 3)));
        RunSeconds(sim, TestMaps.Tower.BuildTime + 0.2f);
        var tower = sim.Buildings.Single(b => b.Type.Id == "tower");
        var raider = sim.SpawnUnit("infantry", 1, new Vector2(9.3f, 3.5f));
        sim.Issue(new AttackCommand(1, new[] { raider.Id }, tower.Id));
        RunTicks(sim, CombatSystem.ScanIntervalTicks); // the tower takes aim
        Assert.That(tower.Target, Is.SameAs(raider));
        sim.TakeEvents();

        // Both ready to strike in the same tick; units act before towers, and the tower has one hit point left.
        tower.Health = 1;
        tower.AttackCooldownTicks = 0;
        raider.AttackCooldownTicks = 0;
        sim.Step();

        Assert.That(tower.IsAlive, Is.False);
        Assert.That(sim.TakeEvents().OfType<ProjectileFired>().Where(e => e.FactionId == 0), Is.Empty,
            "a tower brought down in a tick fires nothing that tick");
    }

    [Test]
    public void ArcherHitsWithAProjectile()
    {
        var sim = EmptySim();
        sim.SpawnUnit("archer", 0, new Vector2(2.5f, 2.5f));
        var dummy = sim.SpawnUnit("dummy", 1, new Vector2(5.5f, 2.5f));

        bool sawProjectile = false;
        for (int i = 0; i < 20; i++)
        {
            sim.Step();
            sawProjectile |= sim.Projectiles.Count > 0;
        }

        Assert.That(sawProjectile, Is.True);
        Assert.That(dummy.Health, Is.EqualTo(90));
    }

    [Test]
    public void HitsAreReportedForSoundsAndEffects()
    {
        var sim = EmptySim();
        var archer = sim.SpawnUnit("archer", 0, new Vector2(2.5f, 2.5f));
        var soldier = sim.SpawnUnit("infantry", 0, new Vector2(8.5f, 6.5f));
        var dummy = sim.SpawnUnit("dummy", 1, new Vector2(5.5f, 2.5f));
        var other = sim.SpawnUnit("dummy", 1, new Vector2(9.1f, 6.5f));

        for (int i = 0; i < 20; i++)
            sim.Step();
        var events = sim.TakeEvents();

        Assert.That(events.OfType<ProjectileFired>().First().FactionId, Is.EqualTo(0));
        var arrow = events.OfType<AttackLanded>().First(e => e.Ranged);
        Assert.That((arrow.AttackerId, arrow.TargetId, arrow.TargetFactionId), Is.EqualTo((archer.Id, dummy.Id, 1)));
        var blow = events.OfType<AttackLanded>().First(e => !e.Ranged);
        Assert.That((blow.AttackerId, blow.TargetId), Is.EqualTo((soldier.Id, other.Id)));
    }

    [Test]
    public void ArrowFizzlesWhenTargetDiesFirst()
    {
        var sim = EmptySim();
        sim.SpawnUnit("archer", 0, new Vector2(2.5f, 2.5f));
        var dummy = sim.SpawnUnit("dummy", 1, new Vector2(6.5f, 2.5f));
        while (sim.Projectiles.Count == 0)
            sim.Step();

        dummy.Health = 0; // killed by something else while the arrow flies
        sim.Step();

        Assert.That(sim.Projectiles, Is.Empty);
    }

    [Test]
    public void RangedArmorOnlyReducesProjectileDamage()
    {
        var sim = EmptySim();
        var heavy = sim.SpawnUnit("heavy", 1, new Vector2(2.5f, 2.5f));

        CombatSystem.ApplyDamage(heavy, 10, ranged: true, attackerFactionId: 0);
        Assert.That(heavy.Health, Is.EqualTo(195), "50% ranged armor halves arrows");

        CombatSystem.ApplyDamage(heavy, 10, ranged: false, attackerFactionId: 0);
        Assert.That(heavy.Health, Is.EqualTo(185), "melee ignores ranged armor");

        CombatSystem.ApplyDamage(heavy, 1, ranged: true, attackerFactionId: 0);
        Assert.That(heavy.Health, Is.EqualTo(184), "every hit deals at least 1");
    }

    [Test]
    public void IdleUnitsAttackEnemiesInSight()
    {
        var sim = EmptySim();
        var a = sim.SpawnUnit("infantry", 0, new Vector2(2.5f, 5.5f));
        var b = sim.SpawnUnit("infantry", 1, new Vector2(6.5f, 5.5f));

        RunSeconds(sim, 15f);

        Assert.That(a.IsAlive && b.IsAlive, Is.False, "one of them must have died");
    }

    [Test]
    public void UnitsOutOfSightIgnoreEachOther()
    {
        var sim = EmptySim();
        var a = sim.SpawnUnit("infantry", 0, new Vector2(2.5f, 5.5f));
        var b = sim.SpawnUnit("infantry", 1, new Vector2(12.5f, 5.5f));

        RunSeconds(sim, 3f);

        Assert.That(a.Health, Is.EqualTo(100));
        Assert.That(b.Health, Is.EqualTo(100));
        Assert.That(a.Position, Is.EqualTo(new Vector2(2.5f, 5.5f)));
    }

    [Test]
    public void ForcedMoveIgnoresEnemies()
    {
        var sim = EmptySim();
        var unit = sim.SpawnUnit("infantry", 0, new Vector2(1.5f, 5.5f));
        var dummy = sim.SpawnUnit("dummy", 1, new Vector2(10.5f, 4.5f));

        sim.Issue(new MoveCommand(0, new[] { unit.Id }, new Vector2(18.5f, 5.5f), MoveMode.Move));
        RunSeconds(sim, 8f);

        Assert.That(dummy.Health, Is.EqualTo(100));
        Assert.That(Vector2.Distance(unit.Position, new Vector2(18.5f, 5.5f)), Is.LessThan(0.01f));
        Assert.That(unit.Order, Is.EqualTo(UnitOrder.None));
    }

    [Test]
    public void MoveOrderPullsUnitsOutOfAFight()
    {
        var sim = EmptySim();
        var unit = sim.SpawnUnit("infantry", 0, new Vector2(2.5f, 2.5f));
        var dummy = sim.SpawnUnit("dummy", 1, new Vector2(3.1f, 2.5f));
        RunSeconds(sim, 2.5f);
        Assert.That(dummy.Health, Is.LessThan(100), "the unit should be fighting before the recall");

        int healthAtRecall = dummy.Health;
        var retreat = new Vector2(18.5f, 8.5f);
        sim.Issue(new MoveCommand(0, new[] { unit.Id }, retreat, MoveMode.Move));
        RunSeconds(sim, 8f);

        Assert.That(dummy.Health, Is.EqualTo(healthAtRecall));
        Assert.That(Vector2.Distance(unit.Position, retreat), Is.LessThan(0.01f));
    }

    [Test]
    public void AttackMoveFightsOnTheWayThenContinues()
    {
        var sim = EmptySim();
        var unit = sim.SpawnUnit("infantry", 0, new Vector2(1.5f, 5.5f));
        var dummy = sim.SpawnUnit("dummy", 1, new Vector2(10.5f, 4.5f));
        var target = new Vector2(18.5f, 5.5f);

        sim.Issue(new MoveCommand(0, new[] { unit.Id }, target, MoveMode.AttackMove));
        RunSeconds(sim, 25f);

        Assert.That(dummy.IsAlive, Is.False);
        Assert.That(Vector2.Distance(unit.Position, target), Is.LessThan(1.5f));
        Assert.That(unit.Order, Is.EqualTo(UnitOrder.None));
    }

    [Test]
    public void AttackUnitCommandHuntsTargetBeyondSight()
    {
        var sim = EmptySim();
        var unit = sim.SpawnUnit("infantry", 0, new Vector2(1.5f, 1.5f));
        var dummy = sim.SpawnUnit("dummy", 1, new Vector2(18.5f, 8.5f));

        sim.Issue(new AttackCommand(0, new[] { unit.Id }, dummy.Id));
        RunSeconds(sim, 20f);

        Assert.That(dummy.IsAlive, Is.False);
        Assert.That(unit.Order, Is.EqualTo(UnitOrder.None));
    }

    [Test]
    public void CannotAttackOwnUnits()
    {
        var sim = EmptySim();
        var unit = sim.SpawnUnit("infantry", 0, new Vector2(1.5f, 1.5f));
        var friend = sim.SpawnUnit("infantry", 0, new Vector2(3.5f, 1.5f));

        sim.Issue(new AttackCommand(0, new[] { unit.Id }, friend.Id));
        RunSeconds(sim, 3f);

        Assert.That(friend.Health, Is.EqualTo(100));
        Assert.That(unit.Order, Is.EqualTo(UnitOrder.None));
    }

    [Test]
    public void GuardReturnsToPostAfterKillingIntruder()
    {
        var sim = EmptySim();
        var post = new Vector2(2.5f, 5.5f);
        var guard = sim.SpawnUnit("infantry", 1, post);
        var intruder = sim.SpawnUnit("dummy", 0, new Vector2(6.5f, 5.5f));

        RunSeconds(sim, 20f);

        Assert.That(intruder.IsAlive, Is.False);
        Assert.That(Vector2.Distance(guard.Position, post), Is.LessThan(1f));
        Assert.That(guard.State, Is.EqualTo(UnitState.Idle));
    }

    [Test]
    public void GuardStopsChasingBeyondLeash()
    {
        var sim = EmptySim();
        var post = new Vector2(2.5f, 5.5f);
        var guard = sim.SpawnUnit("infantry", 1, post);
        var runner = sim.SpawnUnit("infantry", 0, new Vector2(6.5f, 5.5f));

        // The runner flees far east; the guard follows a while but must give up and walk home.
        sim.Issue(new MoveCommand(0, new[] { runner.Id }, new Vector2(19.5f, 5.5f), MoveMode.Move));
        RunSeconds(sim, 15f);

        Assert.That(Vector2.Distance(guard.Position, post), Is.LessThan(1f));
    }
}

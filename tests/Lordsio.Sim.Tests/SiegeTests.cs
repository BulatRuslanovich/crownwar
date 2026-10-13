using System.Numerics;
using Lordsio.Sim.Buildings;
using Lordsio.Sim.Core;
using Lordsio.Sim.Units;

namespace Lordsio.Sim.Tests;

/// <summary>Towers, attacking buildings, and losing the capital.</summary>
public class SiegeTests
{
    private static readonly string[] Field = TestMaps.Field(24, 12);

    private static void RunSeconds(Simulation sim, float seconds, Func<bool>? stop = null)
    {
        for (int i = 0; i < seconds * Simulation.TicksPerSecond && !(stop?.Invoke() ?? false); i++)
            sim.Step();
    }

    private static Building BuildTower(Simulation sim, int faction, int x, int y)
    {
        sim.Issue(new BuildCommand(faction, "tower", At(x, y)));
        RunSeconds(sim, TestMaps.Tower.BuildTime + 0.2f);
        return sim.Buildings.Single(b => b.Type.Id == "tower" && b.FactionId == faction);
    }

    [Test]
    public void TowerShootsEnemiesInRangeOnly()
    {
        var sim = TestMaps.WithCapitals(1, Field, "start 1 1", "unit dummy 0 6 6");
        var tower = BuildTower(sim, 0, 6, 4);
        var near = sim.SpawnUnit("dummy", 1, new Vector2(9.5f, 4.5f));
        var far = sim.SpawnUnit("dummy", 1, new Vector2(16.5f, 4.5f));

        RunSeconds(sim, 3f);

        Assert.That(near.Health, Is.LessThan(100));
        Assert.That(far.Health, Is.EqualTo(100));
        Assert.That(tower.Target, Is.SameAs(near));
    }

    [Test]
    public void UnfinishedTowerDoesNotShoot()
    {
        var sim = TestMaps.WithCapitals(1, Field, "start 1 1", "unit dummy 0 6 6");
        sim.Issue(new BuildCommand(0, "tower", At(6, 4)));
        var enemy = sim.SpawnUnit("dummy", 1, new Vector2(8.5f, 4.5f));

        RunSeconds(sim, TestMaps.Tower.BuildTime - 0.5f);

        Assert.That(enemy.Health, Is.EqualTo(100));
    }

    [Test]
    public void UnitsAttackBuildingsOnOrderAndDestroyThem()
    {
        var sim = TestMaps.WithCapitals(1, Field, "start 1 1", "unit dummy 0 10 6");
        var tower = BuildTower(sim, 0, 12, 6);
        // Heavies laugh at arrows: 2 of them flatten the 100 hp tower before it can kill them.
        var attackers = new[] { sim.SpawnUnit("heavy", 1, new Vector2(20.5f, 6.5f)), sim.SpawnUnit("heavy", 1, new Vector2(20.5f, 7.5f)) };

        sim.Issue(new AttackCommand(1, attackers.Select(a => a.Id).ToList(), tower.Id));
        RunSeconds(sim, 15f, () => !tower.IsAlive);

        Assert.That(tower.IsAlive, Is.False);
        Assert.That(attackers.All(a => a.IsAlive), Is.True);
        Assert.That(sim.Map.IsWalkable(new World.Cell(12, 6)), Is.True);
    }

    [Test]
    public void AttackMovingArmyPrefersUnitsThenBuildings()
    {
        var sim = TestMaps.WithCapitals(1, Field, "start 1 1", "unit dummy 0 9 6");
        sim.GetFaction(0)!.Treasury.Add(100);
        sim.Issue(new BuildCommand(0, "barracks", At(8, 7)));
        sim.Step();
        var barracks = sim.Buildings.Single(b => b.Type.Id == "barracks");
        var defender = sim.Units.Single();
        var attacker = sim.SpawnUnit("infantry", 1, new Vector2(20.5f, 7.5f));

        sim.Issue(new MoveCommand(1, new[] { attacker.Id }, new Vector2(10.5f, 9.5f), MoveMode.AttackMove));
        RunSeconds(sim, 30f, () => !barracks.IsAlive);

        Assert.That(defender.IsAlive, Is.False, "the soldier is dealt with first");
        Assert.That(barracks.IsAlive, Is.False, "then the building in sight");
    }

    [Test]
    public void MoveOrderPullsTroopsAwayFromABuildingTheyAreBreaking()
    {
        var sim = TestMaps.WithCapitals(2, Field, "start 1 1", "start 20 8", "unit dummy 0 6 6");
        var capital = sim.GetBuilding(sim.GetFaction(0)!.CapitalId)!;
        var army = Enumerable.Range(0, 3).Select(i => sim.SpawnUnit("infantry", 1, new Vector2(4.5f + i * 0.7f, 4.5f))).ToList();
        var ids = army.Select(a => a.Id).ToList();
        sim.Issue(new AttackCommand(1, ids, capital.Id));
        RunSeconds(sim, 5f);
        Assert.That(capital.Health, Is.LessThan(capital.MaxHealth), "the troops should be hitting the castle before the recall");

        // Plain move (the default right click): the order beats the fight, whatever stands nearby.
        sim.Issue(new MoveCommand(1, ids, new Vector2(18.5f, 9.5f), MoveMode.Move));
        sim.Step();
        int healthAtRecall = capital.Health;
        RunSeconds(sim, 10f);

        Assert.That(capital.Health, Is.EqualTo(healthAtRecall));
        Assert.That(army.All(u => Vector2.Distance(u.Position, new Vector2(18.5f, 9.5f)) < 3f), Is.True);
    }

    [Test]
    public void DestroyingTheCapitalEliminatesFactionAndEndsMatch()
    {
        var sim = TestMaps.WithCapitals(2, Field, "start 1 1", "start 20 8", "unit dummy 0 6 6");
        var capital = sim.GetBuilding(sim.GetFaction(0)!.CapitalId)!;
        sim.GetFaction(0)!.Treasury.Add(100);
        sim.Issue(new BuildCommand(0, "barracks", At(8, 6)));
        sim.Step();

        var army = Enumerable.Range(0, 5).Select(i => sim.SpawnUnit("infantry", 1, new Vector2(4.5f + i * 0.7f, 4.5f))).ToList();
        sim.Issue(new AttackCommand(1, army.Select(a => a.Id).ToList(), capital.Id));
        RunSeconds(sim, 30f, () => sim.IsOver);

        Assert.That(sim.IsOver, Is.True);
        Assert.That(sim.WinningTeam, Is.EqualTo(1));
        Assert.That(sim.GetFaction(0)!.IsEliminated, Is.True);
        Assert.That(sim.Units.Any(u => u.FactionId == 0), Is.False, "the army disbands");
        Assert.That(sim.Buildings.Any(b => b.FactionId == 0), Is.False, "every building collapses");
        var events = sim.TakeEvents();
        Assert.That(events.OfType<FactionEliminated>().Single().FactionId, Is.EqualTo(0));
        Assert.That(events.OfType<MatchEnded>().Single().WinningTeam, Is.EqualTo(1));

        int tick = sim.Tick;
        sim.Step();
        Assert.That(sim.Tick, Is.EqualTo(tick), "the world is frozen after the match ends");
    }

    [Test]
    public void EliminatedFactionCannotAct()
    {
        var sim = TestMaps.WithCapitals(3, Field, "start 1 1", "start 20 8", "start 20 1");
        var capital = sim.GetBuilding(sim.GetFaction(0)!.CapitalId)!;
        Combat.CombatSystem.ApplyDamage(capital, 10_000, ranged: false, attackerFactionId: 1);
        sim.Step();

        Assert.That(sim.GetFaction(0)!.IsEliminated, Is.True);
        Assert.That(sim.IsOver, Is.False, "two factions remain");
        Assert.That(sim.CanBuild(0, "tower", new World.Cell(5, 5)), Is.EqualTo(BuildError.NotBuildable));
    }

    /// <summary>Shorthand so placement calls read like coordinates.</summary>
    private static World.Cell At(int x, int y) => new(x, y);
}

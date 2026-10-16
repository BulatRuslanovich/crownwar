using System.Numerics;
using Warcrown.Sim.Buildings;
using Warcrown.Sim.Combat;
using Warcrown.Sim.Core;
using Warcrown.Sim.World;
using static Warcrown.Sim.Tests.TestSim;

namespace Warcrown.Sim.Tests;

/// <summary>
/// The unit roster of the shipped rules: training only in the castle, each finished barracks opening one more kind and
/// one more training place, spearmen against horses, crossbow bolts through plate, monks mending, trebuchets against walls.
/// </summary>
public class UnitRosterTests
{
    private static readonly GameRules Rules =
        TestSim.RealRules();

    /// <summary>Two capitals far apart on open ground, plenty of gold for faction 0.</summary>
    private static Simulation CreateSim()
    {
        var sim = new Simulation(TestMaps.Parse(TestMaps.Field(40, 16), "start 2 2", "start 34 11"), Rules,
            new MatchSetup(2, ShuffleStarts: false));
        sim.GetFaction(0)!.Treasury.Add(4000);
        return sim;
    }

    private static Building BuildBarracks(Simulation sim, Cell anchor)
    {
        sim.Issue(new BuildCommand(0, "barracks", anchor));
        RunSeconds(sim, Rules.GetBuildingType("barracks").BuildTime + 0.5f);
        return sim.Buildings.Single(b => b.Type.Id == "barracks" && b.Anchor == anchor && b.IsComplete);
    }

    [Test]
    public void EachBarracksOpensOneMoreKindAndLosingThemClosesIt()
    {
        var sim = CreateSim();
        var castle = CapitalOf(sim, 0);
        Assert.That(sim.CanRecruit(0, castle.Id, "infantry"), Is.EqualTo(RecruitError.None), "spearmen from the start");
        Assert.That(sim.CanRecruit(0, castle.Id, "archer"), Is.EqualTo(RecruitError.Locked));

        var first = BuildBarracks(sim, new Cell(6, 2));
        Assert.That(sim.UnlockLevel(0), Is.EqualTo(1));
        Assert.That(sim.CanRecruit(0, castle.Id, "archer"), Is.EqualTo(RecruitError.None), "the first barracks opens archers");
        Assert.That(sim.CanRecruit(0, castle.Id, "cavalry"), Is.EqualTo(RecruitError.Locked), "and only them");
        Assert.That(sim.CanRecruit(0, first.Id, "infantry"), Is.EqualTo(RecruitError.CannotRecruitType), "barracks train nobody");

        BuildBarracks(sim, new Cell(6, 5));
        Assert.That(sim.CanRecruit(0, castle.Id, "cavalry"), Is.EqualTo(RecruitError.None), "the second opens cavalry");

        CombatSystem.ApplyDamage(first, first.MaxHealth, ranged: false, attackerFactionId: 1);
        sim.Step();
        Assert.That(sim.UnlockLevel(0), Is.EqualTo(1));
        Assert.That(sim.CanRecruit(0, castle.Id, "cavalry"), Is.EqualTo(RecruitError.Locked), "a barracks lost, a step closed");
        Assert.That(sim.CanRecruit(0, castle.Id, "archer"), Is.EqualTo(RecruitError.None));
    }

    [Test]
    public void EveryBarracksAddsATrainingPlaceInTheCastle()
    {
        var sim = CreateSim();
        var castle = CapitalOf(sim, 0);
        BuildBarracks(sim, new Cell(6, 2));
        BuildBarracks(sim, new Cell(6, 5));
        Assert.That(sim.TrainingSlots(castle), Is.EqualTo(3));

        sim.TakeEvents();
        for (int i = 0; i < 4; i++)
            sim.Issue(new RecruitCommand(0, castle.Id, "infantry"));
        RunSeconds(sim, Rules.Units.Get("infantry").BuildTime + 0.2f);

        Assert.That(sim.TakeEvents().OfType<UnitRecruited>().Count(), Is.EqualTo(3), "three train side by side");
        Assert.That(castle.Queue.Count, Is.EqualTo(1), "the fourth waits for a free place");
    }

    [Test]
    public void SpearmenHitHorsesTwiceAsHard()
    {
        var sim = CreateSim();
        var spearman = sim.SpawnUnit("infantry", 0, new Vector2(15.5f, 8.5f));
        var rider = sim.SpawnUnit("cavalry", 1, new Vector2(16.5f, 8.5f));
        var other = sim.SpawnUnit("infantry", 1, new Vector2(17.5f, 8.5f));

        Assert.That(spearman.Type.DamageAgainst(rider), Is.EqualTo(2 * spearman.Type.Damage));
        Assert.That(spearman.Type.DamageAgainst(other), Is.EqualTo(spearman.Type.Damage));
    }

    [Test]
    public void CrossbowBoltsGoThroughPlate()
    {
        var sim = CreateSim();
        var bolted = sim.SpawnUnit("heavy", 1, new Vector2(15.5f, 8.5f));
        var shot = sim.SpawnUnit("heavy", 1, new Vector2(17.5f, 8.5f));
        var crossbow = Rules.Units.Get("crossbowman");
        int damage = crossbow.Damage;

        CombatSystem.ApplyDamage(bolted, damage, ranged: true, attackerFactionId: 0, crossbow.ArmorPiercing);
        CombatSystem.ApplyDamage(shot, damage, ranged: true, attackerFactionId: 0);

        Assert.That(bolted.MaxHealth - bolted.Health, Is.GreaterThan(shot.MaxHealth - shot.Health));
        Assert.That(bolted.MaxHealth - bolted.Health,
            Is.EqualTo((int)MathF.Round(damage * (1f - bolted.RangedArmor * (1f - crossbow.ArmorPiercing)))));
    }

    [Test]
    public void MonksMendWoundedFriendsAndNeverFight()
    {
        var sim = CreateSim();
        var monk = sim.SpawnUnit("monk", 0, new Vector2(15.5f, 8.5f));
        var wounded = sim.SpawnUnit("infantry", 0, new Vector2(17.5f, 8.5f));
        var enemy = sim.SpawnUnit("archer", 1, new Vector2(30.5f, 3.5f)); // far off: nobody shoots anybody
        CombatSystem.ApplyDamage(wounded, 60, ranged: false, attackerFactionId: 1);
        sim.Issue(new AttackCommand(0, new[] { monk.Id }, enemy.Id));
        sim.TakeEvents();
        int before = wounded.Health;
        RunSeconds(sim, 6f);

        var healed = sim.TakeEvents().OfType<UnitHealed>().Where(h => h.HealerId == monk.Id).ToList();
        Assert.That(healed, Is.Not.Empty);
        Assert.That(healed.All(h => h.TargetId == wounded.Id), Is.True);
        Assert.That(wounded.Health, Is.EqualTo(Math.Min(wounded.MaxHealth, before + healed.Sum(h => h.Amount))));
        Assert.That(wounded.Health, Is.GreaterThan(before));
        Assert.That(enemy.Health, Is.EqualTo(enemy.MaxHealth));
        Assert.That(monk.Order, Is.Not.EqualTo(Units.UnitOrder.AttackUnit), "monks take no orders to attack");
    }

    [Test]
    public void TrebuchetsBatterWallsFromBeyondTheCastlesReach()
    {
        var sim = CreateSim();
        var enemyCastle = CapitalOf(sim, 1);
        var trebuchetType = Rules.Units.Get("trebuchet");
        // Just inside its own range, outside the castle's.
        var at = enemyCastle.Position - new Vector2(enemyCastle.Type.Size / 2f + trebuchetType.AttackRange - 0.5f, 0f);
        var trebuchet = sim.SpawnUnit("trebuchet", 0, at);
        Assert.That(enemyCastle.SurfaceDistance(at), Is.GreaterThan(enemyCastle.Type.Attack!.Range));

        RunSeconds(sim, trebuchetType.AttackCooldown * 2 + 3f);

        int perStone = (int)MathF.Round(trebuchetType.Damage * trebuchetType.BuildingDamageMultiplier);
        Assert.That(enemyCastle.MaxHealth - enemyCastle.Health, Is.GreaterThanOrEqualTo(perStone), "its stones ignore the walls' armour");
        Assert.That(trebuchet.Health, Is.EqualTo(trebuchet.MaxHealth), "the castle cannot answer that far");
    }
}

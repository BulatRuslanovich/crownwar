using System.Numerics;
using Lordsio.Sim.Core;
using Lordsio.Sim.Units;
using static Lordsio.Sim.Tests.TestSim;

namespace Lordsio.Sim.Tests;

public class VeterancyTests
{
    private static Simulation Field() => new(TestMaps.Parse(TestMaps.Field(20, 10)), RealRules(), MatchSetup.Sandbox);

    /// <summary>An enemy of <paramref name="typeId"/> next to <paramref name="killer"/>, one blow from death.</summary>
    private static Unit Victim(Simulation sim, Unit killer, string typeId, float dx = 0.6f)
    {
        var victim = sim.SpawnUnit(typeId, 1, killer.Position + new Vector2(dx, 0f));
        victim.Health = 1;
        victim.AttackCooldownTicks = 1000; // does not strike back before it falls
        return victim;
    }

    [Test]
    public void TheKillingBlowEarnsWhatTheFallenCost()
    {
        var sim = Field();
        var soldier = sim.SpawnUnit("infantry", 0, new Vector2(2.5f, 2.5f));
        var victim = Victim(sim, soldier, "infantry");

        RunSeconds(sim, 1f);

        Assert.That(victim.IsAlive, Is.False);
        Assert.That(soldier.Experience, Is.EqualTo(victim.Type.Cost));
        Assert.That(soldier.Rank, Is.Zero, "one recruit is not enough for a rank");
        Assert.That(sim.TakeEvents().OfType<UnitPromoted>(), Is.Empty);
    }

    [Test]
    public void ARankMakesTheUnitTougherAndHitHarder()
    {
        var sim = Field();
        var rank = sim.Rules.Veterancy.Ranks[0];
        var soldier = sim.SpawnUnit("infantry", 0, new Vector2(2.5f, 2.5f));
        soldier.Health = 40; // wounded: the wound stays, the new toughness comes on top
        var dummy = sim.SpawnUnit("infantry", 1, new Vector2(12.5f, 7.5f));
        int recruitDamage = soldier.DamageAgainst(dummy);
        var victim = Victim(sim, soldier, "heavy"); // worth more than the first rank needs
        Assume.That(victim.Type.Cost, Is.GreaterThanOrEqualTo(rank.Experience));

        RunSeconds(sim, 1f);

        Assert.That(soldier.Rank, Is.EqualTo(1));
        int max = (int)MathF.Round(soldier.Type.MaxHealth * rank.HealthFactor);
        Assert.That(soldier.MaxHealth, Is.EqualTo(max));
        Assert.That(soldier.Health, Is.EqualTo(40 + max - soldier.Type.MaxHealth));
        Assert.That(soldier.DamageAgainst(dummy), Is.EqualTo((int)MathF.Round(recruitDamage * rank.DamageFactor)));
        var promoted = sim.TakeEvents().OfType<UnitPromoted>().Single();
        Assert.That((promoted.UnitId, promoted.FactionId, promoted.Rank), Is.EqualTo((soldier.Id, 0, 1)));
    }

    [Test]
    public void EnoughExperienceAtOnceSkipsToTheRankItEarns()
    {
        var sim = Field();
        var soldier = sim.SpawnUnit("infantry", 0, new Vector2(2.5f, 2.5f));
        int top = sim.Rules.Veterancy.Ranks.Count;
        var victim = Victim(sim, soldier, "warlord"); // the Defense boss is worth a lot
        Assume.That(victim.Type.Cost, Is.GreaterThanOrEqualTo(sim.Rules.Veterancy.Ranks[^1].Experience));

        RunSeconds(sim, 1f);

        Assert.That(soldier.Rank, Is.EqualTo(top));
        Assert.That(sim.TakeEvents().OfType<UnitPromoted>().Single().Rank, Is.EqualTo(top));
    }

    [Test]
    public void AnArcherEarnsByItsArrow()
    {
        var sim = Field();
        var archer = sim.SpawnUnit("archer", 0, new Vector2(2.5f, 2.5f));
        var victim = Victim(sim, archer, "infantry", dx: 4f);

        RunSeconds(sim, 2f);

        Assert.That(victim.IsAlive, Is.False);
        Assert.That(archer.Experience, Is.EqualTo(victim.Type.Cost));
    }

    [Test]
    public void AMonkMendsAVeteranUpToItsGreaterHealth()
    {
        var sim = Field();
        var soldier = sim.SpawnUnit("infantry", 0, new Vector2(2.5f, 2.5f));
        Victim(sim, soldier, "heavy");
        RunSeconds(sim, 1f);
        Assume.That(soldier.Rank, Is.EqualTo(1));

        soldier.Health = 10;
        sim.SpawnUnit("monk", 0, new Vector2(3.5f, 3.5f));
        RunSeconds(sim, 30f);

        Assert.That(soldier.Health, Is.EqualTo(soldier.MaxHealth));
        Assert.That(soldier.MaxHealth, Is.GreaterThan(soldier.Type.MaxHealth));
    }

    [Test]
    public void RulesWithoutVeterancyMakeNoVeterans()
    {
        var sim = new Simulation(TestMaps.Parse(TestMaps.Field(20, 10)), TestMaps.Rules, MatchSetup.Sandbox);
        var soldier = sim.SpawnUnit("infantry", 0, new Vector2(2.5f, 2.5f));
        var victim = Victim(sim, soldier, "heavy");

        RunSeconds(sim, 1f);

        Assert.That(victim.IsAlive, Is.False);
        Assert.That((soldier.Experience, soldier.Rank), Is.EqualTo((0, 0)));
    }

    [Test]
    public void RanksMustClimb()
    {
        static void Check(params VeteranRank[] ranks) => new VeterancyRules(ranks).Validate();
        static VeteranRank R(int experience, float health, float damage) => new(experience, health, damage);

        Assert.That(() => Check(R(100, 1.1f, 1.1f), R(300, 1.2f, 1.2f)), Throws.Nothing);
        Assert.That(() => Check(R(100, 1f, 1f), R(100, 1f, 1f)), Throws.ArgumentException, "the same experience twice");
        Assert.That(() => Check(R(100, 1f, 1.2f), R(200, 1f, 1.1f)), Throws.ArgumentException, "a rank weaker than the one below");
        Assert.That(() => Check(R(0, 1f, 1f)), Throws.ArgumentException, "a rank for nothing");
    }
}

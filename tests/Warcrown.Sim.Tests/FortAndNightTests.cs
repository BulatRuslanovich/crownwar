using System.Numerics;
using Warcrown.Sim.Buildings;
using Warcrown.Sim.Core;
using Warcrown.Sim.Units;
using Warcrown.Sim.World;
using static Warcrown.Sim.Tests.TestSim;

namespace Warcrown.Sim.Tests;

/// <summary>Neutral forts that change hands when brought down, and towers that see less far at night unless a beacon lights them.</summary>
public class FortAndNightTests
{
    private static readonly string[] Field = TestMaps.Field(30, 14);

    private static readonly BuildingType Fort = new("fort", "Fort", Size: 3, Cost: 0, BuildTime: 0f, MaxHealth: 100,
        RangedArmor: 0f, Income: 4, ArmyCap: 0, Recruits: Array.Empty<string>(), IsCapturable: true);

    private static readonly BuildingType Watchtower = new("watchtower", "Watchtower", Size: 2, Cost: 50, BuildTime: 1f, MaxHealth: 100,
        RangedArmor: 0f, Income: 0, ArmyCap: 0, Recruits: Array.Empty<string>(), SightRange: 10f, LightRadius: 6f);

    private static readonly GameRules Rules = new(
        startingGold: 1000,
        buildRadius: 6f,
        startingUnits: Array.Empty<StartingUnits>(),
        new UnitCatalog(new[] { TestMaps.Infantry, TestMaps.Archer, TestMaps.Dummy }),
        new[] { TestMaps.Castle, TestMaps.Tower, Fort, Watchtower },
        daySeconds: 10f,
        nightSeconds: 10f,
        nightTowerRangeFactor: 0.5f,
        forts: new FortRules(50, new[] { new StartingUnits("dummy", 2) }));

    /// <summary>Faction 0 in the west, faction 1 in the east, a fort between them (cells 13–15, 5–7) guarded by two dummies.</summary>
    private static Simulation CreateSim(params string[] objects) =>
        new(TestMaps.Parse(Field, new[] { "start 1 1", "start 26 10", "fort 13 5" }.Concat(objects).ToArray()), Rules,
            new MatchSetup(2, ShuffleStarts: false));

    private static Building FortOf(Simulation sim) => sim.Buildings.Single(b => b.Type.IsCapturable);

    /// <summary>Sends six soldiers of <paramref name="faction"/> at the fort and runs until it is theirs; returns the events meanwhile.</summary>
    private static List<SimEvent> Storm(Simulation sim, int faction, Vector2 from)
    {
        var fort = FortOf(sim);
        var soldiers = Enumerable.Range(0, 6).Select(i => sim.SpawnUnit("infantry", faction, from + new Vector2(0f, i * 0.7f))).ToList();
        sim.Issue(new AttackCommand(faction, soldiers.Select(u => u.Id).ToList(), fort.Id));
        var events = new List<SimEvent>();
        for (int i = 0; i < 60 * Simulation.TicksPerSecond && fort.FactionId != faction; i++)
        {
            sim.Step();
            events.AddRange(sim.TakeEvents());
        }
        return events;
    }

    // ---------------------------------------------------------------- forts

    [Test]
    public void AFortStartsNeutralHeldByItsGuard()
    {
        var sim = CreateSim();
        var fort = FortOf(sim);

        Assert.That(fort.FactionId, Is.EqualTo(Simulation.NeutralFactionId));
        var guard = sim.Units.Where(u => u.FactionId == Simulation.NeutralFactionId).ToList();
        Assert.That(guard, Has.Count.EqualTo(2));
        Assert.That(guard.All(u => fort.SurfaceDistance(u.Position) < 3f), "the guard stands by the gate");
        Assert.That(sim.AreEnemies(0, fort.FactionId) && sim.AreEnemies(1, fort.FactionId), "nobody's friend");
        Assert.That(Fort.IsBuildable, Is.False, "forts are taken, never built");
    }

    [Test]
    public void BringingAFortDownTakesItOver()
    {
        var sim = CreateSim();
        var fort = FortOf(sim);
        int incomeBefore = sim.GetFaction(0)!.IncomePerSecond;

        var events = Storm(sim, 0, new Vector2(10.5f, 5f));
        sim.Step();

        Assert.That(fort.FactionId, Is.EqualTo(0), "taken, not razed");
        Assert.That(sim.GetBuilding(fort.Id), Is.SameAs(fort));
        Assert.That(fort.Health, Is.EqualTo(Fort.MaxHealth / 2).Within(Fort.MaxHealth / 20), "with half its walls standing");
        Assert.That(events.OfType<FortCaptured>().Single(), Is.EqualTo(new FortCaptured(0, Simulation.NeutralFactionId, fort.Id, fort.Position)));
        Assert.That(events.OfType<BuildingDestroyed>(), Is.Empty);
        Assert.That(sim.Units.Where(u => u.FactionId == 0).All(u => u.Target != fort), "nobody hits his own fort");
        Assert.That(sim.GetFaction(0)!.IncomePerSecond, Is.EqualTo(incomeBefore + Fort.Income));
        Assert.That(sim.CanDemolish(0, fort.Id, out _), Is.EqualTo(DemolishError.Fort), "held, not pulled down");
    }

    [Test]
    public void ATakenFortCanBeTakenBack()
    {
        var sim = CreateSim();
        var fort = FortOf(sim);
        Storm(sim, 0, new Vector2(10.5f, 5f));

        var events = Storm(sim, 1, new Vector2(19.5f, 5f));

        Assert.That(fort.FactionId, Is.EqualTo(1));
        Assert.That(events.OfType<FortCaptured>().Last().FromFactionId, Is.EqualTo(0));
    }

    [Test]
    public void TheFortsOfAnEliminatedFactionGoBackToNobody()
    {
        var sim = CreateSim();
        var fort = FortOf(sim);
        Storm(sim, 0, new Vector2(10.5f, 5f));
        int health = fort.Health;

        CapitalOf(sim, 0).Health = 0;
        sim.Step();

        Assert.That(sim.GetFaction(0)!.IsEliminated, Is.True);
        Assert.That(sim.GetBuilding(fort.Id), Is.SameAs(fort), "the fort stands");
        Assert.That(fort.FactionId, Is.EqualTo(Simulation.NeutralFactionId));
        Assert.That(fort.Health, Is.EqualTo(health));
    }

    // ---------------------------------------------------------------- night

    [Test]
    public void AtNightTowersReachLessUnlessABeaconOfTheirSideLightsThem()
    {
        var sim = CreateSim("unit infantry 0 6 10", "unit dummy 0 2 8"); // the dummy stays put: the beacon is built by it
        sim.Issue(new BuildCommand(0, "tower", new Cell(6, 12)));
        RunSeconds(sim, TestMaps.Tower.BuildTime + 0.2f);
        var tower = sim.Buildings.Single(b => b.Type.Id == "tower");
        var range = TestMaps.Tower.Attack!.Range;

        Assert.That(sim.AttackRangeOf(tower), Is.EqualTo(range), "by day");
        RunSeconds(sim, 10f - sim.Tick / (float)Simulation.TicksPerSecond + 0.1f);
        Assert.That(sim.IsNight, Is.True);
        Assert.That(sim.AttackRangeOf(tower), Is.EqualTo(range * 0.5f), "in the dark");

        // An enemy beyond the dark reach but within the day's: not shot at.
        var intruder = sim.SpawnUnit("dummy", 1, tower.Position + new Vector2(range - 0.8f, 0f));
        RunSeconds(sim, 1f);
        Assert.That(tower.Target, Is.Null);

        sim.Issue(new BuildCommand(0, "watchtower", new Cell(2, 10)));
        RunSeconds(sim, Watchtower.BuildTime + 0.5f);
        Assert.That(sim.IsLit(tower), Is.True, "the beacon lights it");
        Assert.That(sim.AttackRangeOf(tower), Is.EqualTo(range));
        Assert.That(tower.Target, Is.SameAs(intruder), "and it shoots as by day");
    }

    [Test]
    public void AnEnemysBeaconLightsNothingForUs()
    {
        var sim = CreateSim("unit infantry 0 6 10", "unit infantry 1 9 12");
        sim.Issue(new BuildCommand(0, "tower", new Cell(6, 12)));
        sim.Issue(new BuildCommand(1, "watchtower", new Cell(9, 9)));
        RunSeconds(sim, 2.5f);
        var tower = sim.Buildings.Single(b => b.Type.Id == "tower");

        Assert.That(sim.IsLit(tower), Is.False);
    }
}

using System.Numerics;
using Lordsio.Sim.Core;
using Lordsio.Sim.Territory;
using Lordsio.Sim.World;

namespace Lordsio.Sim.Tests;

public class CaptureTests
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

    private static Simulation CreateSim(params string[] objects) => new(TestMaps.Parse(Field, objects), TestMaps.Rules);

    private static void RunTicks(Simulation sim, int ticks)
    {
        for (int i = 0; i < ticks; i++)
            sim.Step();
    }

    private static int CaptureTicks(string type) =>
        (int)(TestMaps.Rules.GetSettlementType(type).CaptureTime * Simulation.TicksPerSecond);

    [Test]
    public void LoneUnitCapturesAfterCaptureTime()
    {
        var sim = CreateSim("settlement outpost -1 5 5", "unit dummy 0 5 5");
        var outpost = sim.Settlements[0];

        RunTicks(sim, CaptureTicks("outpost") - 2);
        Assert.That(outpost.OwnerId, Is.EqualTo(Settlement.NeutralOwner));
        Assert.That(outpost.CapturingFactionId, Is.EqualTo(0));
        Assert.That(outpost.CaptureProgress, Is.GreaterThan(0.9f));

        RunTicks(sim, 3);
        Assert.That(outpost.OwnerId, Is.EqualTo(0));
        Assert.That(sim.TakeEvents().OfType<SettlementCaptured>().Single(),
            Is.EqualTo(new SettlementCaptured(0, outpost.Id, Settlement.NeutralOwner)));

        sim.Step();
        Assert.That(sim.GetFaction(0)!.IncomePerSecond, Is.EqualTo(3), "ownership pays income");
    }

    [Test]
    public void MoreUnitsCaptureFaster()
    {
        var sim = CreateSim("settlement outpost -1 5 5", "unit dummy 0 5 5 5");

        RunTicks(sim, CaptureTicks("outpost") / 2 + 1);

        Assert.That(sim.Settlements[0].OwnerId, Is.EqualTo(0), "five units capture twice as fast");
    }

    [Test]
    public void GarrisonMustBeDefeatedFirst()
    {
        // The village starts with one neutral dummy guard that never fights back.
        var sim = CreateSim("settlement village -1 5 5", "unit infantry 0 7 5");
        var village = sim.Settlements[0];
        var guard = sim.Units.Single(u => u.FactionId == Settlement.NeutralOwner);

        RunTicks(sim, 5 * Simulation.TicksPerSecond);
        Assert.That(guard.IsAlive, Is.True, "100 hp at 10 dps takes 10 s");
        Assert.That(village.OwnerId, Is.EqualTo(Settlement.NeutralOwner));
        Assert.That(village.IsContested, Is.True);
        Assert.That(village.CaptureProgress, Is.Zero);

        RunTicks(sim, 6 * Simulation.TicksPerSecond + CaptureTicks("village") + 5);
        Assert.That(guard.IsAlive, Is.False);
        Assert.That(village.OwnerId, Is.EqualTo(0));
    }

    [Test]
    public void TwoAttackersContestAndFreezeProgress()
    {
        var sim = CreateSim("settlement outpost -1 5 5", "unit dummy 0 5 5", "unit dummy 1 6 5");

        RunTicks(sim, CaptureTicks("outpost") * 2);

        var outpost = sim.Settlements[0];
        Assert.That(outpost.OwnerId, Is.EqualTo(Settlement.NeutralOwner));
        Assert.That(outpost.IsContested, Is.True);
        Assert.That(outpost.CaptureProgress, Is.Zero);
    }

    [Test]
    public void DefenderPresenceBlocksCapture()
    {
        var sim = CreateSim("settlement outpost 0 5 5", "unit dummy 0 5 5", "unit dummy 1 6 5");

        RunTicks(sim, CaptureTicks("outpost") * 2);

        Assert.That(sim.Settlements[0].OwnerId, Is.EqualTo(0));
    }

    [Test]
    public void ProgressDecaysWhenAttackerLeaves()
    {
        var sim = CreateSim("settlement outpost -1 5 5", "unit infantry 0 5 5");
        var outpost = sim.Settlements[0];
        var unit = sim.Units[0];
        RunTicks(sim, CaptureTicks("outpost") / 2);
        float halfway = outpost.CaptureProgress;

        sim.Issue(new MoveCommand(0, new[] { unit.Id }, new Vector2(18.5f, 5.5f)));
        RunTicks(sim, CaptureTicks("outpost"));

        Assert.That(halfway, Is.GreaterThan(0.4f));
        Assert.That(outpost.CaptureProgress, Is.Zero);
        Assert.That(outpost.CapturingFactionId, Is.EqualTo(Settlement.NeutralOwner));
    }

    [Test]
    public void CaptureMovesIncomeAndCancelsTraining()
    {
        var sim = CreateSim(
            "settlement castle 0 2 2", "settlement castle 0 17 2", "settlement castle 1 10 8",
            "unit dummy 1 2 3");
        var target = sim.Settlements[0];
        // Order training shortly before the castle falls, so the recruit cannot appear in time to defend it.
        RunTicks(sim, CaptureTicks("castle") - 10);
        sim.Issue(new RecruitCommand(0, target.Id, "infantry"));
        sim.Step();
        Assert.That(target.Queue, Has.Count.EqualTo(1));

        RunTicks(sim, 15);

        Assert.That(target.OwnerId, Is.EqualTo(1));
        Assert.That(target.Queue, Is.Empty);
        Assert.That(sim.GetFaction(0)!.IncomePerSecond, Is.EqualTo(10));
        Assert.That(sim.GetFaction(1)!.IncomePerSecond, Is.EqualTo(20));
        Assert.That(sim.IsOver, Is.False, "faction 0 still holds a capital");
    }
}

public class VictoryTests
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

    [Test]
    public void LosingLastCapitalEliminatesFactionAndEndsMatch()
    {
        var sim = new Simulation(TestMaps.Parse(Field,
            "settlement castle 0 2 2", "settlement outpost 0 2 8", "settlement castle 1 17 8",
            "unit dummy 1 2 2", "unit dummy 0 15 2 3"), TestMaps.Rules);

        for (int i = 0; i < 6 * Simulation.TicksPerSecond && !sim.IsOver; i++)
            sim.Step();

        Assert.That(sim.IsOver, Is.True);
        Assert.That(sim.WinnerId, Is.EqualTo(1));
        Assert.That(sim.GetFaction(0)!.IsEliminated, Is.True);
        Assert.That(sim.Units.Any(u => u.FactionId == 0), Is.False, "the loser's army disbands");
        Assert.That(sim.Settlements[1].OwnerId, Is.EqualTo(Settlement.NeutralOwner), "the loser's other settlements turn neutral");
        var events = sim.TakeEvents();
        Assert.That(events.OfType<FactionEliminated>().Single().FactionId, Is.EqualTo(0));
        Assert.That(events.OfType<MatchEnded>().Single().FactionId, Is.EqualTo(1));

        int tick = sim.Tick;
        sim.Step();
        Assert.That(sim.Tick, Is.EqualTo(tick), "the world is frozen after the match ends");
    }

    [Test]
    public void EliminatedFactionCannotRecruit()
    {
        var sim = new Simulation(TestMaps.Parse(Field,
            "settlement castle 0 2 2", "settlement castle 1 17 8", "settlement castle 2 17 2",
            "unit dummy 1 2 2"), TestMaps.Rules);

        for (int i = 0; i < 6 * Simulation.TicksPerSecond; i++)
            sim.Step();

        Assert.That(sim.GetFaction(0)!.IsEliminated, Is.True);
        Assert.That(sim.IsOver, Is.False, "two contenders remain");
        Assert.That(sim.CanRecruit(0, sim.Settlements[0].Id, "infantry"), Is.EqualTo(RecruitError.NotOwner));
    }
}

public class RegionsTests
{
    [Test]
    public void RegionsFollowWalkingDistanceNotStraightLine()
    {
        // A sits right by the river; B is far away but on the same (east) bank as the cells next to A.
        var map = TestMaps.Parse(new[]
        {
            "......~.........",
            "......~.........",
            "......~.........",
            "......~.........",
            "......=.........",
        }, "settlement outpost -1 5 0", "settlement outpost -1 15 0");
        var sim = new Simulation(map, TestMaps.Rules);
        int a = sim.Settlements[0].Id, b = sim.Settlements[1].Id;

        Assert.That(sim.Regions.SettlementAt(new Cell(0, 0)), Is.EqualTo(a));
        Assert.That(sim.Regions.SettlementAt(new Cell(7, 0)), Is.EqualTo(b),
            "two tiles from A as the crow flies, but A is a long walk around the river");
        Assert.That(sim.Regions.SettlementAt(new Cell(6, 0)), Is.Not.EqualTo(Regions.None), "water gets a region for drawing");
    }

    [Test]
    public void EveryCellReachableFromASettlementHasARegion()
    {
        var sim = new Simulation(TestMaps.Parse(new[] { "....^....", "....^....", "........." },
            "settlement outpost -1 0 0", "settlement outpost -1 8 0"), TestMaps.Rules);

        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 9; x++)
                Assert.That(sim.Regions.SettlementAt(new Cell(x, y)), Is.Not.EqualTo(Regions.None), $"cell {x},{y}");
    }
}

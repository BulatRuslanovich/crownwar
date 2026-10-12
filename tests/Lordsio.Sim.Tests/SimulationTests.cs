using System.Numerics;
using Lordsio.Sim.Core;
using Lordsio.Sim.Units;
using Lordsio.Sim.World;

namespace Lordsio.Sim.Tests;

public class SimulationTests
{
    private static readonly string[] RiverMap =
    {
        "..........",
        "....~.....",
        "....~.....",
        "....~.~~~.",
        "....~.~.~.",
        "....~.~~~.",
    };

    private static Simulation CreateSim(params string[] objects) =>
        new(TestMaps.Parse(RiverMap, objects), TestMaps.Rules, MatchSetup.Sandbox);

    private static void RunSeconds(Simulation sim, float seconds)
    {
        for (int i = 0; i < seconds * Simulation.TicksPerSecond; i++)
            sim.Step();
    }

    [Test]
    public void UnitMovesToReachableTarget()
    {
        var sim = CreateSim("unit infantry 0 1 4");
        var unit = sim.Units[0];
        var target = new Vector2(9.5f, 5.5f);

        sim.Issue(new MoveCommand(0, new[] { unit.Id }, target));
        sim.Step();
        Assert.That(unit.LastOrder, Is.EqualTo(MoveOrderResult.Accepted));
        Assert.That(unit.State, Is.EqualTo(UnitState.Moving));

        RunSeconds(sim, 10f);
        Assert.That(unit.State, Is.EqualTo(UnitState.Idle));
        Assert.That(Vector2.Distance(unit.Position, target), Is.LessThan(1e-4f));
    }

    [Test]
    public void UnitNeverStandsOnBlockedTileWhileMoving()
    {
        var sim = CreateSim("unit infantry 0 1 4");
        var unit = sim.Units[0];
        sim.Issue(new MoveCommand(0, new[] { unit.Id }, new Vector2(5.5f, 5.5f)));

        for (int i = 0; i < 10 * Simulation.TicksPerSecond; i++)
        {
            sim.Step();
            Assert.That(sim.Map.IsWalkable(Cell.FromPosition(unit.Position)), Is.True, $"tick {sim.Tick}: {unit.Position}");
        }
        Assert.That(unit.State, Is.EqualTo(UnitState.Idle));
    }

    [Test]
    public void BlockedTargetIsAdjustedToNearestReachablePoint()
    {
        var sim = CreateSim("unit infantry 0 1 1");
        var unit = sim.Units[0];

        // (7,4) is a pocket enclosed by water.
        sim.Issue(new MoveCommand(0, new[] { unit.Id }, new Vector2(7.5f, 4.5f)));
        sim.Step();
        Assert.That(unit.LastOrder, Is.EqualTo(MoveOrderResult.Adjusted));

        RunSeconds(sim, 10f);
        Assert.That(unit.State, Is.EqualTo(UnitState.Idle));
        var cell = Cell.FromPosition(unit.Position);
        Assert.That(sim.Map.AreConnected(cell, new Cell(1, 1)), Is.True);
        // Closest shore cells are exactly two tiles away from the pocket.
        Assert.That(Vector2.Distance(cell.Center, new Vector2(7.5f, 4.5f)), Is.EqualTo(2f).Within(1e-4f));
    }

    [Test]
    public void TargetOutsideMapIsClampedToEdge()
    {
        var sim = CreateSim("unit infantry 0 1 1");
        var unit = sim.Units[0];

        sim.Issue(new MoveCommand(0, new[] { unit.Id }, new Vector2(-5f, 0.5f)));
        RunSeconds(sim, 5f);

        Assert.That(unit.LastOrder, Is.EqualTo(MoveOrderResult.Adjusted));
        Assert.That(Cell.FromPosition(unit.Position), Is.EqualTo(new Cell(0, 0)));
    }

    [Test]
    public void CannotCommandAnotherFactionsUnit()
    {
        var sim = CreateSim("unit infantry 1 1 1");
        var unit = sim.Units[0];
        var start = unit.Position;

        sim.Issue(new MoveCommand(0, new[] { unit.Id }, new Vector2(3.5f, 3.5f)));
        RunSeconds(sim, 2f);

        Assert.That(unit.Position, Is.EqualTo(start));
        Assert.That(unit.LastOrder, Is.EqualTo(MoveOrderResult.None));
    }

    [Test]
    public void UnknownUnitIdsAreIgnored()
    {
        var sim = CreateSim("unit infantry 0 1 1");
        sim.Issue(new MoveCommand(0, new[] { 999 }, new Vector2(3.5f, 3.5f)));

        Assert.DoesNotThrow(sim.Step);
    }

    [Test]
    public void SpeedMatchesUnitType()
    {
        var sim = CreateSim("unit infantry 0 0 0");
        var unit = sim.Units[0];
        sim.Issue(new MoveCommand(0, new[] { unit.Id }, new Vector2(9.5f, 0.5f)));

        RunSeconds(sim, 1f);

        // Commands apply at the start of a tick, so one second moves exactly speed × 1 s.
        Assert.That(unit.Position.X - 0.5f, Is.EqualTo(TestMaps.Rules.Units.Get("infantry").Speed).Within(1e-3f));
    }
}

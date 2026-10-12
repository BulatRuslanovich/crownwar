using System.Numerics;
using Lordsio.Sim.Core;
using Lordsio.Sim.Units;
using Lordsio.Sim.World;

namespace Lordsio.Sim.Tests;

public class GroupMovementTests
{
    /// <summary>A river crossed by a single one-tile bridge.</summary>
    private static readonly string[] BridgeMap =
    {
        "........~.........",
        "........~.........",
        "........~.........",
        "........~.........",
        "........=.........",
        "........~.........",
        "........~.........",
        "........~.........",
        "........~.........",
    };

    private static void RunSeconds(Simulation sim, float seconds, Action? afterTick = null)
    {
        for (int i = 0; i < seconds * Simulation.TicksPerSecond; i++)
        {
            sim.Step();
            afterTick?.Invoke();
        }
    }

    private static int[] AllIds(Simulation sim) => sim.Units.Select(u => u.Id).ToArray();

    [Test]
    public void MapSpawnPlacesGroupWithoutOverlap()
    {
        var sim = new Simulation(TestMaps.Parse(BridgeMap, "unit infantry 0 3 4 20"), TestMaps.Catalog);

        Assert.That(sim.Units, Has.Count.EqualTo(20));
        AssertNoOverlap(sim, tolerance: 1e-3f);
    }

    [Test]
    public void GroupCrossesOneTileBridgeAndSpreadsAtTarget()
    {
        var sim = new Simulation(TestMaps.Parse(BridgeMap, "unit infantry 0 3 4 20"), TestMaps.Catalog);
        var target = new Vector2(13.5f, 4.5f);

        sim.Issue(new MoveCommand(0, AllIds(sim), target));
        RunSeconds(sim, 40f, () =>
        {
            foreach (var unit in sim.Units)
                Assert.That(sim.Map.IsWalkable(Cell.FromPosition(unit.Position)), Is.True,
                    $"tick {sim.Tick}: unit {unit.Id} at {unit.Position} is on a blocked tile");
        });

        foreach (var unit in sim.Units)
        {
            Assert.That(unit.State, Is.EqualTo(UnitState.Idle), $"unit {unit.Id} still moving");
            Assert.That(unit.LastOrder, Is.EqualTo(MoveOrderResult.Accepted), $"unit {unit.Id}");
            Assert.That(unit.Position.X, Is.GreaterThan(9f), $"unit {unit.Id} did not cross");
            Assert.That(Vector2.Distance(unit.Position, target), Is.LessThan(4f), $"unit {unit.Id} too far from target");
        }
        AssertNoOverlap(sim, tolerance: 0.1f);
    }

    [Test]
    public void OverlappingUnitsArePushedApart()
    {
        var sim = new Simulation(TestMaps.Parse(BridgeMap), TestMaps.Catalog);
        var a = sim.SpawnUnit("infantry", 0, new Vector2(3.5f, 3.5f));
        var b = sim.SpawnUnit("infantry", 0, new Vector2(3.5f, 3.5f));

        RunSeconds(sim, 1f);

        Assert.That(Vector2.Distance(a.Position, b.Position), Is.EqualTo(0.6f).Within(0.01f));
    }

    [Test]
    public void UnitsAreKeptOffWallsAndMapEdge()
    {
        var sim = new Simulation(TestMaps.Parse(BridgeMap), TestMaps.Catalog);
        var edge = sim.SpawnUnit("infantry", 0, new Vector2(0.05f, 3.5f));
        var shore = sim.SpawnUnit("infantry", 0, new Vector2(7.9f, 1.5f));

        sim.Step();

        Assert.That(edge.Position.X, Is.EqualTo(0.3f).Within(1e-4f));
        Assert.That(shore.Position.X, Is.EqualTo(7.7f).Within(1e-4f));
    }

    [Test]
    public void DuplicateIdsInCommandAreHandledOnce()
    {
        var sim = new Simulation(TestMaps.Parse(BridgeMap, "unit infantry 0 3 4"), TestMaps.Catalog);
        int id = sim.Units[0].Id;
        var target = new Vector2(5.5f, 4.5f);

        sim.Issue(new MoveCommand(0, new[] { id, id, id }, target));
        RunSeconds(sim, 3f);

        Assert.That(Vector2.Distance(sim.Units[0].Position, target), Is.LessThan(1e-3f));
    }

    private static void AssertNoOverlap(Simulation sim, float tolerance)
    {
        var units = sim.Units;
        for (int i = 0; i < units.Count; i++)
        {
            for (int j = i + 1; j < units.Count; j++)
            {
                float minDist = units[i].Type.Radius + units[j].Type.Radius - tolerance;
                Assert.That(Vector2.Distance(units[i].Position, units[j].Position), Is.GreaterThanOrEqualTo(minDist),
                    $"units {units[i].Id} and {units[j].Id} overlap");
            }
        }
    }
}

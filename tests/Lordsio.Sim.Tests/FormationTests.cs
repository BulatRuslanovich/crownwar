using System.Numerics;
using Lordsio.Sim.Units;
using Lordsio.Sim.World;

namespace Lordsio.Sim.Tests;

public class FormationTests
{
    private const float Radius = 0.3f;

    [Test]
    public void SingleSlotIsTheTargetItself()
    {
        var grid = TestMaps.Parse(new[] { ".....", ".....", "....." }).Grid;
        var target = new Vector2(2.3f, 1.6f);

        Assert.That(Formation.ComputeSlots(grid, target, 1, Radius), Is.EqualTo(new[] { target }));
    }

    [Test]
    public void SlotsAreDistinctSpacedAndClearOfWalls()
    {
        var grid = TestMaps.Parse(new[]
        {
            "............",
            "....^^^.....",
            "....^.......",
            "............",
            "............",
            "............",
        }).Grid;
        var slots = Formation.ComputeSlots(grid, new Vector2(5.5f, 2.5f), 25, Radius);

        Assert.That(slots, Has.Count.EqualTo(25));
        foreach (var slot in slots)
            Assert.That(grid.HasClearance(slot, Radius), Is.True, $"slot {slot} touches a wall");
        for (int i = 0; i < slots.Count; i++)
            for (int j = i + 1; j < slots.Count; j++)
                Assert.That(Vector2.Distance(slots[i], slots[j]), Is.GreaterThanOrEqualTo(Formation.Spacing - 1e-4f));
    }

    [Test]
    public void SlotsStayOnTheTargetsSideOfARiver()
    {
        var grid = TestMaps.Parse(new[]
        {
            "...~......",
            "...~......",
            "...~......",
            "...~......",
            "...=......",
        }).Grid;
        // Target right next to the river: straight-line neighbours across the water are a long walk away.
        var slots = Formation.ComputeSlots(grid, new Vector2(4.5f, 1.5f), 12, Radius);

        Assert.That(slots.All(s => s.X >= 4f), Is.True, string.Join(", ", slots));
    }

    [Test]
    public void CrampedPocketReusesSlots()
    {
        var grid = TestMaps.Parse(new[] { "^^^", "^.^", "^^^" }).Grid;
        var slots = Formation.ComputeSlots(grid, new Vector2(1.5f, 1.5f), 3, Radius);

        Assert.That(slots, Has.Count.EqualTo(3));
        Assert.That(slots.All(s => Cell.FromPosition(s) == new Cell(1, 1)), Is.True);
    }
}

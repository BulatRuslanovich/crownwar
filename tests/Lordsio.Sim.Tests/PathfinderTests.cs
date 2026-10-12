using System.Numerics;
using Lordsio.Sim.World;

namespace Lordsio.Sim.Tests;

public class PathfinderTests
{
    [Test]
    public void StraightLineOnOpenGround()
    {
        var grid = TestMaps.Parse(new[] { "......" }).Grid;
        var path = new Pathfinder(grid).FindCellPath(new Cell(0, 0), new Cell(5, 0));

        Assert.That(path, Has.Count.EqualTo(6));
    }

    [Test]
    public void GoesAroundWall()
    {
        var grid = TestMaps.Parse(new[]
        {
            ".....",
            ".~~~.",
            ".~...",
            ".~...",
        }).Grid;
        var path = new Pathfinder(grid).FindCellPath(new Cell(2, 3), new Cell(0, 3));

        Assert.That(path, Is.Not.Null);
        Assert.That(path!.All(grid.IsWalkable), Is.True);
        Assert.That(path.First(), Is.EqualTo(new Cell(2, 3)));
        Assert.That(path.Last(), Is.EqualTo(new Cell(0, 3)));
        AssertStepsAreAdjacent(path);
    }

    [Test]
    public void DoesNotCutBetweenDiagonalBlockers()
    {
        // The only connection between the two '.' cells is a diagonal squeeze between blockers.
        var grid = TestMaps.Parse(new[]
        {
            ".^",
            "^.",
        }).Grid;

        Assert.That(new Pathfinder(grid).FindCellPath(new Cell(0, 0), new Cell(1, 1)), Is.Null);
    }

    [Test]
    public void DoesNotCutWallCorner()
    {
        var grid = TestMaps.Parse(new[]
        {
            "...",
            ".^.",
            "...",
        }).Grid;
        // The direct diagonal (1,2)->(2,1) would clip the mountain's corner, so the path takes two orthogonal steps.
        var path = new Pathfinder(grid).FindCellPath(new Cell(1, 2), new Cell(2, 1))!;

        Assert.That(path, Is.EqualTo(new[] { new Cell(1, 2), new Cell(2, 2), new Cell(2, 1) }));
    }

    [Test]
    public void UnreachableGoalReturnsNull()
    {
        var grid = TestMaps.Parse(new[]
        {
            "..~..",
            "..~..",
        }).Grid;

        Assert.That(new Pathfinder(grid).FindCellPath(new Cell(0, 0), new Cell(4, 0)), Is.Null);
    }

    [Test]
    public void SmoothedPathSkipsIntermediateCellsOnOpenGround()
    {
        var grid = TestMaps.Parse(new[] { "........", "........", "........" }).Grid;
        var path = new Pathfinder(grid).FindPath(new Vector2(0.5f, 0.5f), new Vector2(7.5f, 2.5f), 0.3f);

        Assert.That(path, Is.EqualTo(new[] { new Vector2(7.5f, 2.5f) }));
    }

    [Test]
    public void SmoothedPathKeepsCornerAroundObstacle()
    {
        var grid = TestMaps.Parse(new[]
        {
            ".....",
            ".~~~.",
            ".~...",
        }).Grid;
        var path = new Pathfinder(grid).FindPath(new Vector2(0.5f, 2.5f), new Vector2(3.5f, 2.5f), 0.3f)!;

        Assert.That(path, Has.Count.GreaterThan(1));
        Vector2 prev = new(0.5f, 2.5f);
        foreach (var p in path)
        {
            Assert.That(grid.IsCorridorClear(prev, p, 0f), Is.True, $"segment {prev} -> {p} crosses water");
            prev = p;
        }
        Assert.That(path.Last(), Is.EqualTo(new Vector2(3.5f, 2.5f)));
    }

    [Test]
    public void NearestReachableCellForBlockedTarget()
    {
        var grid = TestMaps.Parse(new[]
        {
            "....~~~",
            "....~.~",
            "....~~~",
        }).Grid;
        int mainland = grid.ComponentOf(new Cell(0, 0));

        // Target inside the enclosed pocket: nearest mainland cell is on the shore.
        Assert.That(grid.FindNearestInComponent(new Cell(5, 1), mainland, 10), Is.EqualTo(new Cell(3, 1)));
        // Target already reachable: unchanged.
        Assert.That(grid.FindNearestInComponent(new Cell(1, 1), mainland, 10), Is.EqualTo(new Cell(1, 1)));
    }

    private static void AssertStepsAreAdjacent(IReadOnlyList<Cell> path)
    {
        for (int i = 1; i < path.Count; i++)
        {
            int dx = Math.Abs(path[i].X - path[i - 1].X), dy = Math.Abs(path[i].Y - path[i - 1].Y);
            Assert.That(Math.Max(dx, dy), Is.EqualTo(1), $"step {i} is not adjacent");
        }
    }
}

using Lordsio.Sim.World;

namespace Lordsio.Sim.Tests;

public class MapLoaderTests
{
    [Test]
    public void ParsesTilesAndObjects()
    {
        var map = MapLoader.Parse("""
            # comment
            [tiles]
            .f=
            ~^.

            [objects]
            unit infantry 0 2 1
            """);

        Assert.Multiple(() =>
        {
            Assert.That(map.Grid.Width, Is.EqualTo(3));
            Assert.That(map.Grid.Height, Is.EqualTo(2));
            Assert.That(map.Grid.GetTerrain(new Cell(1, 0)), Is.EqualTo(Terrain.Forest));
            Assert.That(map.Grid.GetTerrain(new Cell(2, 0)), Is.EqualTo(Terrain.Road));
            Assert.That(map.Grid.GetTerrain(new Cell(0, 1)), Is.EqualTo(Terrain.Water));
            Assert.That(map.Grid.IsWalkable(new Cell(1, 1)), Is.False);
            Assert.That(map.UnitSpawns, Is.EqualTo(new[] { new UnitSpawn("infantry", 0, new Cell(2, 1)) }));
        });
    }

    [Test]
    public void HandlesWindowsLineEndings()
    {
        var map = MapLoader.Parse("[tiles]\r\n..\r\n..\r\n");
        Assert.That(map.Grid.Height, Is.EqualTo(2));
    }

    [TestCase("[tiles]\n...\n..")]
    [TestCase("[tiles]\n.x.")]
    [TestCase("...")]
    [TestCase("[tiles]\n...\n[objects]\nunit infantry 0 9 9")]
    [TestCase("[tiles]\n.~.\n[objects]\nunit infantry 0 1 0")]
    [TestCase("[tiles]\n...\n[objects]\ncastle 1 1")]
    [TestCase("[weather]\nrain")]
    public void RejectsMalformedMaps(string text)
    {
        Assert.Throws<FormatException>(() => MapLoader.Parse(text));
    }

    [Test]
    public void ComponentsSeparateEnclosedAreas()
    {
        var map = TestMaps.Parse(new[]
        {
            "..^...",
            "..^.^.",
            "..^^^.",
        });
        var grid = map.Grid;
        Assert.Multiple(() =>
        {
            Assert.That(grid.AreConnected(new Cell(0, 0), new Cell(1, 2)), Is.True);
            Assert.That(grid.AreConnected(new Cell(0, 0), new Cell(3, 0)), Is.False);
            Assert.That(grid.AreConnected(new Cell(3, 1), new Cell(5, 2)), Is.True);
            Assert.That(grid.ComponentOf(new Cell(2, 0)), Is.EqualTo(-1));
        });
    }

    [Test]
    public void DefaultGameDataLoads()
    {
        var map = TestSim.DefaultMap();
        var rules = TestSim.RealRules();

        Assert.That(map.StartPositions, Has.Count.GreaterThanOrEqualTo(4));
        Assert.That(map.Deposits, Is.Not.Empty);

        // Every start must fit a capital, whatever faction count and shuffle the match uses.
        var sim = new Core.Simulation(map, rules, new Core.MatchSetup(map.StartPositions.Count, ShuffleStarts: false));
        Assert.That(sim.Buildings.Count(b => b.Type.IsCapital), Is.EqualTo(map.StartPositions.Count));
        Assert.That(sim.Units.Count(u => u.FactionId == 0), Is.EqualTo(rules.StartingUnits.Sum(s => s.Count)));
    }

    [Test]
    public void ParsesStartsAndDeposits()
    {
        var map = MapLoader.Parse("[tiles]\n.....\n.....\n[objects]\nstart 0 0\ngold 3 0 5\ngold 1 0");

        Assert.That(map.StartPositions, Is.EqualTo(new[] { new Cell(0, 0) }));
        Assert.That(map.Deposits, Is.EqualTo(new[]
        {
            new DepositSpawn(new Cell(3, 0), 5),
            new DepositSpawn(new Cell(1, 0), MapLoader.DefaultDepositYield),
        }));
    }

    [TestCase("[tiles]\n.~\n..\n[objects]\ngold 0 0", Description = "deposit on water")]
    [TestCase("[tiles]\n.=\n..\n[objects]\ngold 0 0", Description = "deposit on a road")]
    [TestCase("[tiles]\n...\n...\n[objects]\ngold 1 1", Description = "deposit off the map edge")]
    [TestCase("[tiles]\n...\n...\n[objects]\ngold 0 0\ngold 1 0", Description = "overlapping deposits")]
    [TestCase("[tiles]\n..\n..\n[objects]\ngold 0 0 0", Description = "zero yield")]
    public void RejectsBadDeposits(string text)
    {
        Assert.Throws<FormatException>(() => MapLoader.Parse(text));
    }
}

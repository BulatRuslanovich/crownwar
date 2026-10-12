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
        string root = TestMaps.ProjectRoot();
        var map = MapLoader.Parse(File.ReadAllText(Path.Combine(root, "Maps", "default.txt")));
        var rules = Core.GameRules.Parse(File.ReadAllText(Path.Combine(root, "Data", "rules.json")));

        // Creating the simulation checks every unit and settlement type id against the rules.
        var sim = new Core.Simulation(map, rules);
        Assert.That(sim.Units, Is.Not.Empty);
        Assert.That(sim.Settlements.Any(s => s.OwnerId == 0 && s.Type.Recruits.Count > 0), Is.True,
            "the player needs a settlement to recruit in");
    }

    [Test]
    public void ParsesSettlements()
    {
        var map = MapLoader.Parse("[tiles]\n....\n[objects]\nsettlement castle 0 1 0\nsettlement village -1 3 0");

        Assert.That(map.SettlementSpawns, Is.EqualTo(new[]
        {
            new SettlementSpawn("castle", 0, new Cell(1, 0)),
            new SettlementSpawn("village", -1, new Cell(3, 0)),
        }));
    }

    [Test]
    public void RejectsSettlementOnBlockedTile()
    {
        Assert.Throws<FormatException>(() => MapLoader.Parse("[tiles]\n.~\n[objects]\nsettlement castle 0 1 0"));
    }
}

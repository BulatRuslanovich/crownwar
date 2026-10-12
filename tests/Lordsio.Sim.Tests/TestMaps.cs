using Lordsio.Sim.Core;
using Lordsio.Sim.Territory;
using Lordsio.Sim.Units;
using Lordsio.Sim.World;

namespace Lordsio.Sim.Tests;

internal static class TestMaps
{
    /// <summary>Small round numbers so test expectations are easy to read.</summary>
    public static readonly GameRules Rules = new(
        startingGold: 100,
        new UnitCatalog(new[] { new UnitType("infantry", "Infantry", Speed: 4f, Radius: 0.3f, Cost: 50, BuildTime: 2f) }),
        new[]
        {
            new SettlementType("castle", "Castle", Income: 10, ArmyCap: 5, Recruits: new[] { "infantry" }),
            new SettlementType("outpost", "Outpost", Income: 3, ArmyCap: 0, Recruits: Array.Empty<string>()),
        });

    /// <summary>Builds a map from tile rows; appends an [objects] section if given.</summary>
    public static MapDefinition Parse(string[] rows, params string[] objects)
    {
        var text = "[tiles]\n" + string.Join("\n", rows);
        if (objects.Length > 0)
            text += "\n[objects]\n" + string.Join("\n", objects);
        return MapLoader.Parse(text);
    }

    /// <summary>Walks up from the test output folder to the Godot project root.</summary>
    public static string ProjectRoot()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "project.godot")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("project.godot not found above the test directory.");
    }
}

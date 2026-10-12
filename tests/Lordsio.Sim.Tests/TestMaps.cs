using Lordsio.Sim.Units;
using Lordsio.Sim.World;

namespace Lordsio.Sim.Tests;

internal static class TestMaps
{
    public static readonly UnitCatalog Catalog = new(new[] { new UnitType("infantry", "Infantry", 4f, 0.3f) });

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

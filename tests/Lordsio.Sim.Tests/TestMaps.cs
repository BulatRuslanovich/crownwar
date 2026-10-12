using Lordsio.Sim.Core;
using Lordsio.Sim.Territory;
using Lordsio.Sim.Units;
using Lordsio.Sim.World;

namespace Lordsio.Sim.Tests;

internal static class TestMaps
{
    /// <summary>Small round numbers so test expectations are easy to read.</summary>
    public static readonly UnitType Infantry = new("infantry", "Infantry", Speed: 4f, Radius: 0.3f, Cost: 50, BuildTime: 2f,
        MaxHealth: 100, Damage: 10, AttackRange: 0.2f, AttackCooldown: 1f, SightRange: 5f);

    public static readonly UnitType Archer = new("archer", "Archer", Speed: 4f, Radius: 0.3f, Cost: 60, BuildTime: 2f,
        MaxHealth: 50, Damage: 10, AttackRange: 4f, AttackCooldown: 1f, SightRange: 5f, ProjectileSpeed: 10f);

    public static readonly UnitType Heavy = new("heavy", "Heavy", Speed: 3f, Radius: 0.3f, Cost: 100, BuildTime: 2f,
        MaxHealth: 200, Damage: 20, AttackRange: 0.2f, AttackCooldown: 1f, SightRange: 5f, RangedArmor: 0.5f);

    /// <summary>A target dummy that never fights back (no damage, can't see).</summary>
    public static readonly UnitType Dummy = new("dummy", "Dummy", Speed: 1f, Radius: 0.3f, Cost: 0, BuildTime: 1f,
        MaxHealth: 100, Damage: 0, AttackRange: 0f, AttackCooldown: 1f, SightRange: 0f);

    /// <summary>Small round numbers so test expectations are easy to read.</summary>
    public static readonly GameRules Rules = new(
        startingGold: 100,
        captureRadius: 2f,
        new UnitCatalog(new[] { Infantry, Archer, Heavy, Dummy }),
        new[]
        {
            new SettlementType("castle", "Castle", Income: 10, ArmyCap: 5, Recruits: new[] { "infantry" },
                CaptureTime: 4f, Garrison: Array.Empty<GarrisonEntry>(), IsCapital: true),
            new SettlementType("outpost", "Outpost", Income: 3, ArmyCap: 0, Recruits: Array.Empty<string>(),
                CaptureTime: 2f, Garrison: Array.Empty<GarrisonEntry>()),
            new SettlementType("village", "Village", Income: 2, ArmyCap: 2, Recruits: Array.Empty<string>(),
                CaptureTime: 2f, Garrison: new[] { new GarrisonEntry("dummy", 1) }),
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

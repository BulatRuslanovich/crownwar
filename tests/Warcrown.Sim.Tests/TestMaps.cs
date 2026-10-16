using Warcrown.Sim.Buildings;
using Warcrown.Sim.Core;
using Warcrown.Sim.Units;
using Warcrown.Sim.World;

namespace Warcrown.Sim.Tests;

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

    public static readonly BuildingType Castle = new("castle", "Castle", Size: 2, Cost: 0, BuildTime: 0f, MaxHealth: 200,
        RangedArmor: 0f, Income: 10, ArmyCap: 5, Recruits: new[] { "infantry" }, IsCapital: true);

    public static readonly BuildingType Mine = new("mine", "Mine", Size: 2, Cost: 50, BuildTime: 2f, MaxHealth: 100,
        RangedArmor: 0f, Income: 0, ArmyCap: 0, Recruits: Array.Empty<string>(), IsMine: true);

    public static readonly BuildingType Barracks = new("barracks", "Barracks", Size: 2, Cost: 80, BuildTime: 2f, MaxHealth: 150,
        RangedArmor: 0f, Income: 0, ArmyCap: 5, Recruits: new[] { "infantry" });

    public static readonly BuildingType Tower = new("tower", "Tower", Size: 1, Cost: 60, BuildTime: 2f, MaxHealth: 100,
        RangedArmor: 0.5f, Income: 0, ArmyCap: 0, Recruits: Array.Empty<string>(),
        Attack: new BuildingAttack(Damage: 10, Range: 4f, Cooldown: 1f, ProjectileSpeed: 10f));

    public static readonly GameRules Rules = new(
        startingGold: 100,
        buildRadius: 4f,
        startingUnits: Array.Empty<StartingUnits>(),
        new UnitCatalog(new[] { Infantry, Archer, Heavy, Dummy }),
        new[] { Castle, Mine, Barracks, Tower });

    /// <summary>Builds a map from tile rows; appends an [objects] section if given.</summary>
    public static MapDefinition Parse(string[] rows, params string[] objects)
    {
        var text = "[tiles]\n" + string.Join("\n", rows);
        if (objects.Length > 0)
            text += "\n[objects]\n" + string.Join("\n", objects);
        return MapLoader.Parse(text);
    }

    /// <summary>A match without capitals: only the units placed by the map's objects.</summary>
    public static Simulation Sandbox(string[] rows, params string[] objects) =>
        new(Parse(rows, objects), Rules, MatchSetup.Sandbox);

    /// <summary>A match where faction i owns a capital at the i-th "start" object.</summary>
    public static Simulation WithCapitals(int factions, string[] rows, params string[] objects) =>
        new(Parse(rows, objects), Rules, new MatchSetup(factions, ShuffleStarts: false));

    public static string[] Field(int width, int height) => Enumerable.Repeat(new string('.', width), height).ToArray();

    /// <summary>Walks up from the test output folder to the Godot project root.</summary>
    public static string ProjectRoot()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "project.godot")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("project.godot not found above the test directory.");
    }
}

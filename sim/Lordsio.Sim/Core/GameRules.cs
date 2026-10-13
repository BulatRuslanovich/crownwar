using System.Text.Json;
using Lordsio.Sim.Buildings;
using Lordsio.Sim.Units;

namespace Lordsio.Sim.Core;

public sealed record StartingUnits(string UnitTypeId, int Count);

/// <summary>All tunable game data, loaded from Data/rules.json.</summary>
public sealed class GameRules
{
    private readonly Dictionary<string, BuildingType> _buildingTypes;

    public GameRules(int startingGold, float buildRadius, IReadOnlyList<StartingUnits> startingUnits,
        UnitCatalog units, IEnumerable<BuildingType> buildingTypes,
        float depositReserveSeconds = 0f, float depositRegenSeconds = 0f, int upkeepAllowance = 0)
    {
        if (startingGold < 0)
            throw new ArgumentOutOfRangeException(nameof(startingGold));
        if (buildRadius <= 0f)
            throw new ArgumentOutOfRangeException(nameof(buildRadius));
        if (depositReserveSeconds < 0f || depositRegenSeconds < 0f)
            throw new ArgumentOutOfRangeException(nameof(depositReserveSeconds), "Deposit timings cannot be negative.");
        if (upkeepAllowance < 0)
            throw new ArgumentOutOfRangeException(nameof(upkeepAllowance));
        StartingGold = startingGold;
        DepositReserveSeconds = depositReserveSeconds;
        DepositRegenSeconds = depositRegenSeconds;
        UpkeepAllowance = upkeepAllowance;
        BuildRadius = buildRadius;
        StartingUnits = startingUnits;
        Units = units;

        foreach (var start in startingUnits)
        {
            if (!units.Contains(start.UnitTypeId))
                throw new ArgumentException($"Starting units refer to unknown unit type '{start.UnitTypeId}'.");
        }

        _buildingTypes = new Dictionary<string, BuildingType>();
        foreach (var type in buildingTypes)
        {
            foreach (string unitId in type.Recruits)
            {
                if (!units.Contains(unitId))
                    throw new ArgumentException($"Building type '{type.Id}' recruits unknown unit type '{unitId}'.");
            }
            if (type.Size < 1 || type.MaxHealth <= 0 || type.Cost < 0 || type.BuildTime < 0f)
                throw new ArgumentException($"Building type '{type.Id}': size and health must be positive, cost and build time non-negative.");
            if (type.RangedArmor is < 0f or > 1f)
                throw new ArgumentException($"Building type '{type.Id}': ranged armor must be in [0, 1].");
            if (type.IsMine && type.Size != Lordsio.Sim.World.GoldDeposit.Size)
                throw new ArgumentException($"Mine type '{type.Id}' must match the deposit size {Lordsio.Sim.World.GoldDeposit.Size}.");
            if (!_buildingTypes.TryAdd(type.Id, type))
                throw new ArgumentException($"Duplicate building type id '{type.Id}'.");
        }
        Capital = _buildingTypes.Values.SingleOrDefault(t => t.IsCapital)
                  ?? throw new ArgumentException("Exactly one building type must be the capital.");
    }

    public int StartingGold { get; }

    /// <summary>
    /// Seconds of mining a deposit holds (its reserve is yield × this); 0 means deposits never run out.
    /// </summary>
    public float DepositReserveSeconds { get; }

    /// <summary>Seconds an exhausted deposit needs to refill completely; its mine earns nothing meanwhile.</summary>
    public float DepositRegenSeconds { get; }

    /// <summary>Army upkeep (gold per minute) each faction gets for free: a small garrison costs nothing.</summary>
    public int UpkeepAllowance { get; }

    /// <summary>A building may only be placed this close (tiles, to its center) to one of the builder's units.</summary>
    public float BuildRadius { get; }

    /// <summary>Army each faction starts with next to its capital.</summary>
    public IReadOnlyList<StartingUnits> StartingUnits { get; }

    public UnitCatalog Units { get; }

    /// <summary>The building type every faction starts with; losing it eliminates the faction.</summary>
    public BuildingType Capital { get; }

    public IEnumerable<BuildingType> BuildingTypes => _buildingTypes.Values;

    public bool HasBuildingType(string id) => _buildingTypes.ContainsKey(id);

    public BuildingType GetBuildingType(string id) =>
        _buildingTypes.TryGetValue(id, out var type)
            ? type
            : throw new KeyNotFoundException($"Unknown building type '{id}'.");

    /// <summary>
    /// Parses the rules file. Read through JsonDocument rather than reflection-based deserialization:
    /// Godot reloads game assemblies in the editor, and the reflection serializer's type caches can block
    /// that unload. Keep the file strict JSON without comments: the Godot editor imports *.json files
    /// and warns on anything non-standard.
    /// </summary>
    public static GameRules Parse(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true });
        var root = doc.RootElement;

        var units = new List<UnitType>();
        foreach (var u in root.GetProperty("units").EnumerateArray())
        {
            units.Add(new UnitType(
                Id: RequiredString(u, "id"),
                Name: RequiredString(u, "name"),
                Speed: u.GetProperty("speed").GetSingle(),
                Radius: u.GetProperty("radius").GetSingle(),
                Cost: u.GetProperty("cost").GetInt32(),
                BuildTime: u.GetProperty("buildTime").GetSingle(),
                MaxHealth: u.GetProperty("health").GetInt32(),
                Damage: u.GetProperty("damage").GetInt32(),
                AttackRange: u.GetProperty("attackRange").GetSingle(),
                AttackCooldown: u.GetProperty("attackCooldown").GetSingle(),
                SightRange: u.GetProperty("sightRange").GetSingle(),
                ProjectileSpeed: OptionalSingle(u, "projectileSpeed"),
                RangedArmor: OptionalSingle(u, "rangedArmor"),
                Upkeep: OptionalInt(u, "upkeep")));
        }

        var buildings = new List<BuildingType>();
        foreach (var b in root.GetProperty("buildings").EnumerateArray())
        {
            BuildingAttack? attack = null;
            if (b.TryGetProperty("attack", out var a))
            {
                attack = new BuildingAttack(
                    a.GetProperty("damage").GetInt32(),
                    a.GetProperty("range").GetSingle(),
                    a.GetProperty("cooldown").GetSingle(),
                    a.GetProperty("projectileSpeed").GetSingle());
            }

            buildings.Add(new BuildingType(
                Id: RequiredString(b, "id"),
                Name: RequiredString(b, "name"),
                Size: b.GetProperty("size").GetInt32(),
                Cost: b.GetProperty("cost").GetInt32(),
                BuildTime: b.GetProperty("buildTime").GetSingle(),
                MaxHealth: b.GetProperty("health").GetInt32(),
                RangedArmor: OptionalSingle(b, "rangedArmor"),
                Income: OptionalInt(b, "income"),
                ArmyCap: OptionalInt(b, "armyCap"),
                Recruits: b.TryGetProperty("recruits", out var r)
                    ? r.EnumerateArray().Select(e => e.GetString() ?? "").ToArray()
                    : Array.Empty<string>(),
                IsCapital: OptionalBool(b, "capital"),
                IsMine: OptionalBool(b, "mine"),
                Attack: attack));
        }

        var startingUnits = root.GetProperty("startingUnits").EnumerateArray()
            .Select(s => new StartingUnits(RequiredString(s, "unit"), s.GetProperty("count").GetInt32()))
            .ToArray();

        return new GameRules(
            root.GetProperty("startingGold").GetInt32(),
            root.GetProperty("buildRadius").GetSingle(),
            startingUnits,
            new UnitCatalog(units),
            buildings,
            OptionalSingle(root, "depositReserveSeconds"),
            OptionalSingle(root, "depositRegenSeconds"),
            OptionalInt(root, "upkeepAllowance"));
    }

    private static float OptionalSingle(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.GetSingle() : 0f;

    private static int OptionalInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.GetInt32() : 0;

    private static bool OptionalBool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.GetBoolean();

    private static string RequiredString(JsonElement element, string name) =>
        element.GetProperty(name).GetString() is { Length: > 0 } s
            ? s
            : throw new FormatException($"Missing '{name}' in rules entry {element}.");
}

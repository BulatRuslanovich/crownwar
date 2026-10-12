using System.Text.Json;
using Lordsio.Sim.Territory;
using Lordsio.Sim.Units;

namespace Lordsio.Sim.Core;

/// <summary>All tunable game data, loaded from Data/rules.json.</summary>
public sealed class GameRules
{
    private readonly Dictionary<string, SettlementType> _settlementTypes;

    public GameRules(int startingGold, float captureRadius, UnitCatalog units, IEnumerable<SettlementType> settlementTypes)
    {
        if (startingGold < 0)
            throw new ArgumentOutOfRangeException(nameof(startingGold));
        if (captureRadius <= 0f)
            throw new ArgumentOutOfRangeException(nameof(captureRadius));
        StartingGold = startingGold;
        CaptureRadius = captureRadius;
        Units = units;
        _settlementTypes = new Dictionary<string, SettlementType>();
        foreach (var type in settlementTypes)
        {
            foreach (string unitId in type.Recruits.Concat(type.Garrison.Select(g => g.UnitTypeId)))
            {
                if (!units.Contains(unitId))
                    throw new ArgumentException($"Settlement type '{type.Id}' refers to unknown unit type '{unitId}'.");
            }
            if (type.CaptureTime <= 0f)
                throw new ArgumentException($"Settlement type '{type.Id}': capture time must be > 0.");
            if (!_settlementTypes.TryAdd(type.Id, type))
                throw new ArgumentException($"Duplicate settlement type id '{type.Id}'.");
        }
    }

    public int StartingGold { get; }

    /// <summary>Units within this distance (tiles) of a settlement's center take part in capturing it.</summary>
    public float CaptureRadius { get; }

    public UnitCatalog Units { get; }

    public SettlementType GetSettlementType(string id) =>
        _settlementTypes.TryGetValue(id, out var type)
            ? type
            : throw new KeyNotFoundException($"Unknown settlement type '{id}'.");

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
                RangedArmor: OptionalSingle(u, "rangedArmor")));
        }

        var settlements = new List<SettlementType>();
        foreach (var s in root.GetProperty("settlements").EnumerateArray())
        {
            var garrison = new List<GarrisonEntry>();
            if (s.TryGetProperty("garrison", out var g))
            {
                foreach (var entry in g.EnumerateArray())
                    garrison.Add(new GarrisonEntry(RequiredString(entry, "unit"), entry.GetProperty("count").GetInt32()));
            }

            settlements.Add(new SettlementType(
                Id: RequiredString(s, "id"),
                Name: RequiredString(s, "name"),
                Income: s.GetProperty("income").GetInt32(),
                ArmyCap: s.GetProperty("armyCap").GetInt32(),
                Recruits: s.GetProperty("recruits").EnumerateArray().Select(e => e.GetString() ?? "").ToArray(),
                CaptureTime: s.GetProperty("captureTime").GetSingle(),
                Garrison: garrison,
                IsCapital: s.TryGetProperty("capital", out var capital) && capital.GetBoolean()));
        }

        return new GameRules(
            root.GetProperty("startingGold").GetInt32(),
            root.GetProperty("captureRadius").GetSingle(),
            new UnitCatalog(units),
            settlements);
    }

    private static float OptionalSingle(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.GetSingle() : 0f;

    private static string RequiredString(JsonElement element, string name) =>
        element.GetProperty(name).GetString() is { Length: > 0 } s
            ? s
            : throw new FormatException($"Missing '{name}' in rules entry {element}.");
}

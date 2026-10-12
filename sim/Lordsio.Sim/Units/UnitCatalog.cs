using System.Text.Json;

namespace Lordsio.Sim.Units;

/// <summary>Static stats of a unit kind. Speed is in tiles per second, radius in tiles.</summary>
public sealed record UnitType(string Id, string Name, float Speed, float Radius);

public sealed class UnitCatalog
{
    private readonly Dictionary<string, UnitType> _types;

    public UnitCatalog(IEnumerable<UnitType> types)
    {
        _types = new Dictionary<string, UnitType>();
        foreach (var type in types)
        {
            if (type.Speed <= 0f || type.Radius <= 0f || type.Radius >= 0.5f)
                throw new ArgumentException($"Unit type '{type.Id}': speed must be > 0 and radius in (0, 0.5).");
            if (!_types.TryAdd(type.Id, type))
                throw new ArgumentException($"Duplicate unit type id '{type.Id}'.");
        }
    }

    public IEnumerable<UnitType> All => _types.Values;

    public UnitType Get(string id) =>
        _types.TryGetValue(id, out var type) ? type : throw new KeyNotFoundException($"Unknown unit type '{id}'.");

    /// <summary>
    /// Parses <c>{ "units": [ { "id", "name", "speed", "radius" }, ... ] }</c>
    /// (speed in tiles per second, radius in tiles). Keep the file strict JSON without comments:
    /// the Godot editor imports *.json files and warns on anything non-standard.
    /// Read through JsonDocument rather than reflection-based deserialization: Godot reloads game
    /// assemblies in the editor, and the reflection serializer's type caches can block that unload.
    /// </summary>
    public static UnitCatalog Parse(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });

        var types = new List<UnitType>();
        foreach (var item in doc.RootElement.GetProperty("units").EnumerateArray())
        {
            types.Add(new UnitType(
                Id: item.GetProperty("id").GetString() ?? throw new FormatException("Unit type without id."),
                Name: item.GetProperty("name").GetString() ?? "",
                Speed: item.GetProperty("speed").GetSingle(),
                Radius: item.GetProperty("radius").GetSingle()));
        }
        return new UnitCatalog(types);
    }
}

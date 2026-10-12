namespace Lordsio.Sim.Units;

/// <summary>
/// Static stats of a unit kind. Speed is in tiles per second, radius in tiles,
/// cost in gold and build time in seconds of production at a settlement.
/// </summary>
public sealed record UnitType(string Id, string Name, float Speed, float Radius, int Cost, float BuildTime);

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
            if (type.Cost < 0 || type.BuildTime < 0f)
                throw new ArgumentException($"Unit type '{type.Id}': cost and build time cannot be negative.");
            if (!_types.TryAdd(type.Id, type))
                throw new ArgumentException($"Duplicate unit type id '{type.Id}'.");
        }
    }

    public IEnumerable<UnitType> All => _types.Values;

    public bool Contains(string id) => _types.ContainsKey(id);

    public UnitType Get(string id) =>
        _types.TryGetValue(id, out var type) ? type : throw new KeyNotFoundException($"Unknown unit type '{id}'.");
}

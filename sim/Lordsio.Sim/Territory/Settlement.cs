using Lordsio.Sim.Units;
using Lordsio.Sim.World;

namespace Lordsio.Sim.Territory;

public sealed record GarrisonEntry(string UnitTypeId, int Count);

/// <summary>
/// Static stats of a settlement kind. Income is gold per second, capture time is seconds of
/// uncontested presence by a single unit. A faction that loses its last capital is eliminated.
/// Neutral settlements start with their garrison standing around them.
/// </summary>
public sealed record SettlementType(
    string Id,
    string Name,
    int Income,
    int ArmyCap,
    IReadOnlyList<string> Recruits,
    float CaptureTime,
    IReadOnlyList<GarrisonEntry> Garrison,
    bool IsCapital = false);

public sealed class ProductionItem
{
    internal ProductionItem(UnitType type, int totalTicks)
    {
        Type = type;
        TotalTicks = totalTicks;
        TicksLeft = totalTicks;
    }

    public UnitType Type { get; }
    public int TotalTicks { get; }
    public int TicksLeft { get; internal set; }
    public float Progress => TotalTicks == 0 ? 1f : 1f - (float)TicksLeft / TotalTicks;
}

public sealed class Settlement
{
    public const int NeutralOwner = -1;
    public const int MaxQueueLength = 5;

    private readonly List<ProductionItem> _queue = new();

    internal Settlement(int id, SettlementType type, Cell cell, int ownerId)
    {
        Id = id;
        Type = type;
        Cell = cell;
        OwnerId = ownerId;
    }

    public int Id { get; }
    public SettlementType Type { get; }
    public Cell Cell { get; }
    public int OwnerId { get; internal set; }

    /// <summary>Faction currently capturing the settlement, or <see cref="NeutralOwner"/> when nobody is.</summary>
    public int CapturingFactionId { get; internal set; } = NeutralOwner;

    /// <summary>Capture progress of <see cref="CapturingFactionId"/>, 0..1.</summary>
    public float CaptureProgress { get; internal set; }

    /// <summary>Several factions are in the capture zone, so progress is frozen.</summary>
    public bool IsContested { get; internal set; }

    /// <summary>Units being trained; only the first one makes progress.</summary>
    public IReadOnlyList<ProductionItem> Queue => _queue;

    internal void Enqueue(ProductionItem item) => _queue.Add(item);

    /// <summary>Training paid for by the previous owner is lost with the settlement.</summary>
    internal void ClearQueue() => _queue.Clear();

    /// <summary>Advances production by one tick and returns the unit type that finished, if any.</summary>
    internal UnitType? AdvanceProduction()
    {
        if (_queue.Count == 0)
            return null;
        var current = _queue[0];
        if (--current.TicksLeft > 0)
            return null;
        _queue.RemoveAt(0);
        return current.Type;
    }
}

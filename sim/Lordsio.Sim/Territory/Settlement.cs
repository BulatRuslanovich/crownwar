using Lordsio.Sim.Units;
using Lordsio.Sim.World;

namespace Lordsio.Sim.Territory;

/// <summary>Static stats of a settlement kind. Income is gold per second.</summary>
public sealed record SettlementType(string Id, string Name, int Income, int ArmyCap, IReadOnlyList<string> Recruits);

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

    /// <summary>Units being trained; only the first one makes progress.</summary>
    public IReadOnlyList<ProductionItem> Queue => _queue;

    internal void Enqueue(ProductionItem item) => _queue.Add(item);

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

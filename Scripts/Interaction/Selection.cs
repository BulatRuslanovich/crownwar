using System.Collections.Generic;

namespace Lordsio.Interaction;

/// <summary>
/// What the local player has selected: either a set of units or one settlement.
/// Pure client-side state: the simulation never sees it.
/// </summary>
public sealed class Selection
{
    private readonly HashSet<int> _ids = new();

    public IReadOnlyCollection<int> Ids => _ids;
    public int Count => _ids.Count;
    public int? SettlementId { get; private set; }

    public bool Contains(int unitId) => _ids.Contains(unitId);

    public void Select(int unitId)
    {
        Clear();
        _ids.Add(unitId);
    }

    /// <summary>Replaces the selection, or adds to it when <paramref name="additive"/> is set.</summary>
    public void Select(IEnumerable<int> unitIds, bool additive)
    {
        if (!additive)
            _ids.Clear();
        SettlementId = null;
        _ids.UnionWith(unitIds);
    }

    public void Toggle(int unitId)
    {
        SettlementId = null;
        if (!_ids.Remove(unitId))
            _ids.Add(unitId);
    }

    public void SelectSettlement(int settlementId)
    {
        _ids.Clear();
        SettlementId = settlementId;
    }

    public void Clear()
    {
        _ids.Clear();
        SettlementId = null;
    }
}

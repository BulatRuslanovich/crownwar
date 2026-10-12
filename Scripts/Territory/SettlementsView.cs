using System.Collections.Generic;
using Godot;
using Lordsio.Core;

namespace Lordsio.Territory;

/// <summary>One <see cref="SettlementView"/> per settlement; keeps their selection highlight in sync.</summary>
public partial class SettlementsView : Node2D
{
    private readonly Dictionary<int, SettlementView> _views = new();
    private MatchController _match = null!;

    public void Initialize(MatchController match)
    {
        _match = match;
        foreach (var settlement in match.Sim.Settlements)
        {
            var view = new SettlementView { Name = $"Settlement{settlement.Id}" };
            view.Initialize(settlement);
            _views.Add(settlement.Id, view);
            AddChild(view);
        }
    }

    public override void _Process(double delta)
    {
        foreach (var (id, view) in _views)
            view.Selected = _match.Selection.SettlementId == id;
    }
}

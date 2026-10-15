using System.Linq;
using Godot;
using Lordsio.Core;

namespace Lordsio.UI;

/// <summary>The selected troops: how many of each kind and how healthy they are.</summary>
public partial class Hud
{
    /// <summary>
    /// Selected troops: how many of each type (a click keeps only that type) and how healthy they are.
    /// Hidden while a building is selected, whose panel takes the same place.
    /// </summary>
    private void UpdateSelectionPanel()
    {
        var units = _match.Selection.Ids.Select(id => _match.Sim.GetUnit(id)).OfType<Sim.Units.Unit>().ToList();
        _selectionPanel.Visible = units.Count > 0 && _match.Selection.BuildingId == null;
        if (!_selectionPanel.Visible)
        {
            _shownComposition = "";
            return;
        }
        _selectionTitle.Text = string.Format(Tr("HUD_SELECTED"), units.Count);
        _selectionHealth.Value = 100.0 * units.Sum(u => u.Health) / System.Math.Max(1, units.Sum(u => u.MaxHealth));

        var groups = units.GroupBy(u => u.Type.Id).OrderBy(g => g.Key).Select(g => (Id: g.Key, Count: g.Count())).ToList();
        string composition = string.Join(",", groups.Select(g => $"{g.Id}:{g.Count}"));
        if (composition == _shownComposition)
            return;
        _shownComposition = composition;
        foreach (var child in _selectionTypes.GetChildren())
            child.QueueFree();
        foreach (var (id, count) in groups)
        {
            var type = _match.Sim.Rules.Units.Get(id);
            var button = new Button
            {
                Text = $"×{count}",
                Icon = Icons.Unit(id, FactionColors.Get(_match.LocalFactionId)),
                FocusMode = Control.FocusModeEnum.None,
                TooltipText = Names.Of(type) + "\n" + Tr("HUD_SELECT_ONLY_TYPE"),
            };
            button.Pressed += () => _match.Selection.Select(
                _match.Selection.Ids.Where(uid => _match.Sim.GetUnit(uid)?.Type.Id == id).ToList(), additive: false);
            _selectionTypes.AddChild(button);
        }
    }
}

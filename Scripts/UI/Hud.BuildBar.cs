using System.Linq;
using Godot;
using Warcrown.Core;
using Warcrown.Sim.Buildings;

namespace Warcrown.UI;

/// <summary>
/// The build bar: a button per building kind, with its price, hotkey and how many stand. In Defense a player whose
/// outpost has fallen also gets a button to build a new one.
/// </summary>
public partial class Hud
{
    /// <summary>
    /// Two short lines next to the picture: the cost, then the hotkey and how many of a limited kind stand already.
    /// The name and the rest are in the tooltip.
    /// </summary>
    private string BuildButtonText(BuildingType type, int standing, int cost)
    {
        string hotkey = BuildHotkeys.TryGetValue(type.Id, out var key) ? $"[{key}]" : "";
        string count = type.MaxCount > 0 ? $"{standing}/{type.MaxCount}" : "";
        string second = hotkey.Length > 0 && count.Length > 0 ? hotkey + " " + count : hotkey + count;
        return $"{cost}{Tr("HUD_GOLD_SHORT")}\n{second}";
    }

    /// <summary>What each build button last showed: how many stand, the price (barracks grow dearer), whether it is
    /// affordable, pressed, in which language.</summary>
    private (int Standing, int Cost, bool Available, bool Pressed, bool Shown, int Text)[] _buildShown = System.Array.Empty<(int, int, bool, bool, bool, int)>();

    private void UpdateBuildButtons()
    {
        if (_match.Sim.GetFaction(_match.LocalFactionId) is not { } faction)
            return;
        if (_buildShown.Length != _buildButtons.Count)
        {
            _buildShown = new (int, int, bool, bool, bool, int)[_buildButtons.Count];
            System.Array.Fill(_buildShown, (-1, -1, false, false, false, -1));
        }
        for (int i = 0; i < _buildButtons.Count; i++)
        {
            var (button, type) = _buildButtons[i];
            int standing = type.MaxCount > 0 ? _match.Sim.CountBuildings(_match.LocalFactionId, type.Id) : 0;
            int cost = _match.Sim.BuildCost(type, _match.LocalFactionId);
            bool available = faction.Treasury.CanAfford(cost) && (type.MaxCount == 0 || standing < type.MaxCount);
            bool pressed = _placement.ActiveType == type;
            bool shown = type.IsBuildable || _match.Sim.MayBuildOutpost(_match.LocalFactionId, type);
            if (!Changed(ref _buildShown[i], (standing, cost, available, pressed, shown, _textVersion)))
                continue;
            button.Visible = shown;
            if (!shown && pressed)
                _placement.Cancel();
            button.Text = BuildButtonText(type, standing, cost);
            MarkAvailable(button, available);
            button.ButtonPressed = pressed;
        }
    }

    private void CreateBuildButtons(HBoxContainer row)
    {
        bool defense = _match.Sim.Defense != null;
        foreach (var type in _match.Sim.Rules.BuildingTypes.Where(t => t.IsBuildable || defense && t.IsCapital).OrderBy(t => _match.Sim.BuildCost(t, _match.LocalFactionId)))
        {
            var button = new Button
            {
                Text = BuildButtonText(type, 0, _match.Sim.BuildCost(type, _match.LocalFactionId)),
                Icon = Icons.Building(type.Id, FactionColors.Get(_match.LocalFactionId)),
                IconAlignment = HorizontalAlignment.Left,
                ToggleMode = true,
                FocusMode = Control.FocusModeEnum.None,
                TooltipText = type.IsCapital
                    ? Tr("DEFENSE_OUTPOST") + "\n" + Tr("DEFENSE_OUTPOST_TOOLTIP")
                    : Names.Of(type) + "\n" + HudText.DescribeBuildingType(_match.Sim, type)
                      + (type.MaxCount > 0 ? "\n" + string.Format(Tr("INFO_LIMIT"), type.MaxCount) : ""),
                Visible = type.IsBuildable,
            };
            // Compact: tall buildings are drawn narrower so every button stays about as high as the text.
            button.AddThemeConstantOverride("icon_max_width", 22);
            button.AddThemeConstantOverride("h_separation", 6);
            button.AddThemeFontSizeOverride("font_size", 13);
            string id = type.Id;
            button.Pressed += () =>
            {
                if (_placement.ActiveType?.Id == id)
                    _placement.Cancel();
                else
                    _placement.Begin(id);
            };
            row.AddChild(button);
            _buildButtons.Add((button, type));
        }
    }
}

using System.Linq;
using Godot;
using Lordsio.Core;
using Lordsio.Sim.Buildings;

namespace Lordsio.UI;

/// <summary>The build bar: a button per building kind, with its price, hotkey and how many stand.</summary>
public partial class Hud
{
    /// <summary>
    /// Two short lines next to the picture: the cost, then the hotkey and how many of a limited kind stand already.
    /// The name and the rest are in the tooltip.
    /// </summary>
    private string BuildButtonText(BuildingType type)
    {
        string hotkey = BuildHotkeys.TryGetValue(type.Id, out var key) ? $"[{key}]" : "";
        string count = type.MaxCount > 0 ? $"{_match.Sim.CountBuildings(_match.LocalFactionId, type.Id)}/{type.MaxCount}" : "";
        return $"{type.Cost}{Tr("HUD_GOLD_SHORT")}\n{string.Join(" ", new[] { hotkey, count }.Where(p => p.Length > 0))}";
    }

    private bool AtLimit(BuildingType type) =>
        type.MaxCount > 0 && _match.Sim.CountBuildings(_match.LocalFactionId, type.Id) >= type.MaxCount;

    private void CreateBuildButtons(HBoxContainer row)
    {
        foreach (var type in _match.Sim.Rules.BuildingTypes.Where(t => t.IsBuildable).OrderBy(t => t.Cost))
        {
            var button = new Button
            {
                Text = BuildButtonText(type),
                Icon = Icons.Building(type.Id, FactionColors.Get(_match.LocalFactionId)),
                IconAlignment = HorizontalAlignment.Left,
                ToggleMode = true,
                FocusMode = Control.FocusModeEnum.None,
                TooltipText = Names.Of(type) + "\n" + HudText.DescribeBuildingType(_match.Sim, type)
                              + (type.MaxCount > 0 ? "\n" + string.Format(Tr("INFO_LIMIT"), type.MaxCount) : ""),
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

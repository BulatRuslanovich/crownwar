using System.Collections.Generic;
using Godot;
using Warcrown.Sim.Buildings;
using Warcrown.Sim.Core;
using Warcrown.Sim.Units;

namespace Warcrown.UI;

/// <summary>
/// The doctrine row of the castle's panel: a button per doctrine until the player commits to one (it opens at the
/// rules' number of barracks), then the name of the chosen one.
/// </summary>
public partial class Hud
{
    private VBoxContainer _doctrineRow = null!;
    private Label _doctrineTitle = null!;
    private readonly List<(Button Button, Doctrine Doctrine)> _doctrineButtons = new();
    private (string? Chosen, int Level, int Text) _doctrineShown = ("?", -1, -1);

    private void CreateDoctrineRow(Node rows, Node after)
    {
        _doctrineRow = new VBoxContainer();
        _doctrineTitle = new Label();
        _doctrineTitle.AddThemeFontSizeOverride("font_size", 14);
        _doctrineRow.AddChild(_doctrineTitle);
        var buttons = new HBoxContainer();
        _doctrineRow.AddChild(buttons);
        foreach (var doctrine in _match.Sim.Rules.Doctrines)
        {
            var button = new Button { FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            button.AddThemeFontSizeOverride("font_size", 14);
            string id = doctrine.Id;
            button.Pressed += () => _match.Issue(new ChooseDoctrineCommand(_match.LocalFactionId, id));
            buttons.AddChild(button);
            _doctrineButtons.Add((button, doctrine));
        }
        rows.AddChild(_doctrineRow);
        rows.MoveChild(_doctrineRow, after.GetIndex() + 1);
        _doctrineRow.Visible = false;
    }

    private void UpdateDoctrine(Building building, bool own)
    {
        var sim = _match.Sim;
        _doctrineRow.Visible = own && building.IsComplete && building.Type.Recruits.Count > 0 && sim.Rules.Doctrines.Count > 0
                               && sim.GetFaction(_match.LocalFactionId) != null;
        if (!_doctrineRow.Visible)
            return;
        string? chosen = sim.GetFaction(_match.LocalFactionId)!.Doctrine;
        int level = sim.UnlockLevel(_match.LocalFactionId);
        if (!Changed(ref _doctrineShown, (chosen, level, _textVersion)))
            return;
        if (sim.Rules.FindDoctrine(chosen) is { } doctrine)
        {
            _doctrineTitle.Text = string.Format(Tr("HUD_DOCTRINE_CHOSEN"), Tr(HudText.DoctrineKey(doctrine)));
            _doctrineTitle.TooltipText = HudText.DescribeDoctrine(sim, doctrine);
            _doctrineTitle.MouseFilter = Control.MouseFilterEnum.Pass;
            foreach (var (button, _) in _doctrineButtons)
                button.Visible = false;
            return;
        }
        _doctrineTitle.Text = Tr("HUD_DOCTRINE_CHOOSE");
        _doctrineTitle.TooltipText = "";
        foreach (var (button, option) in _doctrineButtons)
        {
            var error = sim.CanChooseDoctrine(_match.LocalFactionId, option.Id);
            button.Visible = true;
            button.Text = Tr(HudText.DoctrineKey(option));
            MarkAvailable(button, error == DoctrineError.None);
            string about = HudText.DescribeDoctrine(sim, option);
            button.TooltipText = error == DoctrineError.None ? about : HudText.Describe(error, sim.Rules.DoctrineBarracks) + "\n" + about;
        }
    }
}

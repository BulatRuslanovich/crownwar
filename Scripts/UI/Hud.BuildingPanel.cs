using System.Linq;
using Godot;
using Lordsio.Core;
using Lordsio.Sim.Buildings;
using Lordsio.Sim.Core;

namespace Lordsio.UI;

/// <summary>The selected building's panel: health, recruiting, its training queue, upgrades and demolition.</summary>
public partial class Hud
{
    private void UpdateBuildingPanel()
    {
        UpdateSelectionPanel();
        var building = _match.Selection.BuildingId is { } id ? _match.Sim.GetBuilding(id) : null;
        _buildingPanel.Visible = building != null;
        if (building == null)
        {
            _panelBuildingId = null;
            return;
        }
        if (_panelBuildingId != building.Id)
        {
            _panelBuildingId = building.Id;
            RebuildRecruitButtons(building);
        }

        bool own = building.FactionId == _match.LocalFactionId;
        string level = building.Type.MaxTier > 1 ? " " + string.Format(Tr("HUD_TIER"), building.Type.Tier) : "";
        // Own buildings need no owner in the title (and the short title keeps the panel clear of the build bar).
        _title.Text = $"{Names.Of(building.Type)}{level}" + (own ? "" : $" ({Names.Faction(building.FactionId)})");
        string status = building.IsComplete
            ? HudText.DescribeBuildingType(_match.Sim, building.Type, building)
            : string.Format(Tr("HUD_UNDER_CONSTRUCTION"), building.ConstructionProgress * 100f);
        _info.Text = string.Format(Tr("HUD_HEALTH"), building.Health, building.MaxHealth) + "\n" + status;

        _recruitRow.Visible = own && building.IsComplete && _recruitButtons.Count > 0;
        foreach (var (button, typeId) in _recruitButtons)
        {
            var type = _match.Sim.Rules.Units.Get(typeId);
            // Buttons stay clickable: the simulation is the judge and explains a refusal with a message.
            var error = _match.Sim.CanRecruit(_match.LocalFactionId, building.Id, typeId);
            // A kind not opened yet (too few barracks) is not shown at all.
            button.Visible = error != RecruitError.Locked;
            button.Text = $"{type.Cost}{Tr("HUD_GOLD_SHORT")}";
            MarkAvailable(button, error == RecruitError.None);
            string about = HudText.DescribeUnitType(_match.Sim, type);
            button.TooltipText = error == RecruitError.None ? about : HudText.Describe(error) + "\n" + about;
        }

        UpdateUpgrade(building, own);
        UpdateDemolish(building, own);

        bool training = own && building.Queue.Count > 0;
        _queue.Visible = training;
        _progress.Visible = training;
        if (training)
        {
            // Several recruits train side by side (a place per barracks); the rest wait.
            int active = Mathf.Min(_match.Sim.TrainingSlots(building), building.Queue.Count);
            var current = building.Queue[0];
            string names = string.Join(", ", building.Queue.Take(active).Select(item => Names.Of(item.Type)));
            _queue.Text = string.Format(Tr("HUD_TRAINING"), names) +
                (building.Queue.Count > active ? "  " + string.Format(Tr("HUD_QUEUED"), building.Queue.Count - active) : "");
            _progress.Value = current.Progress * 100f;
        }
    }

    /// <summary>
    /// The level section of a tower's or mine's panel: its level, the button for the next one (cost, hotkey U, and
    /// what changes in the tooltip), or the progress of the upgrade under way.
    /// </summary>
    private void UpdateUpgrade(Building building, bool own)
    {
        var type = building.Type;
        bool visible = own && building.IsComplete && type.MaxTier > 1;
        _upgradeTitle.Visible = visible;
        _upgradeButton.Visible = visible && !building.IsUpgrading && type.NextTier != null;
        _upgradeProgress.Visible = visible && building.IsUpgrading;
        if (!visible)
            return;

        if (building.IsUpgrading)
        {
            _upgradeTitle.Text = string.Format(Tr("HUD_UPGRADING"), type.Tier + 1, building.UpgradeProgress * 100f);
            _upgradeProgress.Value = building.UpgradeProgress * 100f;
            return;
        }
        _upgradeTitle.Text = string.Format(Tr(type.NextTier == null ? "HUD_LEVEL_MAX" : "HUD_LEVEL"), type.Tier, type.MaxTier);
        if (type.NextTier is not { } next)
            return;
        _upgradeButton.Text = string.Format(Tr("HUD_UPGRADE_BUTTON"), next.Tier, type.UpgradeCost);
        var error = _match.Sim.CanUpgrade(_match.LocalFactionId, building.Id);
        MarkAvailable(_upgradeButton, error == UpgradeError.None);
        string changes = HudText.DescribeUpgrade(type, next);
        _upgradeButton.TooltipText = error == UpgradeError.None ? changes : HudText.Describe(error) + "\n" + changes;
    }

    /// <summary>The demolish button: what pulling the building down gives back; a second press within seconds does it.</summary>
    private void UpdateDemolish(Building building, bool own)
    {
        var error = _match.Sim.CanDemolish(_match.LocalFactionId, building.Id, out int refund);
        _demolishButton.Visible = own && error == DemolishError.None;
        if (!_demolishButton.Visible)
            return;
        bool armed = _demolishArmed is { } a && a.BuildingId == building.Id && Time.GetTicksMsec() < a.UntilMs;
        _demolishButton.Text = string.Format(Tr(armed ? "HUD_DEMOLISH_CONFIRM" : "HUD_DEMOLISH"), refund);
        _demolishButton.Modulate = armed ? ProblemColor : Colors.White;
        _demolishButton.TooltipText = string.Format(Tr("HUD_DEMOLISH_TOOLTIP"), _match.Sim.Rules.DemolishRefundPercent);
    }

    /// <summary>Delete or the panel button: the first press asks, the second (within three seconds) pulls the building down.</summary>
    private void DemolishSelected()
    {
        if (_match.Selection.BuildingId is not { } id || _match.Sim.CanDemolish(_match.LocalFactionId, id, out _) != DemolishError.None)
            return;
        if (_demolishArmed is { } a && a.BuildingId == id && Time.GetTicksMsec() < a.UntilMs)
        {
            _demolishArmed = null;
            _match.Issue(new DemolishCommand(_match.LocalFactionId, id));
            return;
        }
        _demolishArmed = (id, Time.GetTicksMsec() + DemolishConfirmMs);
    }

    /// <summary>U: upgrade the selected building, the same as its panel button.</summary>
    private void UpgradeSelected()
    {
        if (_match.Selection.BuildingId is { } id && _match.Sim.GetBuilding(id) is { } building
            && building.FactionId == _match.LocalFactionId && building.Type.MaxTier > 1)
            _match.Issue(new UpgradeCommand(_match.LocalFactionId, id));
    }

    private void RebuildRecruitButtons(Building building)
    {
        foreach (var (button, _) in _recruitButtons)
            button.QueueFree();
        _recruitButtons.Clear();

        foreach (string typeId in building.Type.Recruits)
        {
            var button = new Button
            {
                FocusMode = Control.FocusModeEnum.None,
                Icon = Icons.Unit(typeId, FactionColors.Get(_match.LocalFactionId)),
            };
            int buildingId = building.Id;
            // Shift orders five at once (the queue has no limit, only the army cap).
            button.Pressed += () =>
            {
                int count = Input.IsKeyPressed(Key.Shift) ? ShiftRecruitCount : 1;
                for (int i = 0; i < count; i++)
                    _match.Issue(new RecruitCommand(_match.LocalFactionId, buildingId, typeId));
            };
            _recruitRow.AddChild(button);
            _recruitButtons.Add((button, typeId));
        }
    }
}

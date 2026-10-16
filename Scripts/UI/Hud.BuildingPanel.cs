using System.Collections.Generic;
using System.Linq;
using Godot;
using Warcrown.Core;
using Warcrown.Sim.Buildings;
using Warcrown.Sim.Core;

namespace Warcrown.UI;

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
        UpdateBuildingInfo(building, own);
        UpdateRecruitButtons(building, own);
        UpdateUpgrade(building, own);
        UpdateDemolish(building, own);
        UpdateQueue(building, own);
    }

    private (int Id, int Health, int Tier, int Built, int Slots, int Reserve, int Regen, int Text) _infoShown;

    /// <summary>The title (kind, level, owner) and what the building does, with its health.</summary>
    private void UpdateBuildingInfo(Building building, bool own)
    {
        int built = building.IsComplete ? 101 : (int)(building.ConstructionProgress * 100f);
        int slots = building.Type.Recruits.Count > 0 ? _match.Sim.TrainingSlots(building) : 0;
        if (!Changed(ref _infoShown, (building.Id, building.Health, building.Type.Tier, built, slots, building.Deposit?.Reserve ?? 0,
                building.Deposit?.RegenTicksLeft / Simulation.TicksPerSecond ?? 0, _textVersion)))
            return;
        string level = building.Type.MaxTier > 1 ? " " + string.Format(Tr("HUD_TIER"), building.Type.Tier) : "";
        // Own buildings need no owner in the title (and the short title keeps the panel clear of the build bar).
        _title.Text = $"{Names.Of(building.Type)}{level}" + (own ? "" : $" ({Names.Faction(building.FactionId)})");
        string status = building.IsComplete
            ? HudText.DescribeBuildingType(_match.Sim, building.Type, building)
            : string.Format(Tr("HUD_UNDER_CONSTRUCTION"), building.ConstructionProgress * 100f);
        _info.Text = string.Format(Tr("HUD_HEALTH"), building.Health, building.MaxHealth) + "\n" + status;
    }

    /// <summary>What each recruit button last showed: why it is refused (if it is), in which language.</summary>
    private readonly List<(RecruitError Error, int Text)> _recruitShown = new();

    private void UpdateRecruitButtons(Building building, bool own)
    {
        _recruitRow.Visible = own && building.IsComplete && _recruitButtons.Count > 0;
        if (!_recruitRow.Visible)
            return;
        for (int i = 0; i < _recruitButtons.Count; i++)
        {
            var (button, typeId) = _recruitButtons[i];
            // Buttons stay clickable: the simulation is the judge and explains a refusal with a message.
            var error = _match.Sim.CanRecruit(_match.LocalFactionId, building.Id, typeId);
            var shown = _recruitShown[i];
            if (!Changed(ref shown, (error, _textVersion)))
                continue;
            _recruitShown[i] = shown;
            var type = _match.Sim.Rules.Units.Get(typeId);
            // A kind not opened yet (too few barracks) is not shown at all.
            button.Visible = error != RecruitError.Locked;
            button.Text = $"{type.Cost}{Tr("HUD_GOLD_SHORT")}";
            MarkAvailable(button, error == RecruitError.None);
            string about = HudText.DescribeUnitType(_match.Sim, type);
            button.TooltipText = error == RecruitError.None ? about : HudText.Describe(error) + "\n" + about;
        }
    }

    private (int Count, int Active, string? First, int Text) _queueShown;

    /// <summary>Who is training (several side by side, a place per barracks) and how many wait.</summary>
    private void UpdateQueue(Building building, bool own)
    {
        bool training = own && building.Queue.Count > 0;
        _queue.Visible = training;
        _progress.Visible = training;
        if (!training)
            return;
        _progress.Value = building.Queue[0].Progress * 100f;
        int active = Mathf.Min(_match.Sim.TrainingSlots(building), building.Queue.Count);
        if (!Changed(ref _queueShown, (building.Queue.Count, active, building.Queue[0].Type.Id, _textVersion)))
            return;
        string names = string.Join(", ", building.Queue.Take(active).Select(item => Names.Of(item.Type)));
        _queue.Text = string.Format(Tr("HUD_TRAINING"), names) +
            (building.Queue.Count > active ? "  " + string.Format(Tr("HUD_QUEUED"), building.Queue.Count - active) : "");
    }

    /// <summary>
    /// The level section of a tower's or mine's panel: its level, the button for the next one (cost, hotkey U, and
    /// what changes in the tooltip), or the progress of the upgrade under way.
    /// </summary>
    private (int Id, int Tier, int Progress, UpgradeError Error, int Text) _upgradeShown;

    private void UpdateUpgrade(Building building, bool own)
    {
        var type = building.Type;
        bool visible = own && building.IsComplete && type.MaxTier > 1;
        _upgradeTitle.Visible = visible;
        _upgradeButton.Visible = visible && !building.IsUpgrading && type.NextTier != null;
        _upgradeProgress.Visible = visible && building.IsUpgrading;
        if (!visible)
            return;

        var error = _match.Sim.CanUpgrade(_match.LocalFactionId, building.Id);
        int progress = building.IsUpgrading ? (int)(building.UpgradeProgress * 100f) : -1;
        if (!Changed(ref _upgradeShown, (building.Id, type.Tier, progress, error, _textVersion)))
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
        MarkAvailable(_upgradeButton, error == UpgradeError.None);
        string changes = HudText.DescribeUpgrade(type, next);
        _upgradeButton.TooltipText = error == UpgradeError.None ? changes : HudText.Describe(error) + "\n" + changes;
    }

    /// <summary>The demolish button: what pulling the building down gives back; a second press within seconds does it.</summary>
    private (int Id, bool Armed, int Refund, int Text) _demolishShown;

    private void UpdateDemolish(Building building, bool own)
    {
        var error = _match.Sim.CanDemolish(_match.LocalFactionId, building.Id, out int refund);
        _demolishButton.Visible = own && error == DemolishError.None;
        if (!_demolishButton.Visible)
            return;
        bool armed = _demolishArmed is { } a && a.BuildingId == building.Id && Time.GetTicksMsec() < a.UntilMs;
        if (!Changed(ref _demolishShown, (building.Id, armed, refund, _textVersion)))
            return;
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

    /// <summary>Four recruit buttons to a row of the building panel (340 wide, 4 apart).</summary>
    private const float RecruitButtonWidth = 82f;

    private void RebuildRecruitButtons(Building building)
    {
        foreach (var (button, _) in _recruitButtons)
        {
            _shownAvailable.Remove(button);
            button.QueueFree();
        }
        _recruitButtons.Clear();
        _recruitShown.Clear();

        foreach (string typeId in building.Type.Recruits)
        {
            var button = new Button
            {
                FocusMode = Control.FocusModeEnum.None,
                Icon = Icons.Unit(typeId, FactionColors.Get(_match.LocalFactionId)),
                // A quarter of the panel each, however many kinds are open: the grid never changes its look.
                CustomMinimumSize = new Vector2(RecruitButtonWidth, 0),
            };
            // Compact like the build bar, so four fit in a row of the panel.
            button.AddThemeConstantOverride("icon_max_width", 22);
            button.AddThemeConstantOverride("h_separation", 4);
            button.AddThemeFontSizeOverride("font_size", 13);
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
            _recruitShown.Add(((RecruitError)(-1), -1));
        }
    }
}

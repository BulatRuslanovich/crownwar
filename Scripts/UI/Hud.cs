using System.Collections.Generic;
using System.Linq;
using Godot;
using Lordsio.Core;
using Lordsio.Interaction;
using Lordsio.Sim.Buildings;
using Lordsio.Sim.Core;

namespace Lordsio.UI;

/// <summary>
/// Player HUD: resources, the build bar, the selected building's panel and short messages.
/// Reads the simulation and only ever changes it by issuing commands. All text goes through the
/// translation tables (Translations/strings.csv).
/// </summary>
public partial class Hud : CanvasLayer
{
    private const float MessageDuration = 2.5f;

    private static readonly Color ProblemColor = new("ff8a80");

    /// <summary>Physical hotkey per building type id; types without one are still on the bar.</summary>
    private static readonly Dictionary<string, Key> BuildHotkeys = new()
    {
        ["mine"] = Key.M,
        ["barracks"] = Key.B,
        ["tower"] = Key.T,
    };

    private readonly List<(Button Button, string UnitTypeId)> _recruitButtons = new();
    private readonly List<(Button Button, BuildingType Type)> _buildButtons = new();
    private MatchController _match = null!;
    private BuildPlacement _placement = null!;
    private Label _gold = null!;
    private Label _income = null!;
    private Label _army = null!;
    private Label _buildHint = null!;
    private Control _buildingPanel = null!;
    private Label _title = null!;
    private Label _info = null!;
    private Container _recruitRow = null!;
    private Label _queue = null!;
    private ProgressBar _progress = null!;
    private Label _message = null!;
    private AlertIndicator _alert = null!;
    private int? _panelBuildingId;
    private float _messageTimeLeft;

    public void Initialize(MatchController match, BuildPlacement placement)
    {
        _match = match;
        _placement = placement;
        _gold = GetNode<Label>("ResourceBar/Row/Gold");
        _income = GetNode<Label>("ResourceBar/Row/Income");
        _income.MouseFilter = Control.MouseFilterEnum.Pass; // labels ignore the mouse by default: no tooltip
        _army = GetNode<Label>("ResourceBar/Row/Army");
        _buildHint = GetNode<Label>("BuildBar/Rows/Hint");
        _buildingPanel = GetNode<Control>("BuildingPanel");
        _title = GetNode<Label>("BuildingPanel/Rows/Title");
        _info = GetNode<Label>("BuildingPanel/Rows/Info");
        _recruitRow = GetNode<Container>("BuildingPanel/Rows/RecruitButtons");
        _queue = GetNode<Label>("BuildingPanel/Rows/Queue");
        _progress = GetNode<ProgressBar>("BuildingPanel/Rows/Progress");
        _message = GetNode<Label>("Message");
        _alert = GetNode<AlertIndicator>("AlertIndicator");
        _alert.Initialize(match);
        _buildingPanel.Visible = false;
        _message.Visible = false;
        CreateBuildButtons(GetNode<HBoxContainer>("BuildBar/Rows/Buttons"));
    }

    public override void _Process(double delta)
    {
        if (_match.Sim.GetFaction(_match.LocalFactionId) is { } faction)
        {
            _gold.Text = string.Format(Tr("HUD_GOLD"), faction.Treasury.Gold);
            float net = faction.NetPerMinute / 60f;
            _income.Text = string.Format(Tr("HUD_INCOME"), net);
            _income.Modulate = net < 0f ? ProblemColor : Colors.White;
            _income.TooltipText = string.Format(Tr("HUD_INCOME_TOOLTIP"), faction.IncomePerSecond,
                faction.UpkeepPerMinute / 60f, faction.ArmyUpkeepPerMinute, _match.Sim.Rules.UpkeepAllowance);
            _army.Text = string.Format(Tr("HUD_ARMY"), faction.Population, faction.ArmyCap);
            _army.Modulate = faction.Population >= faction.ArmyCap ? ProblemColor : Colors.White;
            foreach (var (button, type) in _buildButtons)
            {
                button.Text = BuildButtonText(type); // follows a language change
                button.Modulate = faction.Treasury.CanAfford(type.Cost) ? Colors.White : ProblemColor;
                button.ButtonPressed = _placement.ActiveType == type;
            }
        }

        _buildHint.Text = _placement.ActiveType is { } placing
            ? string.Format(Tr("BUILD_PLACING"), Names.Of(placing), DescribePlacement(_placement.CurrentError))
            : Tr("BUILD_HINT");

        UpdateBuildingPanel();

        // Lockstep stall: keep the notice up for as long as we wait.
        if (_match.WaitingForPlayers)
            ShowMessage(Tr("NET_WAITING"));

        if (_messageTimeLeft > 0f)
        {
            _messageTimeLeft -= (float)delta;
            _message.Modulate = new Color(1, 1, 1, Mathf.Clamp(_messageTimeLeft / 0.5f, 0f, 1f));
            _message.Visible = _messageTimeLeft > 0f;
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false, CtrlPressed: false } key)
            return;
        foreach (var (_, type) in _buildButtons)
        {
            if (BuildHotkeys.TryGetValue(type.Id, out var hotkey) && key.PhysicalKeycode == hotkey)
            {
                _placement.Begin(type.Id);
                GetViewport().SetInputAsHandled();
                return;
            }
        }
    }

    /// <summary>Reacts to simulation events that concern the local player.</summary>
    public void OnSimEvent(SimEvent e)
    {
        int player = _match.LocalFactionId;
        switch (e)
        {
            case RecruitRejected rejected when rejected.FactionId == player:
                ShowMessage(Describe(rejected.Reason));
                break;
            case BuildRejected rejected when rejected.FactionId == player:
                ShowMessage(DescribePlacement(rejected.Reason));
                break;
            case UnitRecruited recruited when recruited.FactionId == player && _match.Sim.GetUnit(recruited.UnitId) is { } unit:
                ShowMessage(string.Format(Tr("MSG_UNIT_READY"), Names.Of(unit.Type)));
                break;
            case BuildingCompleted completed when completed.FactionId == player && _match.Sim.GetBuilding(completed.BuildingId) is { } building:
                ShowMessage(string.Format(Tr("MSG_BUILDING_COMPLETED"), Names.Of(building.Type)));
                break;
            case BuildingDestroyed destroyed:
                string name = Names.Of(_match.Sim.Rules.GetBuildingType(destroyed.BuildingTypeId));
                if (destroyed.FactionId == player)
                    ShowMessage(string.Format(Tr("MSG_BUILDING_LOST"), name));
                else if (destroyed.KillerFactionId == player)
                    ShowMessage(string.Format(Tr("MSG_ENEMY_BUILDING_DESTROYED"), name));
                break;
            case DepositExhausted exhausted when exhausted.FactionId == player:
                ShowMessage(string.Format(Tr("MSG_DEPOSIT_EXHAUSTED"), _match.Sim.Rules.DepositRegenSeconds));
                break;
            case DepositRestored restored when restored.FactionId == player:
                ShowMessage(Tr("MSG_DEPOSIT_RESTORED"));
                break;
            case FactionEliminated eliminated when eliminated.FactionId != player:
                ShowMessage(string.Format(Tr("MSG_FACTION_FELL"), Names.Faction(eliminated.FactionId)));
                break;
        }
    }

    /// <summary>Warns that the player is under attack and points at the place.</summary>
    public void ShowAlert(System.Numerics.Vector2 simPosition)
    {
        ShowMessage(Tr("MSG_UNDER_ATTACK"));
        _alert.PointAt(simPosition);
    }

    private string BuildButtonText(BuildingType type)
    {
        string hotkey = BuildHotkeys.TryGetValue(type.Id, out var key) ? $" [{key}]" : "";
        return $"{Names.Of(type)} {type.Cost}{Tr("HUD_GOLD_SHORT")}{hotkey}";
    }

    private void CreateBuildButtons(HBoxContainer row)
    {
        foreach (var type in _match.Sim.Rules.BuildingTypes.Where(t => t.IsBuildable).OrderBy(t => t.Cost))
        {
            var button = new Button
            {
                Text = BuildButtonText(type),
                ToggleMode = true,
                FocusMode = Control.FocusModeEnum.None,
                TooltipText = DescribeBuildingType(type),
            };
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

    private void UpdateBuildingPanel()
    {
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
        _title.Text = $"{Names.Of(building.Type)} ({(own ? Tr("HUD_YOURS") : Names.Faction(building.FactionId))})";
        string status = building.IsComplete
            ? DescribeBuildingType(building.Type, building)
            : string.Format(Tr("HUD_UNDER_CONSTRUCTION"), building.ConstructionProgress * 100f);
        _info.Text = string.Format(Tr("HUD_HEALTH"), building.Health, building.MaxHealth) + "\n" + status;

        _recruitRow.Visible = own && building.IsComplete && _recruitButtons.Count > 0;
        foreach (var (button, typeId) in _recruitButtons)
        {
            var type = _match.Sim.Rules.Units.Get(typeId);
            button.Text = $"{Names.Of(type)}  {type.Cost}{Tr("HUD_GOLD_SHORT")}";
            // Buttons stay clickable: the simulation is the judge and explains a refusal with a message.
            var error = _match.Sim.CanRecruit(_match.LocalFactionId, building.Id, typeId);
            button.Modulate = error == RecruitError.None ? Colors.White : ProblemColor;
            string upkeep = string.Format(Tr("INFO_UPKEEP"), type.Upkeep);
            button.TooltipText = error == RecruitError.None ? upkeep : Describe(error) + "\n" + upkeep;
        }

        bool training = own && building.Queue.Count > 0;
        _queue.Visible = training;
        _progress.Visible = training;
        if (training)
        {
            var current = building.Queue[0];
            _queue.Text = string.Format(Tr("HUD_TRAINING"), Names.Of(current.Type)) +
                (building.Queue.Count > 1 ? "  " + string.Format(Tr("HUD_QUEUED"), building.Queue.Count - 1) : "");
            _progress.Value = current.Progress * 100f;
        }
    }

    private void RebuildRecruitButtons(Building building)
    {
        foreach (var (button, _) in _recruitButtons)
            button.QueueFree();
        _recruitButtons.Clear();

        foreach (string typeId in building.Type.Recruits)
        {
            var button = new Button { FocusMode = Control.FocusModeEnum.None };
            int buildingId = building.Id;
            button.Pressed += () => _match.Issue(
                new RecruitCommand(_match.LocalFactionId, buildingId, typeId));
            _recruitRow.AddChild(button);
            _recruitButtons.Add((button, typeId));
        }
    }

    /// <summary>A short message in the middle of the screen, e.g. about other players in a network game.</summary>
    public void ShowNotice(string text) => ShowMessage(text);

    private void ShowMessage(string text)
    {
        _message.Text = text;
        _message.Visible = true;
        _message.Modulate = Colors.White;
        _messageTimeLeft = MessageDuration;
    }

    private string DescribeBuildingType(BuildingType type, Building? instance = null)
    {
        var parts = new List<string>();
        if (type.IsMine)
            parts.Add(instance?.Deposit is { } d ? DescribeDeposit(d) : Tr("INFO_MINE"));
        if (type.Income > 0)
            parts.Add(string.Format(Tr("INFO_INCOME"), type.Income));
        if (type.ArmyCap > 0)
            parts.Add(string.Format(Tr("INFO_ARMY_CAP"), type.ArmyCap));
        if (type.Recruits.Count > 0)
            parts.Add(Tr("INFO_TRAINS"));
        if (type.Attack is { } attack)
            parts.Add(string.Format(Tr("INFO_SHOOTS"), attack.Range));
        if (type.IsCapital)
            parts.Add(Tr("INFO_CAPITAL"));
        return string.Join("  ·  ", parts);
    }

    private string DescribeDeposit(Lordsio.Sim.World.GoldDeposit d)
    {
        if (d.IsExhausted)
            return string.Format(Tr("INFO_DEPOSIT_EXHAUSTED"), Mathf.CeilToInt(d.RegenTicksLeft / (float)Simulation.TicksPerSecond));
        string mining = string.Format(Tr("INFO_MINING"), d.Yield);
        return d.IsEndless ? mining : mining + "  ·  " + string.Format(Tr("INFO_DEPOSIT_RESERVE"), d.Reserve, d.Capacity);
    }

    private string Describe(RecruitError error) => Tr(error switch
    {
        RecruitError.NotEnoughGold => "ERR_NOT_ENOUGH_GOLD",
        RecruitError.ArmyCapReached => "ERR_ARMY_CAP",
        RecruitError.QueueFull => "ERR_QUEUE_FULL",
        RecruitError.NotComplete => "ERR_NOT_COMPLETE",
        RecruitError.NotOwner => "ERR_NOT_OWNER",
        RecruitError.CannotRecruitType => "ERR_CANNOT_TRAIN",
        _ => "ERR_UNKNOWN_BUILDING",
    });

    private string DescribePlacement(BuildError error) => Tr(error switch
    {
        BuildError.None => "BUILD_OK",
        BuildError.NotEnoughGold => "ERR_NOT_ENOUGH_GOLD",
        BuildError.NoUnitsNearby => "BUILD_ERR_NO_UNITS",
        BuildError.NeedsDeposit => "BUILD_ERR_NEEDS_DEPOSIT",
        BuildError.OnDeposit => "BUILD_ERR_ON_DEPOSIT",
        BuildError.Occupied => "BUILD_ERR_OCCUPIED",
        BuildError.BlockedTerrain => "BUILD_ERR_TERRAIN",
        BuildError.OutOfBounds => "BUILD_ERR_OUT_OF_BOUNDS",
        _ => "BUILD_ERR_NOT_BUILDABLE",
    });
}

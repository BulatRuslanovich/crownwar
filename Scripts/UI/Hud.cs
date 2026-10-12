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
/// Reads the simulation and only ever changes it by issuing commands.
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
    private HBoxContainer _recruitRow = null!;
    private Label _queue = null!;
    private ProgressBar _progress = null!;
    private Label _message = null!;
    private Label _banner = null!;
    private int? _panelBuildingId;
    private float _messageTimeLeft;

    public void Initialize(MatchController match, BuildPlacement placement)
    {
        _match = match;
        _placement = placement;
        _gold = GetNode<Label>("ResourceBar/Row/Gold");
        _income = GetNode<Label>("ResourceBar/Row/Income");
        _army = GetNode<Label>("ResourceBar/Row/Army");
        _buildHint = GetNode<Label>("BuildBar/Rows/Hint");
        _buildingPanel = GetNode<Control>("BuildingPanel");
        _title = GetNode<Label>("BuildingPanel/Rows/Title");
        _info = GetNode<Label>("BuildingPanel/Rows/Info");
        _recruitRow = GetNode<HBoxContainer>("BuildingPanel/Rows/RecruitButtons");
        _queue = GetNode<Label>("BuildingPanel/Rows/Queue");
        _progress = GetNode<ProgressBar>("BuildingPanel/Rows/Progress");
        _message = GetNode<Label>("Message");
        _banner = GetNode<Label>("Banner");
        _buildingPanel.Visible = false;
        _message.Visible = false;
        _banner.Visible = false;
        CreateBuildButtons(GetNode<HBoxContainer>("BuildBar/Rows/Buttons"));
    }

    public override void _Process(double delta)
    {
        if (_match.Sim.GetFaction(MatchController.PlayerFactionId) is { } faction)
        {
            _gold.Text = $"Gold {faction.Treasury.Gold}";
            _income.Text = $"+{faction.IncomePerSecond}/s";
            _army.Text = $"Army {faction.Population}/{faction.ArmyCap}";
            _army.Modulate = faction.Population >= faction.ArmyCap ? ProblemColor : Colors.White;
            foreach (var (button, type) in _buildButtons)
            {
                button.Modulate = faction.Treasury.CanAfford(type.Cost) ? Colors.White : ProblemColor;
                button.ButtonPressed = _placement.ActiveType == type;
            }
        }

        _buildHint.Text = _placement.ActiveType is { } placing
            ? $"Placing {placing.Name}: {DescribePlacement(_placement.CurrentError)}   (LMB place, Shift+LMB place more, RMB / Esc cancel)"
            : "Build near your own troops. Mines go on gold deposits.";

        UpdateBuildingPanel();

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
        int player = MatchController.PlayerFactionId;
        switch (e)
        {
            case RecruitRejected rejected when rejected.FactionId == player:
                ShowMessage(Describe(rejected.Reason));
                break;
            case BuildRejected rejected when rejected.FactionId == player:
                ShowMessage(DescribePlacement(rejected.Reason));
                break;
            case UnitRecruited recruited when recruited.FactionId == player && _match.Sim.GetUnit(recruited.UnitId) is { } unit:
                ShowMessage($"{unit.Type.Name} is ready");
                break;
            case BuildingCompleted completed when completed.FactionId == player && _match.Sim.GetBuilding(completed.BuildingId) is { } building:
                ShowMessage($"{building.Type.Name} completed");
                break;
            case BuildingDestroyed destroyed:
                string name = _match.Sim.Rules.GetBuildingType(destroyed.BuildingTypeId).Name;
                if (destroyed.FactionId == player)
                    ShowMessage($"Your {name} was destroyed!");
                else if (destroyed.KillerFactionId == player)
                    ShowMessage($"Enemy {name} destroyed");
                break;
            case FactionEliminated eliminated when eliminated.FactionId != player:
                ShowMessage($"Faction {eliminated.FactionId} has fallen");
                break;
            case MatchEnded ended:
                _banner.Text = ended.FactionId == player ? "Victory!" : "Defeat";
                _banner.Modulate = ended.FactionId == player ? new Color("ffe066") : ProblemColor;
                _banner.Visible = true;
                break;
        }
    }

    private void CreateBuildButtons(HBoxContainer row)
    {
        foreach (var type in _match.Sim.Rules.BuildingTypes.Where(t => t.IsBuildable).OrderBy(t => t.Cost))
        {
            string hotkey = BuildHotkeys.TryGetValue(type.Id, out var key) ? $" [{key}]" : "";
            var button = new Button
            {
                Text = $"{type.Name} {type.Cost}g{hotkey}",
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

        bool own = building.FactionId == MatchController.PlayerFactionId;
        _title.Text = $"{building.Type.Name} ({(own ? "yours" : $"faction {building.FactionId}")})";
        string status = building.IsComplete
            ? DescribeBuildingType(building.Type, building)
            : $"Under construction: {building.ConstructionProgress * 100f:0}%";
        _info.Text = $"Health {building.Health}/{building.MaxHealth}\n{status}";

        _recruitRow.Visible = own && building.IsComplete && _recruitButtons.Count > 0;
        foreach (var (button, typeId) in _recruitButtons)
        {
            // Buttons stay clickable: the simulation is the judge and explains a refusal with a message.
            var error = _match.Sim.CanRecruit(MatchController.PlayerFactionId, building.Id, typeId);
            button.Modulate = error == RecruitError.None ? Colors.White : ProblemColor;
            button.TooltipText = error == RecruitError.None ? "" : Describe(error);
        }

        bool training = own && building.Queue.Count > 0;
        _queue.Visible = training;
        _progress.Visible = training;
        if (training)
        {
            var current = building.Queue[0];
            _queue.Text = $"Training {current.Type.Name}" +
                (building.Queue.Count > 1 ? $"  (+{building.Queue.Count - 1} queued)" : "");
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
            var type = _match.Sim.Rules.Units.Get(typeId);
            var button = new Button { Text = $"{type.Name}  {type.Cost}g", FocusMode = Control.FocusModeEnum.None };
            int buildingId = building.Id;
            button.Pressed += () => _match.Sim.Issue(
                new RecruitCommand(MatchController.PlayerFactionId, buildingId, typeId));
            _recruitRow.AddChild(button);
            _recruitButtons.Add((button, typeId));
        }
    }

    private void ShowMessage(string text)
    {
        _message.Text = text;
        _message.Visible = true;
        _message.Modulate = Colors.White;
        _messageTimeLeft = MessageDuration;
    }

    private static string DescribeBuildingType(BuildingType type, Building? instance = null)
    {
        var parts = new List<string>();
        if (type.IsMine)
            parts.Add(instance?.Deposit is { } d ? $"Mining +{d.Yield} gold/s" : "Earns gold on a deposit");
        if (type.Income > 0)
            parts.Add($"Income +{type.Income}/s");
        if (type.ArmyCap > 0)
            parts.Add($"Army limit +{type.ArmyCap}");
        if (type.Recruits.Count > 0)
            parts.Add("Trains troops");
        if (type.Attack is { } attack)
            parts.Add($"Shoots enemies within {attack.Range:0} tiles");
        if (type.IsCapital)
            parts.Add("Lose it and you lose the match");
        return string.Join("  ·  ", parts);
    }

    private static string Describe(RecruitError error) => error switch
    {
        RecruitError.NotEnoughGold => "Not enough gold",
        RecruitError.ArmyCapReached => "Army limit reached — build barracks",
        RecruitError.QueueFull => "Training queue is full",
        RecruitError.NotComplete => "Still under construction",
        RecruitError.NotOwner => "This building is not yours",
        RecruitError.CannotRecruitType => "Cannot train this unit here",
        RecruitError.UnknownBuilding => "Building no longer exists",
        _ => error.ToString(),
    };

    private static string DescribePlacement(BuildError error) => error switch
    {
        BuildError.None => "ok",
        BuildError.NotEnoughGold => "not enough gold",
        BuildError.NoUnitsNearby => "your troops must be nearby",
        BuildError.NeedsDeposit => "mines must stand on a gold deposit",
        BuildError.OnDeposit => "keep gold deposits free for mines",
        BuildError.Occupied => "something is already built here",
        BuildError.BlockedTerrain => "needs open ground (no water, rocks or roads)",
        BuildError.OutOfBounds => "outside the map",
        BuildError.NotBuildable => "cannot be built",
        _ => error.ToString(),
    };
}

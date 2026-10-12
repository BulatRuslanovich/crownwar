using System.Collections.Generic;
using System.Linq;
using Godot;
using Lordsio.Core;
using Lordsio.Sim.Core;
using Lordsio.Sim.Territory;

namespace Lordsio.UI;

/// <summary>
/// Player HUD: resources, the selected settlement's recruitment panel and short messages.
/// Reads the simulation and only ever changes it by issuing commands.
/// </summary>
public partial class Hud : CanvasLayer
{
    private const float MessageDuration = 2.5f;

    private static readonly Color ProblemColor = new("ff8a80");

    private readonly List<(Button Button, string UnitTypeId)> _recruitButtons = new();
    private MatchController _match = null!;
    private Label _gold = null!;
    private Label _income = null!;
    private Label _army = null!;
    private Control _settlementPanel = null!;
    private Label _title = null!;
    private Label _info = null!;
    private HBoxContainer _buttons = null!;
    private Label _queue = null!;
    private ProgressBar _progress = null!;
    private Label _message = null!;
    private int? _panelSettlementId;
    private float _messageTimeLeft;

    public void Initialize(MatchController match)
    {
        _match = match;
        _gold = GetNode<Label>("ResourceBar/Row/Gold");
        _income = GetNode<Label>("ResourceBar/Row/Income");
        _army = GetNode<Label>("ResourceBar/Row/Army");
        _settlementPanel = GetNode<Control>("SettlementPanel");
        _title = GetNode<Label>("SettlementPanel/Rows/Title");
        _info = GetNode<Label>("SettlementPanel/Rows/Info");
        _buttons = GetNode<HBoxContainer>("SettlementPanel/Rows/RecruitButtons");
        _queue = GetNode<Label>("SettlementPanel/Rows/Queue");
        _progress = GetNode<ProgressBar>("SettlementPanel/Rows/Progress");
        _message = GetNode<Label>("Message");
        _settlementPanel.Visible = false;
        _message.Visible = false;
    }

    public override void _Process(double delta)
    {
        var sim = _match.Sim;
        if (sim.GetFaction(MatchController.PlayerFactionId) is { } faction)
        {
            _gold.Text = $"Gold {faction.Treasury.Gold}";
            _income.Text = $"+{faction.IncomePerSecond}/s";
            _army.Text = $"Army {faction.Population}/{faction.ArmyCap}";
            _army.Modulate = faction.Population >= faction.ArmyCap ? ProblemColor : Colors.White;
        }

        UpdateSettlementPanel();

        if (_messageTimeLeft > 0f)
        {
            _messageTimeLeft -= (float)delta;
            _message.Modulate = new Color(1, 1, 1, Mathf.Clamp(_messageTimeLeft / 0.5f, 0f, 1f));
            _message.Visible = _messageTimeLeft > 0f;
        }
    }

    /// <summary>Reacts to simulation events that concern the local player.</summary>
    public void OnSimEvent(SimEvent e)
    {
        if (e.FactionId != MatchController.PlayerFactionId)
            return;
        switch (e)
        {
            case RecruitRejected rejected:
                ShowMessage(Describe(rejected.Reason));
                break;
            case UnitRecruited recruited when _match.Sim.GetUnit(recruited.UnitId) is { } unit:
                ShowMessage($"{unit.Type.Name} is ready");
                break;
        }
    }

    private void UpdateSettlementPanel()
    {
        var settlement = _match.Selection.SettlementId is { } id ? _match.Sim.GetSettlement(id) : null;
        _settlementPanel.Visible = settlement != null;
        if (settlement == null)
        {
            _panelSettlementId = null;
            return;
        }
        if (_panelSettlementId != settlement.Id)
        {
            _panelSettlementId = settlement.Id;
            RebuildRecruitButtons(settlement);
        }

        bool own = settlement.OwnerId == MatchController.PlayerFactionId;
        string owner = settlement.OwnerId == Settlement.NeutralOwner ? "neutral"
            : own ? "yours" : $"faction {settlement.OwnerId}";
        _title.Text = $"{settlement.Type.Name} ({owner})";
        _info.Text = $"Income +{settlement.Type.Income}/s   Army cap +{settlement.Type.ArmyCap}";

        _buttons.Visible = own && _recruitButtons.Count > 0;
        foreach (var (button, typeId) in _recruitButtons)
        {
            // Buttons stay clickable: the simulation is the judge and explains a refusal with a message.
            var error = _match.Sim.CanRecruit(MatchController.PlayerFactionId, settlement.Id, typeId);
            button.Modulate = error == RecruitError.None ? Colors.White : ProblemColor;
            button.TooltipText = error == RecruitError.None ? "" : Describe(error);
        }

        bool producing = own && settlement.Queue.Count > 0;
        _queue.Visible = producing;
        _progress.Visible = producing;
        if (producing)
        {
            var current = settlement.Queue[0];
            _queue.Text = $"Training {current.Type.Name}" +
                (settlement.Queue.Count > 1 ? $"  (+{settlement.Queue.Count - 1} queued)" : "");
            _progress.Value = current.Progress * 100f;
        }
    }

    private void RebuildRecruitButtons(Settlement settlement)
    {
        foreach (var (button, _) in _recruitButtons)
            button.QueueFree();
        _recruitButtons.Clear();

        foreach (string typeId in settlement.Type.Recruits)
        {
            var type = _match.Sim.Rules.Units.Get(typeId);
            var button = new Button { Text = $"{type.Name}  {type.Cost}g", FocusMode = Control.FocusModeEnum.None };
            int settlementId = settlement.Id;
            button.Pressed += () => _match.Sim.Issue(
                new RecruitCommand(MatchController.PlayerFactionId, settlementId, typeId));
            _buttons.AddChild(button);
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

    private static string Describe(RecruitError error) => error switch
    {
        RecruitError.NotEnoughGold => "Not enough gold",
        RecruitError.ArmyCapReached => "Army limit reached — capture more settlements",
        RecruitError.QueueFull => "Training queue is full",
        RecruitError.NotOwner => "This settlement is not yours",
        RecruitError.CannotRecruitType => "Cannot train this unit here",
        RecruitError.UnknownSettlement => "Settlement no longer exists",
        _ => error.ToString(),
    };
}

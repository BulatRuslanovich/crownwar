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
    private static readonly Color DefaultTextColor = new(0.95f, 0.9f, 0.8f);
    private static readonly Color NightColor = new("9fb4ff");

    /// <summary>Physical hotkey per building type id; types without one are still on the bar.</summary>
    private static readonly Dictionary<string, Key> BuildHotkeys = new()
    {
        ["mine"] = Key.M,
        ["barracks"] = Key.B,
        ["tower"] = Key.T,
        ["watchtower"] = Key.V,
        ["catapult"] = Key.C,
    };

    private readonly List<(Button Button, string UnitTypeId)> _recruitButtons = new();
    private Control _selectionPanel = null!;
    private Label _selectionTitle = null!;
    private Container _selectionTypes = null!;
    private ProgressBar _selectionHealth = null!;
    /// <summary>The selection's unit counts by type as last shown, so the buttons are rebuilt only when it changes.</summary>
    private string _shownComposition = "";
    private readonly List<(Button Button, BuildingType Type)> _buildButtons = new();
    private MatchController _match = null!;
    private BuildPlacement _placement = null!;
    private Label _gold = null!;
    private Label _income = null!;
    private Label _army = null!;
    private Label _dayNight = null!;
    private Button _demolishButton = null!;
    /// <summary>The first press arms the demolish button for a few seconds; a second press pulls the building down.</summary>
    private (int BuildingId, ulong UntilMs)? _demolishArmed;
    private const ulong DemolishConfirmMs = 3000;
    private Label _buildHint = null!;
    private Control _buildingPanel = null!;
    private Label _title = null!;
    private Label _info = null!;
    private Container _recruitRow = null!;
    private Label _queue = null!;
    private ProgressBar _progress = null!;
    private Label _upgradeTitle = null!;
    private Button _upgradeButton = null!;
    private ProgressBar _upgradeProgress = null!;
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
        _upgradeTitle = GetNode<Label>("BuildingPanel/Rows/UpgradeTitle");
        _upgradeButton = GetNode<Button>("BuildingPanel/Rows/UpgradeButton");
        _upgradeButton.FocusMode = Control.FocusModeEnum.None;
        _upgradeButton.Pressed += UpgradeSelected;
        _upgradeProgress = GetNode<ProgressBar>("BuildingPanel/Rows/UpgradeProgress");
        _demolishButton = new Button { FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        _demolishButton.AddThemeFontSizeOverride("font_size", 14);
        _demolishButton.Pressed += DemolishSelected;
        GetNode("BuildingPanel/Rows").AddChild(_demolishButton);
        _dayNight = new Label { MouseFilter = Control.MouseFilterEnum.Pass };
        GetNode("ResourceBar/Row").AddChild(_dayNight);
        _message = GetNode<Label>("Message");
        _alert = GetNode<AlertIndicator>("AlertIndicator");
        _alert.Initialize(match);
        _buildingPanel.Visible = false;
        _message.Visible = false;
        CreateBuildButtons(GetNode<HBoxContainer>("BuildBar/Rows/Buttons"));
        AddResourceIcons();
        GetNode<Minimap>("MinimapPanel/Minimap").Initialize(match);
        var scoreboard = new Scoreboard { Name = "Scoreboard" };
        AddChild(scoreboard);
        scoreboard.Initialize(match);
        _selectionPanel = GetNode<Control>("SelectionPanel");
        _selectionTitle = GetNode<Label>("SelectionPanel/Rows/Title");
        _selectionTypes = GetNode<Container>("SelectionPanel/Rows/Types");
        _selectionHealth = GetNode<ProgressBar>("SelectionPanel/Rows/Health");
        _selectionPanel.Visible = false;
        UpdateResourceBar(); // filled from the start, even if the match opens paused
    }

    public override void _Process(double delta)
    {
        UpdateResourceBar();
        if (_match.Sim.GetFaction(_match.LocalFactionId) is { } faction)
        {
            foreach (var (button, type) in _buildButtons)
            {
                button.Text = BuildButtonText(type); // follows a language change
                MarkAvailable(button, faction.Treasury.CanAfford(type.Cost) && !AtLimit(type));
                button.ButtonPressed = _placement.ActiveType == type;
            }
        }
        UpdateRest(delta);
    }

    /// <summary>An unaffordable button keeps its picture (dimmed) and shows its price in red.</summary>
    private static void MarkAvailable(Button button, bool available)
    {
        var text = available ? DefaultTextColor : ProblemColor;
        foreach (string slot in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color" })
            button.AddThemeColorOverride(slot, text);
        var icon = available ? Colors.White : new Color(1f, 1f, 1f, 0.55f);
        foreach (string slot in new[] { "icon_normal_color", "icon_hover_color", "icon_pressed_color", "icon_hover_pressed_color" })
            button.AddThemeColorOverride(slot, icon);
    }

    private void UpdateResourceBar()
    {
        if (_match.Sim.GetFaction(_match.LocalFactionId) is { } faction)
        {
            var treasury = faction.Treasury;
            _gold.Text = treasury.Capacity > 0
                ? string.Format(Tr("HUD_GOLD_CAPPED"), treasury.Gold, treasury.Capacity)
                : string.Format(Tr("HUD_GOLD"), treasury.Gold);
            _gold.Modulate = treasury.IsFull ? ProblemColor : Colors.White;
            _gold.TooltipText = treasury.Capacity > 0 ? string.Format(Tr("HUD_GOLD_CAP_TOOLTIP"), treasury.Capacity) : "";
            float net = faction.NetPerMinute / 60f;
            _income.Text = string.Format(Tr("HUD_INCOME"), net);
            _income.Modulate = net < 0f ? ProblemColor : Colors.White;
            _income.TooltipText = string.Format(Tr("HUD_INCOME_TOOLTIP"), faction.IncomePerSecond,
                faction.UpkeepPerMinute / 60f, faction.ArmyUpkeepPerMinute, _match.Sim.Rules.UpkeepAllowance);
            _army.Text = string.Format(Tr("HUD_ARMY"), faction.Population, faction.ArmyCap);
            _army.Modulate = faction.Population >= faction.ArmyCap ? ProblemColor : Colors.White;

            var sim = _match.Sim;
            _dayNight.Visible = sim.Rules.NightSeconds > 0f;
            int left = Mathf.CeilToInt(sim.SecondsToDayNightChange);
            _dayNight.Text = string.Format(Tr(sim.IsNight ? "HUD_NIGHT" : "HUD_DAY"), left / 60, left % 60);
            _dayNight.Modulate = sim.IsNight ? NightColor : Colors.White;
            _dayNight.TooltipText = Tr("HUD_NIGHT_TOOLTIP");
        }
    }

    private void UpdateRest(double delta)
    {
        // Only while placing, and only to say why it cannot go here.
        bool blocked = _placement.ActiveType != null && _placement.CurrentError != BuildError.None;
        _buildHint.Visible = blocked;
        if (blocked)
            _buildHint.Text = DescribePlacement(_placement.CurrentError);

        UpdateBuildingPanel();

        // Lockstep stall: keep the notice up for as long as we wait.
        if (_match.CatchingUp)
            ShowMessage(string.Format(Tr("NET_REJOINING"), _match.CatchUpProgress * 100f));
        else if (_match.WaitingForPlayers)
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
        if (key.PhysicalKeycode == Key.U)
        {
            UpgradeSelected();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (key.PhysicalKeycode == Key.Delete)
        {
            DemolishSelected();
            GetViewport().SetInputAsHandled();
            return;
        }
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
            case BuildingUpgradeStarted started when started.FactionId == player && _match.Sim.GetBuilding(started.BuildingId) is { } building:
                ShowMessage(string.Format(Tr("MSG_UPGRADE_STARTED"), Names.Of(building.Type), building.Type.Tier + 1));
                break;
            case BuildingUpgraded upgraded when upgraded.FactionId == player && _match.Sim.GetBuilding(upgraded.BuildingId) is { } building:
                ShowMessage(string.Format(Tr("MSG_UPGRADE_DONE"), Names.Of(building.Type), upgraded.Tier));
                break;
            case UpgradeRejected rejected when rejected.FactionId == player:
                ShowMessage(Describe(rejected.Reason));
                break;
            case BuildingDemolished demolished when demolished.FactionId == player:
                ShowMessage(string.Format(Tr("MSG_DEMOLISHED"),
                    Names.Of(_match.Sim.Rules.GetBuildingType(demolished.BuildingTypeId)), demolished.Refund));
                break;
            case GoldLooted loot when loot.FactionId == player:
                ShowMessage(string.Format(Tr("MSG_LOOTED"), loot.Amount));
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

    /// <summary>A coin before the gold and a soldier before the army size.</summary>
    private void AddResourceIcons()
    {
        var row = _gold.GetParent();
        var coin = new TextureRect { Texture = Icons.Coin(), StretchMode = TextureRect.StretchModeEnum.KeepCentered };
        row.AddChild(coin);
        row.MoveChild(coin, _gold.GetIndex());
        var soldier = new TextureRect
        {
            Texture = Icons.Unit("infantry", FactionColors.Get(_match.LocalFactionId), scale: 1),
            StretchMode = TextureRect.StretchModeEnum.KeepCentered,
        };
        row.AddChild(soldier);
        row.MoveChild(soldier, _army.GetIndex());
        ((HBoxContainer)row).AddThemeConstantOverride("separation", 10);
    }

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
                TooltipText = Names.Of(type) + "\n" + DescribeBuildingType(type)
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
            ? DescribeBuildingType(building.Type, building)
            : string.Format(Tr("HUD_UNDER_CONSTRUCTION"), building.ConstructionProgress * 100f);
        _info.Text = string.Format(Tr("HUD_HEALTH"), building.Health, building.MaxHealth) + "\n" + status;

        _recruitRow.Visible = own && building.IsComplete && _recruitButtons.Count > 0;
        foreach (var (button, typeId) in _recruitButtons)
        {
            var type = _match.Sim.Rules.Units.Get(typeId);
            button.Text = $"{type.Cost}{Tr("HUD_GOLD_SHORT")}";
            // Buttons stay clickable: the simulation is the judge and explains a refusal with a message.
            var error = _match.Sim.CanRecruit(_match.LocalFactionId, building.Id, typeId);
            MarkAvailable(button, error == RecruitError.None);
            string upkeep = Names.Of(type) + "\n" + string.Format(Tr("INFO_UPKEEP"), type.Upkeep) + "\n" + Tr("HUD_RECRUIT_SHIFT")
                            + (type.IsSiege ? "\n" + string.Format(Tr("INFO_SIEGE"), type.BuildingDamageMultiplier) : "");
            button.TooltipText = error == RecruitError.None ? upkeep : Describe(error) + "\n" + upkeep;
        }

        UpdateUpgrade(building, own);
        UpdateDemolish(building, own);

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
        string changes = DescribeUpgrade(type, next);
        _upgradeButton.TooltipText = error == UpgradeError.None ? changes : Describe(error) + "\n" + changes;
    }

    /// <summary>What the next level changes, "before → after", built from the rules so new levels describe themselves.</summary>
    private string DescribeUpgrade(BuildingType from, BuildingType to)
    {
        var lines = new List<string> { string.Format(Tr("UPGRADE_HEALTH"), from.MaxHealth, to.MaxHealth) };
        if (from.Attack is { } a && to.Attack is { } b)
        {
            if (a.Damage != b.Damage)
                lines.Add(string.Format(Tr("UPGRADE_DAMAGE"), a.Damage, b.Damage));
            if (!Mathf.IsEqualApprox(a.Range, b.Range))
                lines.Add(string.Format(Tr("UPGRADE_RANGE"), a.Range, b.Range));
            if (!Mathf.IsEqualApprox(a.SplashRadius, b.SplashRadius))
                lines.Add(string.Format(Tr("UPGRADE_SPLASH"), a.SplashRadius, b.SplashRadius));
        }
        if (to.YieldBonus != from.YieldBonus)
            lines.Add(string.Format(Tr("UPGRADE_YIELD"), from.YieldBonus, to.YieldBonus));
        lines.Add(string.Format(Tr("UPGRADE_COST_TIME"), from.UpgradeCost, from.UpgradeTime));
        return string.Join("\n", lines);
    }

    private string Describe(UpgradeError error) => Tr(error switch
    {
        UpgradeError.NotEnoughGold => "ERR_NOT_ENOUGH_GOLD",
        UpgradeError.InProgress => "ERR_UPGRADE_BUSY",
        UpgradeError.MaxTier => "ERR_UPGRADE_MAX",
        UpgradeError.NotComplete => "ERR_NOT_COMPLETE",
        _ => "ERR_NOT_OWNER",
    });

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
                int count = Input.IsKeyPressed(Key.Shift) ? 5 : 1;
                for (int i = 0; i < count; i++)
                    _match.Issue(new RecruitCommand(_match.LocalFactionId, buildingId, typeId));
            };
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
            parts.Add(instance?.Deposit is { } d ? DescribeDeposit(d, type.YieldBonus) : Tr("INFO_MINE"));
        if (type.Income > 0)
            parts.Add(string.Format(Tr("INFO_INCOME"), type.Income));
        if (type.ArmyCap > 0)
            parts.Add(string.Format(Tr("INFO_ARMY_CAP"), type.ArmyCap));
        if (type.Recruits.Count > 0)
            parts.Add(Tr("INFO_TRAINS"));
        if (type.Attack is { IsSplash: true } splash)
            parts.Add(string.Format(Tr("INFO_SPLASH"), splash.Range, splash.Damage, splash.SplashRadius));
        else if (type.Attack is { } attack)
            parts.Add(string.Format(Tr("INFO_SHOOTS"), attack.Range, attack.Damage));
        else if (type.SightRange > 0f && !type.IsCapital)
            parts.Add(string.Format(Tr("INFO_SIGHT"), type.SightRange));
        if (type.IsCapital)
            parts.Add(Tr("INFO_CAPITAL"));
        return string.Join("  ·  ", parts);
    }

    private string DescribeDeposit(Lordsio.Sim.World.GoldDeposit d, int levelBonus)
    {
        if (d.IsExhausted)
            return string.Format(Tr("INFO_DEPOSIT_EXHAUSTED"), Mathf.CeilToInt(d.RegenTicksLeft / (float)Simulation.TicksPerSecond));
        string mining = string.Format(Tr("INFO_MINING"), d.Yield + levelBonus);
        return d.IsEndless ? mining : mining + "  ·  " + string.Format(Tr("INFO_DEPOSIT_RESERVE"), d.Reserve, d.Capacity);
    }

    private string Describe(RecruitError error) => Tr(error switch
    {
        RecruitError.NotEnoughGold => "ERR_NOT_ENOUGH_GOLD",
        RecruitError.ArmyCapReached => "ERR_ARMY_CAP",
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
        BuildError.LimitReached => "BUILD_ERR_LIMIT",
        _ => "BUILD_ERR_NOT_BUILDABLE",
    });
}

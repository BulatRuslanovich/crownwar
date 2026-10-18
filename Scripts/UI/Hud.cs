using System.Collections.Generic;
using System.Linq;
using Godot;
using Warcrown.Core;
using Warcrown.Interaction;
using Warcrown.Sim.Buildings;
using Warcrown.Sim.Core;

namespace Warcrown.UI;

/// <summary>
/// Player HUD: resources, the build bar, the selected building's panel and short messages.
/// Reads the simulation and only ever changes it by issuing commands. All text goes through the
/// translation tables (Translations/strings.csv).
/// </summary>
public partial class Hud : CanvasLayer
{
    private const float MessageDuration = 3f;
    /// <summary>Recruits ordered at once by a Shift+click.</summary>
    private const int ShiftRecruitCount = 5;

    private static readonly Color ProblemColor = UiPalette.Problem;
    private static readonly Color DefaultTextColor = UiPalette.Text;
    private static readonly Color NightColor = UiPalette.Night;

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
    /// <summary>The "i" by the title: what the building does, in its tooltip.</summary>
    private Control _about = null!;
    private Container _recruitRow = null!;
    private Label _queue = null!;
    private ProgressBar _progress = null!;
    private Label _upgradeTitle = null!;
    private Button _upgradeButton = null!;
    private ProgressBar _upgradeProgress = null!;
    private MessageFeed _messages = null!;
    private AlertIndicator _alert = null!;
    private int? _panelBuildingId;
    private Button _diplomacyButton = null!;

    /// <summary>Bumped when the language changes, so every cached text is rebuilt.</summary>
    private int _textVersion;
    /// <summary>The colouring each button last got from <see cref="MarkAvailable"/>.</summary>
    private readonly Dictionary<Button, bool> _shownAvailable = new();

    /// <summary>Peace and war with the other players (P).</summary>
    public DiplomacyPanel Diplomacy { get; private set; } = null!;

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
        _title = GetNode<Label>("BuildingPanel/Rows/TitleRow/Title");
        _about = GetNode<Control>("BuildingPanel/Rows/TitleRow/About");
        _info = GetNode<Label>("BuildingPanel/Rows/Info");
        _recruitRow = GetNode<Container>("BuildingPanel/Rows/RecruitButtons");
        CreateDoctrineRow(GetNode("BuildingPanel/Rows"), _recruitRow);
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
        _messages = new MessageFeed { Name = "Messages" };
        AddChild(_messages);
        _messages.Initialize(GetNode<Label>("Message"), MessageDuration);
        _alert = GetNode<AlertIndicator>("AlertIndicator");
        _alert.Initialize(match);
        _buildingPanel.Visible = false;
        CreateBuildButtons(GetNode<HBoxContainer>("BuildBar/Rows/Buttons"));
        AddResourceIcons();
        AddWaveCounter();
        GetNode<Minimap>("MinimapPanel/Minimap").Initialize(match);
        var scoreboard = new Scoreboard { Name = "Scoreboard" };
        AddChild(scoreboard);
        scoreboard.Initialize(match);
        Diplomacy = new DiplomacyPanel { Name = "Diplomacy" };
        AddChild(Diplomacy);
        Diplomacy.Initialize(match, this);
        // On the top bar only when there is someone to deal with: bots do not do diplomacy.
        _diplomacyButton = new Button { Text = "DIPLO_BUTTON", FocusMode = Control.FocusModeEnum.None, Visible = false };
        _diplomacyButton.AddThemeFontSizeOverride("font_size", 14);
        _diplomacyButton.Pressed += Diplomacy.Toggle;
        GetNode("ResourceBar/Row").AddChild(_diplomacyButton);
        _selectionPanel = GetNode<Control>("SelectionPanel");
        _selectionTitle = GetNode<Label>("SelectionPanel/Rows/Title");
        _selectionTypes = GetNode<Container>("SelectionPanel/Rows/Types");
        _selectionHealth = GetNode<ProgressBar>("SelectionPanel/Rows/Health");
        _selectionPanel.Visible = false;
        UpdateResourceBar(0f); // filled from the start, even if the match opens paused
    }

    public override void _Process(double delta)
    {
        UpdateResourceBar((float)delta);
        UpdateWaveCounter();
        UpdateBuildButtons();
        UpdateRest(delta);
    }

    /// <summary>The language changed: every text shown is built again.</summary>
    public override void _Notification(int what)
    {
        if (what == NotificationTranslationChanged)
            _textVersion++;
    }

    /// <summary>
    /// Whether what a part of the HUD shows has changed since it was last drawn (and remembers the new state).
    /// The HUD runs every frame; its texts are formatted, translated and handed to the engine only when this says so.
    /// </summary>
    private static bool Changed<T>(ref T shown, T now)
    {
        if (EqualityComparer<T>.Default.Equals(shown, now))
            return false;
        shown = now;
        return true;
    }

    private static readonly StringName[] TextColorSlots =
        { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color" };
    private static readonly StringName[] IconColorSlots =
        { "icon_normal_color", "icon_hover_color", "icon_pressed_color", "icon_hover_pressed_color" };
    private static readonly Color DimmedIcon = new(1f, 1f, 1f, 0.55f);

    /// <summary>An unaffordable button keeps its picture (dimmed) and shows its price in red.</summary>
    private void MarkAvailable(Button button, bool available)
    {
        if (_shownAvailable.TryGetValue(button, out bool shown) && shown == available)
            return;
        _shownAvailable[button] = available;
        var text = available ? DefaultTextColor : ProblemColor;
        foreach (var slot in TextColorSlots)
            button.AddThemeColorOverride(slot, text);
        var icon = available ? Colors.White : DimmedIcon;
        foreach (var slot in IconColorSlots)
            button.AddThemeColorOverride(slot, icon);
    }

    private void UpdateRest(double delta)
    {
        // Only while placing, and only to say why it cannot go here.
        // Nobody near is no obstacle while someone can walk over to build.
        bool blocked = _placement.ActiveType != null && _placement.CurrentError != BuildError.None && _placement.Builder == null;
        _buildHint.Visible = blocked;
        if (blocked)
            _buildHint.Text = HudText.DescribePlacement(_placement.CurrentError);

        UpdateBuildingPanel();
        _diplomacyButton.Visible = Diplomacy.HasCounterparts;

        // Lockstep stall: keep the notice up for as long as we wait.
        if (_match.CatchingUp)
            ShowMessage(string.Format(Tr("NET_REJOINING"), _match.CatchUpProgress * 100f));
        else if (_match.WaitingForPlayers)
            ShowMessage(Tr("NET_WAITING"));


    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false, CtrlPressed: false } key)
            return;
        if (key.PhysicalKeycode == Key.P)
        {
            Diplomacy.Toggle();
            GetViewport().SetInputAsHandled();
            return;
        }
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
}

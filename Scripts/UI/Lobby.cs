using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Lordsio.Net;
using Lordsio.Sim.AI;
using Lordsio.Sim.Core;
using Lordsio.Sim.Net;

namespace Lordsio.UI;

/// <summary>
/// The lobby of a network game: four slots, each a player, a bot or open, on one of four teams.
/// The host picks the mode, fills open slots with bots, sets teams and starts; a client may only change its own team.
/// In Defense there are no bots and no teams: everyone holds the citadel together.
/// </summary>
public partial class Lobby : Control
{
    private Button _leaveButton = null!;
    private const int KindOpen = 0;
    private const int KindPlayer = 4;
    /// <summary>Bot difficulties in the order of the slot menu items after "open".</summary>

    private NetSession _session = null!;
    private Label _info = null!;
    private Label _hint = null!;
    private Button _start = null!;
    private OptionButton _mode = null!;
    private Label _modeHint = null!;
    private static readonly MatchMode[] Modes = { MatchMode.Versus, MatchMode.Defense };
    private readonly List<(Label Name, OptionButton Kind, OptionButton Team)> _rows = new();

    /// <summary>Raised when the player leaves the lobby (the host closing it ends the game for everyone).</summary>
    public event Action? LeaveRequested;

    public void Initialize(NetSession session) => _session = session;

    public override void _Ready()
    {
        _info = GetNode<Label>("Center/Panel/Rows/Info");
        _hint = GetNode<Label>("Center/Panel/Rows/Hint");
        _start = GetNode<Button>("Center/Panel/Rows/Buttons/Start");
        _start.Pressed += _session.StartMatch;
        (_leaveButton = GetNode<Button>("Center/Panel/Rows/Buttons/Leave")).Pressed += () => LeaveRequested?.Invoke();

        CreateModeRow(GetNode<VBoxContainer>("Center/Panel/Rows"));
        var grid = GetNode<GridContainer>("Center/Panel/Rows/Slots");
        for (int i = 0; i < NetSession.MaxSlots; i++)
            _rows.Add(CreateRow(grid, i));

        _info.Text = _session.IsHost ? string.Format(Tr("NET_HOST_ADDRESS"), string.Join(", ", LocalAddresses())) : "";
        _info.Visible = _session.IsHost;
        _start.Visible = _session.IsHost;

        _session.LobbyUpdated += Refresh;
        Refresh();
        (_session.IsHost ? _start : _leaveButton).GrabFocus();
    }

    public override void _ExitTree() => _session.LobbyUpdated -= Refresh;

    /// <summary>The mode picker under the address: the host chooses, everyone else sees the choice.</summary>
    private void CreateModeRow(VBoxContainer rows)
    {
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 10);
        row.AddChild(new Label { Text = "NET_MODE" });
        _mode = new OptionButton();
        foreach (var mode in Modes)
            _mode.AddItem(ModeKey(mode));
        _mode.ItemSelected += item => _session.SetMode(Modes[item]);
        row.AddChild(_mode);
        _modeHint = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color(1, 1, 1, 0.7f) };
        rows.AddChild(row);
        rows.AddChild(_modeHint);
        rows.MoveChild(row, GetNode("Center/Panel/Rows/Info").GetIndex() + 1);
        rows.MoveChild(_modeHint, row.GetIndex() + 1);
    }

    public static string ModeKey(MatchMode mode) => mode == MatchMode.Defense ? "MODE_DEFENSE" : "MODE_VERSUS";

    private (Label, OptionButton, OptionButton) CreateRow(GridContainer grid, int index)
    {
        var name = new Label { CustomMinimumSize = new Vector2(200, 0), ClipText = true };
        var kind = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        kind.AddItem("NET_SLOT_OPEN");
        foreach (var difficulty in Difficulties.All)
            kind.AddItem(Difficulties.SlotKey(difficulty));
        kind.AddItem("NET_SLOT_PLAYER");
        kind.SetItemDisabled(KindPlayer, true); // players take slots by joining, not from this menu
        kind.ItemSelected += item => _session.SetSlot(index,
            item == KindOpen ? NetSlotKind.Empty : NetSlotKind.Bot,
            item == KindOpen ? BotDifficulty.Normal : Difficulties.All[item - 1]);

        var team = new OptionButton();
        for (int t = 0; t < NetSession.MaxSlots; t++)
            team.AddItem(string.Format(Tr("NET_TEAM_N"), t + 1));
        team.ItemSelected += item =>
        {
            if (_session.IsHost)
                _session.SetTeam(index, (int)item);
            else
                _session.RequestTeam((int)item);
        };

        grid.AddChild(name);
        grid.AddChild(kind);
        grid.AddChild(team);
        return (name, kind, team);
    }

    private void Refresh()
    {
        var slots = _session.Slots;
        bool defense = _session.Mode == MatchMode.Defense;
        _mode.Selected = Array.IndexOf(Modes, _session.Mode);
        _mode.Disabled = !_session.IsHost;
        _modeHint.Text = Tr(defense ? "MODE_DEFENSE_HINT" : "MODE_VERSUS_HINT");
        int own = slots.FindIndex(s => s.Kind == NetSlotKind.Human && s.PeerId == _session.LocalPeerId);
        for (int i = 0; i < _rows.Count; i++)
        {
            var (name, kind, team) = _rows[i];
            if (i >= slots.Count)
            {
                name.Text = "";
                kind.Disabled = team.Disabled = true;
                continue;
            }
            var slot = slots[i];
            name.Text = slot.Kind switch
            {
                NetSlotKind.Human => $"{i + 1}. {slot.Name}" + (i == own ? " " + Tr("NET_YOU") : "") + (slot.PeerId == 1 ? " " + Tr("NET_HOST_MARK") : ""),
                NetSlotKind.Bot => $"{i + 1}. {Tr("NET_BOT")}",
                _ => $"{i + 1}. —",
            };
            kind.Selected = slot.Kind switch
            {
                NetSlotKind.Human => KindPlayer,
                NetSlotKind.Bot => 1 + Math.Max(0, Array.IndexOf(Difficulties.All, (BotDifficulty)slot.Difficulty)),
                _ => KindOpen,
            };
            kind.Disabled = !_session.IsHost || slot.Kind == NetSlotKind.Human;
            for (int item = 1; item < KindPlayer; item++)
                kind.SetItemDisabled(item, defense); // no bots against the waves
            team.Selected = slot.Team;
            // Hidden in place (not Visible = false, which would shift the grid's cells): no teams in Defense.
            team.Modulate = defense ? Colors.Transparent : Colors.White;
            team.Disabled = defense || slot.Kind == NetSlotKind.Empty || !(_session.IsHost || i == own);
        }

        if (_session.IsHost)
        {
            _start.Disabled = !_session.CanStart;
            _hint.Text = _session.CanStart ? Tr(defense ? "NET_HINT_HOST_DEFENSE" : "NET_HINT_HOST") : Tr("NET_NEED_TWO_TEAMS");
        }
        else
        {
            _hint.Text = Tr("NET_WAITING_HOST_START");
        }
    }

    /// <summary>This PC's IPv4 addresses on the local network, for players who cannot find the game by themselves.</summary>
    private static IEnumerable<string> LocalAddresses()
    {
        var addresses = Net.LanDiscovery.LocalAddresses().ToList();
        return addresses.Count > 0 ? addresses : new List<string> { "127.0.0.1" };
    }
}

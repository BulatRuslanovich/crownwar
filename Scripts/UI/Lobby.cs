using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Lordsio.Net;
using Lordsio.Sim.AI;
using Lordsio.Sim.Net;

namespace Lordsio.UI;

/// <summary>
/// The lobby of a network game: four slots, each a player, a bot or open, on one of four teams.
/// The host fills open slots with bots, sets teams and starts; a client may only change its own team.
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
            team.Selected = slot.Team;
            team.Disabled = slot.Kind == NetSlotKind.Empty || !(_session.IsHost || i == own);
        }

        if (_session.IsHost)
        {
            _start.Disabled = !_session.CanStart;
            _hint.Text = _session.CanStart ? Tr("NET_HINT_HOST") : Tr("NET_NEED_TWO_TEAMS");
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

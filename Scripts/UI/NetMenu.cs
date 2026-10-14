using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Lordsio.Core;
using Lordsio.Net;

namespace Lordsio.UI;

/// <summary>
/// Network game screen: pick a name, host a game, or join one found on the local network
/// (or typed in by IP address). Switching to the lobby is up to <see cref="Main"/>, which listens to the session.
/// </summary>
public partial class NetMenu : Control
{
    private const double RefreshInterval = 0.5;

    private NetSession _session = null!;
    private string? _problemKey;
    private LanDiscovery _discovery = null!;
    private LineEdit _name = null!;
    private LineEdit _address = null!;
    private VBoxContainer _games = null!;
    private Label _empty = null!;
    private Label _status = null!;
    private Button _host = null!;
    private Button _join = null!;
    private readonly Dictionary<string, Button> _gameButtons = new();
    private double _sinceRefresh = RefreshInterval;

    /// <summary>Raised when the player goes back to the main menu.</summary>
    public event Action? BackRequested;

    /// <param name="problemKey">Why we came back here (connection lost, rejected...), shown as the status line.</param>
    public void Initialize(NetSession session, string? problemKey)
    {
        _session = session;
        _problemKey = problemKey;
    }

    public override void _Ready()
    {
        _name = GetNode<LineEdit>("Center/Panel/Rows/NameRow/Name");
        _address = GetNode<LineEdit>("Center/Panel/Rows/AddressRow/Address");
        _games = GetNode<VBoxContainer>("Center/Panel/Rows/Games");
        _empty = GetNode<Label>("Center/Panel/Rows/Games/Empty");
        _status = GetNode<Label>("Center/Panel/Rows/Status");
        _host = GetNode<Button>("Center/Panel/Rows/Host");
        _join = GetNode<Button>("Center/Panel/Rows/AddressRow/Join");

        _name.Text = GameSettings.Instance?.PlayerName ?? "Player";
        _status.Text = _problemKey != null ? Tr(_problemKey) : "";

        _host.Pressed += Host;
        if (MatchSaveStore.TryLoad() is { } save)
        {
            // The host's last match was cut short (a crash, or the host left): offer to bring it back.
            int minutes = save.Frames.Count / Sim.Core.Simulation.TicksPerSecond / 60;
            var resume = new Button { Text = string.Format(Tr("NET_RESUME_SAVED"), save.Start.Seed, minutes) };
            resume.Pressed += () => Resume(save);
            var rows = GetNode<Control>("Center/Panel/Rows");
            rows.AddChild(resume);
            rows.MoveChild(resume, _host.GetIndex() + 1);
        }
        _join.Pressed += () => Join(_address.Text);
        _address.TextSubmitted += Join;
        GetNode<Button>("Center/Panel/Rows/Back").Pressed += () => BackRequested?.Invoke();

        _discovery = new LanDiscovery();
        AddChild(_discovery);
        _discovery.Listen();
        if (!_discovery.IsListening)
            _empty.Text = "NET_DISCOVERY_UNAVAILABLE";

        _host.GrabFocus();
    }

    public override void _Process(double delta)
    {
        _sinceRefresh += delta;
        if (_sinceRefresh < RefreshInterval)
            return;
        _sinceRefresh = 0;
        RefreshGames();
    }

    /// <summary>Updates the found-games list in place, so a button does not vanish under the cursor.</summary>
    private void RefreshGames()
    {
        var seen = new HashSet<string>();
        foreach (var game in _discovery.Games)
        {
            string key = game.Id;
            seen.Add(key);
            if (!_gameButtons.TryGetValue(key, out var button))
            {
                button = new Button { Alignment = HorizontalAlignment.Left };
                button.Pressed += () =>
                {
                    if (_discovery.Games.FirstOrDefault(g => g.Id == key) is { } current)
                        Join(current.Address);
                };
                _games.AddChild(button);
                _gameButtons[key] = button;
            }
            bool sameVersion = game.Version == GameVersion.Current;
            string text = string.Format(Tr("NET_GAME_ENTRY"), game.HostName, game.Players, game.Players + game.FreeSlots, game.Address);
            if (!sameVersion)
                text += " — " + Tr("NET_OTHER_VERSION");
            else if (game.InMatch)
                text += " — " + Tr(game.FreeSlots > 0 ? "NET_GAME_REJOIN" : "NET_GAME_RUNNING");
            else if (game.FreeSlots == 0)
                text += " — " + Tr("NET_GAME_FULL");
            button.Text = text;
            button.Disabled = !sameVersion || game.FreeSlots == 0;
        }
        foreach (var key in new List<string>(_gameButtons.Keys))
        {
            if (seen.Contains(key))
                continue;
            _gameButtons[key].QueueFree();
            _gameButtons.Remove(key);
        }
        _empty.Visible = _gameButtons.Count == 0;
    }

    private string SaveName()
    {
        string name = _name.Text.Trim();
        if (GameSettings.Instance is { } settings && name.Length > 0 && name != settings.PlayerName)
        {
            settings.PlayerName = name;
            settings.Save();
        }
        return name;
    }

    private void Host()
    {
        // The lobby takes over the discovery port's role; stop listening first so a game hosted
        // on this PC does not show up in our own list.
        _discovery.Stop();
        var error = _session.Host(SaveName());
        if (error != Error.Ok)
        {
            _status.Text = Tr("NET_HOST_FAILED");
            _discovery.Listen();
        }
    }

    private void Resume(Sim.Net.SavedMatch save)
    {
        _discovery.Stop();
        var error = _session.HostResume(SaveName(), save);
        if (error != Error.Ok)
        {
            _status.Text = Tr("NET_HOST_FAILED");
            _discovery.Listen();
        }
    }

    private void Join(string address)
    {
        address = address.Trim();
        if (address.Length == 0)
        {
            _status.Text = Tr("NET_ENTER_ADDRESS");
            _address.GrabFocus();
            return;
        }
        var error = _session.Join(address, SaveName());
        if (error != Error.Ok)
        {
            _status.Text = Tr("NET_CONNECT_FAILED");
            return;
        }
        _status.Text = string.Format(Tr("NET_CONNECTING"), address);
        _host.Disabled = true;
        _join.Disabled = true;
    }
}

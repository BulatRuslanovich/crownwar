using System;
using System.Globalization;
using Godot;
using Warcrown.Core;
using Warcrown.Sim.AI;

namespace Warcrown.UI;

/// <summary>Title screen with match setup (opponents, difficulty, seed), settings and quit.</summary>
public partial class MainMenu : Control
{
    private Button _startButton = null!;

    private MatchConfig _initial = MatchConfig.Default;
    private SpinBox _bots = null!;
    private OptionButton _difficulty = null!;
    private LineEdit _seed = null!;
    private Control _menuPanel = null!;
    private SettingsPanel _settings = null!;
    private HowToPlay _help = null!;

    /// <summary>Raised with the chosen setup when the player presses Start.</summary>
    public event Action<MatchConfig>? StartRequested;

    /// <summary>Raised when the player opens the network game screen.</summary>
    public event Action? NetworkRequested;

    /// <summary>Raised when the player quits the game.</summary>
    public event Action? QuitRequested;

    /// <summary>Opens the "How to play" panel (also used by a dev flag for screenshots).</summary>
    public void ShowHelp()
    {
        _menuPanel.Visible = false;
        _help.Visible = true;
    }

    /// <summary>Pre-fills the form, e.g. with the setup of the match just left.</summary>
    public void Initialize(MatchConfig config) => _initial = config;

    public override void _Ready()
    {
        _menuPanel = GetNode<Control>("Center");
        _bots = GetNode<SpinBox>("Center/Panel/Rows/Grid/Bots");
        _difficulty = GetNode<OptionButton>("Center/Panel/Rows/Grid/Difficulty");
        _seed = GetNode<LineEdit>("Center/Panel/Rows/Grid/Seed");
        _settings = GetNode<SettingsPanel>("Settings");
        _help = GetNode<HowToPlay>("HowToPlay");
        GetNode<Label>("Version").Text = "v" + Net.GameVersion.Number;

        foreach (var difficulty in Difficulties.All)
            _difficulty.AddItem(Difficulties.MenuKey(difficulty));

        _bots.Value = _initial.BotCount;
        _difficulty.Selected = Math.Max(0, Array.IndexOf(Difficulties.All, _initial.Difficulty));
        _seed.Text = _initial.Seed?.ToString(CultureInfo.InvariantCulture) ?? "";

        (_startButton = GetNode<Button>("Center/Panel/Rows/Start")).Pressed += Start;
        GetNode<Button>("Center/Panel/Rows/Network").Pressed += () => NetworkRequested?.Invoke();
        GetNode<Button>("Center/Panel/Rows/Help").Pressed += () =>
        {
            _menuPanel.Visible = false;
            _help.Visible = true;
        };
        _help.Closed += () => _menuPanel.Visible = true;
        GetNode<Button>("Center/Panel/Rows/Settings").Pressed += () =>
        {
            _menuPanel.Visible = false;
            _settings.Visible = true;
        };
        GetNode<Button>("Center/Panel/Rows/Quit").Pressed += () => QuitRequested?.Invoke();
        _settings.Closed += () => _menuPanel.Visible = true;
        _settings.Visible = false;
        _startButton.GrabFocus();
    }

    private void Start()
    {
        int? seed = int.TryParse(_seed.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int s) ? s : null;
        StartRequested?.Invoke(MatchConfig.Single((int)_bots.Value, Difficulties.All[Math.Max(0, _difficulty.Selected)], seed));
    }
}

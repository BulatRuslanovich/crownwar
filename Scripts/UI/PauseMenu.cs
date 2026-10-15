using Godot;
using Lordsio.Core;
using Lordsio.Interaction;

namespace Lordsio.UI;

/// <summary>
/// Esc menu: resume, restart, settings, leave to the main menu. Runs while the tree is paused
/// (process mode Always is set in the scene).
/// </summary>
public partial class PauseMenu : CanvasLayer
{
    private Button _resumeButton = null!;
    private MatchController _match = null!;
    private Label _pausedBy = null!;

    /// <summary>In a network game: who paused it (null hides the line).</summary>
    public void ShowPausedBy(string? name)
    {
        _pausedBy.Visible = name != null;
        if (name != null)
            _pausedBy.Text = string.Format(Tr("NET_PAUSED_BY"), name);
    }
    private BuildPlacement _placement = null!;
    private Control _menu = null!;
    private SettingsPanel _settings = null!;
    private HowToPlay _help = null!;

    public void Initialize(MatchController match, BuildPlacement placement)
    {
        _match = match;
        _placement = placement;
        _menu = GetNode<Control>("Dim/Center");
        _settings = GetNode<SettingsPanel>("Settings");
        _help = GetNode<HowToPlay>("HowToPlay");
        GetNode<Button>("Dim/Center/Panel/Rows/Help").Pressed += () =>
        {
            _menu.Visible = false;
            _help.Visible = true;
        };
        _help.Closed += () => _menu.Visible = true;
        (_resumeButton = GetNode<Button>("Dim/Center/Panel/Rows/Resume")).Pressed += () => _match.SetPaused(false);
        _pausedBy = new Label { HorizontalAlignment = HorizontalAlignment.Center, Visible = false };
        var rows = GetNode<Control>("Dim/Center/Panel/Rows");
        rows.AddChild(_pausedBy);
        rows.MoveChild(_pausedBy, 1);
        var restart = GetNode<Button>("Dim/Center/Panel/Rows/Restart");
        restart.Pressed += () => _match.RequestRestart();
        restart.Visible = _match.Net == null;
        GetNode<Button>("Dim/Center/Panel/Rows/Settings").Pressed += () =>
        {
            _menu.Visible = false;
            _settings.Visible = true;
        };
        GetNode<Button>("Dim/Center/Panel/Rows/Menu").Pressed += () => _match.RequestMenu();
        _settings.Closed += () => _menu.Visible = true;
        _settings.Visible = false;
        Visible = false;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { PhysicalKeycode: Key.Escape, Pressed: true, Echo: false })
            return;
        if (_settings.Visible || _help.Visible)
            return; // those panels close themselves
        if (!_match.IsPaused && _placement.IsActive)
            return; // Esc cancels building placement first
        _match.SetPaused(!_match.IsPaused);
        if (_match.IsPaused)
            _resumeButton.GrabFocus();
        GetViewport().SetInputAsHandled();
    }
}

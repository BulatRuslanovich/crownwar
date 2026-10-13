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
    private MatchController _match = null!;
    private BuildPlacement _placement = null!;
    private Control _menu = null!;
    private SettingsPanel _settings = null!;

    public void Initialize(MatchController match, BuildPlacement placement)
    {
        _match = match;
        _placement = placement;
        _menu = GetNode<Control>("Dim/Center");
        _settings = GetNode<SettingsPanel>("Settings");
        GetNode<Button>("Dim/Center/Panel/Rows/Resume").Pressed += () => _match.SetPaused(false);
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
        if (_settings.Visible)
            return; // the settings panel closes itself
        if (!_match.IsPaused && _placement.IsActive)
            return; // Esc cancels building placement first
        _match.SetPaused(!_match.IsPaused);
        if (_match.IsPaused)
            GetNode<Button>("Dim/Center/Panel/Rows/Resume").GrabFocus();
        GetViewport().SetInputAsHandled();
    }
}

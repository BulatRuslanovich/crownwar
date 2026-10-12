using System;
using Godot;
using Lordsio.Core;

namespace Lordsio.UI;

/// <summary>Edits <see cref="GameSettings"/>; changes apply immediately and are saved on Back.</summary>
public partial class SettingsPanel : Control
{
    private HSlider _volume = null!;
    private CheckBox _fullscreen = null!;
    private OptionButton _uiScale = null!;
    private CheckBox _edgePan = null!;
    private OptionButton _language = null!;
    private bool _loading;

    /// <summary>Raised when the player closes the panel.</summary>
    public event Action? Closed;

    public override void _Ready()
    {
        _volume = GetNode<HSlider>("Center/Panel/Rows/Grid/Volume");
        _fullscreen = GetNode<CheckBox>("Center/Panel/Rows/Grid/Fullscreen");
        _uiScale = GetNode<OptionButton>("Center/Panel/Rows/Grid/UiScale");
        _edgePan = GetNode<CheckBox>("Center/Panel/Rows/Grid/EdgePan");
        _language = GetNode<OptionButton>("Center/Panel/Rows/Grid/Language");

        foreach (float scale in GameSettings.UiScales)
            _uiScale.AddItem($"{scale * 100:0}%");
        _language.AddItem("SETTINGS_LANGUAGE_SYSTEM");
        _language.AddItem("English");
        _language.AddItem("Русский");

        _volume.ValueChanged += _ => Changed();
        _fullscreen.Toggled += _ => Changed();
        _uiScale.ItemSelected += _ => Changed();
        _edgePan.Toggled += _ => Changed();
        _language.ItemSelected += _ => Changed();
        GetNode<Button>("Center/Panel/Rows/Back").Pressed += Close;
        VisibilityChanged += () =>
        {
            if (Visible)
                LoadValues();
        };
        LoadValues();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (Visible && @event is InputEventKey { PhysicalKeycode: Key.Escape, Pressed: true })
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    private void LoadValues()
    {
        if (GameSettings.Instance is not { } s)
            return;
        _loading = true;
        _volume.Value = s.MasterVolume;
        _fullscreen.ButtonPressed = s.Fullscreen;
        _uiScale.Selected = Math.Max(0, Array.IndexOf(GameSettings.UiScales, s.UiScale));
        _edgePan.ButtonPressed = s.EdgePan;
        _language.Selected = Math.Max(0, Array.IndexOf(GameSettings.Languages, s.Language));
        _loading = false;
    }

    private void Changed()
    {
        if (_loading || GameSettings.Instance is not { } s)
            return;
        s.MasterVolume = (float)_volume.Value;
        s.Fullscreen = _fullscreen.ButtonPressed;
        s.UiScale = GameSettings.UiScales[Math.Max(0, _uiScale.Selected)];
        s.EdgePan = _edgePan.ButtonPressed;
        s.Language = GameSettings.Languages[Math.Max(0, _language.Selected)];
        s.Apply();
    }

    private void Close()
    {
        GameSettings.Instance?.Save();
        Visible = false;
        Closed?.Invoke();
    }
}

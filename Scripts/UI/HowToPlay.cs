using System;
using Godot;

namespace Warcrown.UI;

/// <summary>Rules in brief and the controls, reachable from the main menu and the pause menu.</summary>
public partial class HowToPlay : Control
{
    private Button _backButton = null!;
    private RichTextLabel _text = null!;

    /// <summary>Raised when the player closes the panel.</summary>
    public event Action? Closed;

    public override void _Ready()
    {
        _text = GetNode<RichTextLabel>("Center/Panel/Rows/Text");
        (_backButton = GetNode<Button>("Center/Panel/Rows/Back")).Pressed += Close;
        VisibilityChanged += () =>
        {
            if (!Visible)
                return;
            _text.Text = Tr("HELP_TEXT"); // follows a language change
            _text.ScrollToLine(0);
            _backButton.GrabFocus();
        };
        _text.Text = Tr("HELP_TEXT");
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (Visible && @event is InputEventKey { PhysicalKeycode: Key.Escape, Pressed: true })
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    private void Close()
    {
        Visible = false;
        Closed?.Invoke();
    }
}

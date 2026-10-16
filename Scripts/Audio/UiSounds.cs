using Godot;

namespace Lordsio.Audio;

/// <summary>
/// A soft click for every button press (the button sinks a little under it) and a faint tick on hover, for all screens. Lives under
/// <see cref="Core.Main"/> and hooks every button as it enters the tree, so no screen needs to know about it.
/// </summary>
public partial class UiSounds : Node
{
    private AudioStreamPlayer _click = null!;
    private AudioStreamPlayer _hover = null!;

    public override void _EnterTree()
    {
        ProcessMode = ProcessModeEnum.Always; // menus also work while the game is paused
        _click = new AudioStreamPlayer { Stream = SoundBank.UiClick(), VolumeDb = -12f, Bus = SoundBank.Bus };
        _hover = new AudioStreamPlayer { Stream = SoundBank.UiHover(), VolumeDb = -22f, Bus = SoundBank.Bus };
        AddChild(_click);
        AddChild(_hover);
        GetTree().NodeAdded += OnNodeAdded;
    }

    public override void _ExitTree()
    {
        GetTree().NodeAdded -= OnNodeAdded;
        foreach (var player in new[] { _click, _hover })
        {
            player.Stop();
            player.Stream = null;
        }
    }

    /// <summary>Silences the sounds at once (before quitting, like the music).</summary>
    public void StopAll()
    {
        _click.Stop();
        _hover.Stop();
    }

    private void OnNodeAdded(Node node)
    {
        if (node is not BaseButton button)
            return;
        button.Pressed += () => _click.Play();
        if (button is Button pressable)
        {
            pressable.ButtonDown += () => UI.UiMotion.Squeeze(pressable, down: true);
            pressable.ButtonUp += () => UI.UiMotion.Squeeze(pressable, down: false);
        }
        button.MouseEntered += () =>
        {
            if (!button.Disabled)
                _hover.Play();
        };
    }
}

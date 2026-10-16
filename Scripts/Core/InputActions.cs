using Godot;

namespace Warcrown.Core;

/// <summary>
/// Input actions registered from code so they live next to the code that reads them.
/// Physical keycodes keep WASD in place on non-QWERTY / non-Latin layouts.
/// </summary>
public static class InputActions
{
    public const string CameraLeft = "camera_left";
    public const string CameraRight = "camera_right";
    public const string CameraUp = "camera_up";
    public const string CameraDown = "camera_down";

    public static void Register()
    {
        Add(CameraLeft, Key.A, Key.Left);
        Add(CameraRight, Key.D, Key.Right);
        Add(CameraUp, Key.W, Key.Up);
        Add(CameraDown, Key.S, Key.Down);
    }

    private static void Add(string action, params Key[] keys)
    {
        if (InputMap.HasAction(action))
            return;
        InputMap.AddAction(action);
        foreach (var key in keys)
            InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
    }
}

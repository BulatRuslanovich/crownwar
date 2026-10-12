using Godot;
using Lordsio.Core;

namespace Lordsio.World;

/// <summary>
/// RTS camera: keyboard / screen-edge panning, middle-mouse drag, wheel zoom towards the cursor.
/// Zoom uses fixed steps so pixel art stays crisp at the integer levels. Position is the view center.
/// </summary>
public partial class CameraController : Camera2D
{
    private static readonly float[] ZoomLevels = { 0.5f, 1f, 2f, 3f, 4f };

    /// <summary>Pan speed in screen pixels per second, so it feels the same at every zoom level.</summary>
    [Export] public float PanSpeed { get; set; } = 900f;
    [Export] public int EdgeMargin { get; set; } = 6;
    [Export] public bool EdgePanEnabled { get; set; } = true;

    private int _zoomIndex = 2;
    private Rect2 _bounds = new(Vector2.Zero, new Vector2(1e6f, 1e6f));
    private bool _dragging;
    // Stays false until the cursor actually enters the window; otherwise a stale cursor
    // position from before the window opened could trigger edge panning.
    private bool _mouseInWindow;

    public float ZoomLevel => ZoomLevels[_zoomIndex];

    public override void _Ready()
    {
        Zoom = Vector2.One * ZoomLevel;
        GetWindow().MouseEntered += OnMouseEntered;
        GetWindow().MouseExited += OnMouseExited;
    }

    public override void _ExitTree()
    {
        GetWindow().MouseEntered -= OnMouseEntered;
        GetWindow().MouseExited -= OnMouseExited;
    }

    public void SetZoomIndex(int index)
    {
        _zoomIndex = Mathf.Clamp(index, 0, ZoomLevels.Length - 1);
        Zoom = Vector2.One * ZoomLevel;
        ClampToBounds();
    }

    public void SetBounds(Rect2 bounds)
    {
        _bounds = bounds;
        ClampToBounds();
    }

    public void CenterOn(Vector2 worldPosition)
    {
        Position = worldPosition;
        ClampToBounds();
    }

    public override void _Process(double delta)
    {
        Vector2 dir = Godot.Input.GetVector(
            InputActions.CameraLeft, InputActions.CameraRight, InputActions.CameraUp, InputActions.CameraDown);

        if (EdgePanEnabled && _mouseInWindow && !_dragging && GetWindow().HasFocus())
        {
            Vector2 mouse = GetViewport().GetMousePosition();
            Vector2 size = GetViewportRect().Size;
            if (mouse.X <= EdgeMargin) dir.X -= 1;
            if (mouse.X >= size.X - EdgeMargin) dir.X += 1;
            if (mouse.Y <= EdgeMargin) dir.Y -= 1;
            if (mouse.Y >= size.Y - EdgeMargin) dir.Y += 1;
        }

        if (dir != Vector2.Zero)
            Position += dir.LimitLength(1f) * PanSpeed * (float)delta / ZoomLevel;

        // Every frame, so a window resize can never leave the view outside the map.
        ClampToBounds();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Middle } middle:
                _dragging = middle.Pressed;
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true } up:
                ZoomStep(+1, up.Position);
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true } down:
                ZoomStep(-1, down.Position);
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseMotion motion when _dragging:
                Position -= motion.Relative / ZoomLevel;
                ClampToBounds();
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    private void ZoomStep(int direction, Vector2 screenPoint)
    {
        int next = Mathf.Clamp(_zoomIndex + direction, 0, ZoomLevels.Length - 1);
        if (next == _zoomIndex)
            return;

        // Keep the world point under the cursor fixed while zooming.
        Vector2 fromCenter = screenPoint - GetViewportRect().Size / 2;
        Vector2 worldUnderCursor = Position + fromCenter / ZoomLevel;
        _zoomIndex = next;
        Zoom = Vector2.One * ZoomLevel;
        Position = worldUnderCursor - fromCenter / ZoomLevel;
        ClampToBounds();
    }

    private void ClampToBounds()
    {
        Vector2 half = GetViewportRect().Size / (2 * ZoomLevel);
        Position = new Vector2(ClampAxis(Position.X, _bounds.Position.X, _bounds.End.X, half.X),
                               ClampAxis(Position.Y, _bounds.Position.Y, _bounds.End.Y, half.Y));
    }

    /// <summary>Keeps the view inside [min, max]; centers it when the view is larger than the bounds.</summary>
    private static float ClampAxis(float value, float min, float max, float halfView) =>
        max - min <= 2 * halfView ? (min + max) / 2 : Mathf.Clamp(value, min + halfView, max - halfView);

    private void OnMouseEntered() => _mouseInWindow = true;
    private void OnMouseExited() => _mouseInWindow = false;
}

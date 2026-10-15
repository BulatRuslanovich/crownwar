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

    /// <summary>Off for the menu backdrop: no keys, mouse or screen edges move the camera.</summary>
    public bool UserControl { get; set; } = true;

    /// <summary>Moves the view towards a point at a gentle, frame-rate independent pace.</summary>
    public void GlideTowards(Vector2 worldPosition, float tilesPerSecond, double delta)
    {
        var step = worldPosition - Position;
        float max = tilesPerSecond * SimConvert.TileSize * (float)delta;
        Position += step.LimitLength(max);
        ClampToBounds();
    }

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

    /// <summary>Shake strength 0..1, fading by itself; the camera sways by its square (small knocks stay gentle).</summary>
    private float _trauma;
    private const float MaxShakePixels = 7f;
    private const float TraumaFadePerSecond = 1.8f;
    /// <summary>How fast the sway swings (radians a second of the slowest wave).</summary>
    private const float ShakeSpeed = 42f;
    private float _shakeTime;
    private float _shakePhase;

    /// <summary>A knock to the screen (a stone landing, a building collapsing): adds up, capped at 1.</summary>
    public void Shake(float amount)
    {
        float strength = amount * (GameSettings.Instance?.ScreenShake ?? 1f);
        if (strength <= 0f)
            return;
        if (_trauma <= 0f)
            _shakePhase = GD.Randf() * Mathf.Tau; // each knock sways its own way
        _trauma = Mathf.Min(1f, _trauma + strength);
    }

    /// <summary>Jitters the view through the offset, which leaves the camera's position and bounds alone.</summary>
    private void UpdateShake(double delta)
    {
        if (_trauma <= 0f)
        {
            Offset = Vector2.Zero;
            return;
        }
        _trauma = Mathf.Max(0f, _trauma - TraumaFadePerSecond * (float)delta);
        float strength = _trauma * _trauma * MaxShakePixels / ZoomLevel; // the same on screen at every zoom
        // A sum of waves at unrelated speeds: a smooth, irregular sway rather than the buzz of a new random point each frame.
        _shakeTime += (float)delta * ShakeSpeed;
        float t = _shakeTime + _shakePhase;
        var sway = new Vector2(Mathf.Sin(t * 1.0f) + 0.5f * Mathf.Sin(t * 2.7f + 1.3f),
            Mathf.Sin(t * 1.3f + 2.1f) + 0.5f * Mathf.Sin(t * 3.1f)) / 1.5f;
        Offset = sway * strength;
    }

    /// <summary>The part of the world on screen now (at the current zoom), in world pixels.</summary>
    public Rect2 VisibleRect
    {
        get
        {
            var size = GetViewportRect().Size / ZoomLevel;
            return new Rect2(GetScreenCenterPosition() - size / 2f, size);
        }
    }

    public void CenterOn(Vector2 worldPosition)
    {
        Position = worldPosition;
        ClampToBounds();
    }

    public override void _Process(double delta)
    {
        UpdateShake(delta);
        if (!UserControl)
        {
            ClampToBounds();
            return;
        }
        // Ctrl+key combinations are hotkeys (Ctrl+A selects all), not camera movement.
        Vector2 dir = Godot.Input.IsKeyPressed(Key.Ctrl)
            ? Vector2.Zero
            : Godot.Input.GetVector(
                InputActions.CameraLeft, InputActions.CameraRight, InputActions.CameraUp, InputActions.CameraDown);

        if (EdgePanEnabled && (GameSettings.Instance?.EdgePan ?? true) && _mouseInWindow && !_dragging && GetWindow().HasFocus())
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
        if (!UserControl)
            return;
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

using Godot;
using Lordsio.Core;
using SimVector2 = System.Numerics.Vector2;

namespace Lordsio.UI;

/// <summary>
/// Points at the latest attack on the player for a few seconds: a pulsing ring when it is on screen,
/// an arrow on the screen edge when it is not.
/// </summary>
public partial class AlertIndicator : Control
{
    private const float ShowSeconds = 4f;
    private const float EdgeMargin = 36f;

    private static readonly Color AlertColor = new("ff5a4a");

    private MatchController _match = null!;
    private Vector2 _world;
    private float _timeLeft;

    public void Initialize(MatchController match)
    {
        _match = match;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void PointAt(SimVector2 simPosition)
    {
        _world = SimConvert.ToWorld(simPosition);
        _timeLeft = ShowSeconds;
    }

    public override void _Process(double delta)
    {
        if (_timeLeft <= 0f)
            return;
        _timeLeft -= (float)delta;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_timeLeft <= 0f)
            return;
        // World → screen through the camera; the HUD layer itself does not move.
        var screen = _match.GetViewport().GetCanvasTransform() * _world;
        var rect = GetViewportRect();
        float pulse = 0.5f + 0.5f * Mathf.Sin(_timeLeft * 10f);
        var color = AlertColor with { A = Mathf.Min(1f, _timeLeft) };

        if (rect.Grow(-EdgeMargin).HasPoint(screen))
        {
            DrawArc(screen, 14f + 6f * pulse, 0f, Mathf.Tau, 32, color, 2f);
            return;
        }

        var center = rect.GetCenter();
        var dir = (screen - center).Normalized();
        var inner = rect.Grow(-EdgeMargin);
        // Walk from the center towards the attack until the edge of the inner rectangle.
        float tx = dir.X != 0 ? ((dir.X > 0 ? inner.End.X : inner.Position.X) - center.X) / dir.X : float.MaxValue;
        float ty = dir.Y != 0 ? ((dir.Y > 0 ? inner.End.Y : inner.Position.Y) - center.Y) / dir.Y : float.MaxValue;
        var tip = center + dir * Mathf.Min(tx, ty);
        var side = new Vector2(-dir.Y, dir.X);
        float size = 14f + 4f * pulse;
        DrawColoredPolygon(new[] { tip, tip - dir * size + side * size * 0.6f, tip - dir * size - side * size * 0.6f }, color);
    }
}

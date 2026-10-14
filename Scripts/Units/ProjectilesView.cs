using Godot;
using Lordsio.Core;

namespace Lordsio.Units;

/// <summary>
/// Draws arrows in flight as short strokes pointing at their target, and catapult stones as small rocks flying
/// in an arc with their shadow on the ground.
/// </summary>
public partial class ProjectilesView : Node2D
{
    private static readonly Color Shaft = new("f2e6c9");
    private static readonly Color Stone = new("6e6862");
    private static readonly Color StoneLight = new("9d968e");
    private static readonly Color Shadow = new(0f, 0f, 0f, 0.3f);
    private const float Length = 5f;
    /// <summary>How high (pixels) a stone rises at the middle of its flight.</summary>
    private const float ArcHeight = 18f;

    private MatchController _match = null!;

    public void Initialize(MatchController match)
    {
        _match = match;
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        float alpha = _match.InterpolationAlpha;
        foreach (var p in _match.Sim.Projectiles)
        {
            if (!_match.Fog.IsVisible(p.Position))
                continue;
            var position = SimConvert.ToWorld(System.Numerics.Vector2.Lerp(p.PreviousPosition, p.Position, alpha));
            if (p.SplashRadius > 0f)
            {
                DrawStone(p, position);
                continue;
            }
            // Arrows fly at chest height, not along the ground.
            position += new Vector2(0, -6);
            var direction = (SimConvert.ToWorld(p.Target.Position) + new Vector2(0, -6) - position).Normalized();
            DrawLine(position - direction * Length, position, Shaft, 1f);
        }
    }

    private void DrawStone(Sim.Combat.Projectile p, Vector2 ground)
    {
        float total = System.Numerics.Vector2.Distance(p.Origin, p.Target.Position);
        float left = System.Numerics.Vector2.Distance(p.Position, p.Target.Position);
        float progress = total > 0.01f ? Mathf.Clamp(1f - left / total, 0f, 1f) : 1f;
        float height = Mathf.Sin(progress * Mathf.Pi) * ArcHeight + 4f;
        DrawSetTransform(ground, 0f, new Vector2(1f, 0.5f));
        DrawCircle(Vector2.Zero, 2f, Shadow);
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        var at = ground + new Vector2(0, -height);
        DrawCircle(at, 2f, Stone);
        DrawRect(new Rect2(at + new Vector2(-1, -1), Vector2.One), StoneLight);
    }
}

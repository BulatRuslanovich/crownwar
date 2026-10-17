using Godot;
using Warcrown.Core;
using Warcrown.World;

namespace Warcrown.Units;

/// <summary>
/// Draws arrows in flight as short strokes pointing at their target, and catapult stones as small rocks flying
/// in an arc with their shadow on the ground; after dark the stones are set alight and fly as fireballs trailing
/// embers, bright against the night. A dragon's fire is always a fireball. Missiles rise to a flyer they are shot
/// at, and a flyer's own fire falls from its height.
/// </summary>
public partial class ProjectilesView : Node2D
{
    private static readonly Color Shaft = new("f2e6c9");
    private static readonly Color Stone = new("6e6862");
    private static readonly Color StoneLight = new("9d968e");
    private static readonly Color Shadow = new(0f, 0f, 0f, 0.3f);
    private static readonly Color FireCore = new("fff0a0");
    private static readonly Color FireOuter = new("ff7a1a");
    private static readonly Color Fletching = new("e8e2d0");
    private static readonly Color Head = new("9aa0a8");
    private const float Length = 10f;
    /// <summary>How high (pixels) a stone rises at the middle of its flight.</summary>
    private const float ArcHeight = 36f;
    /// <summary>Arrows fly at chest height, not along the ground.</summary>
    private const float ChestHeight = 12f;

    private MatchController _match = null!;

    public void Initialize(MatchController match)
    {
        _match = match;
    }

    /// <summary>Whether the last frame drew anything (then one more draw clears it once all have landed).</summary>
    private bool _drewAny;

    public override void _Process(double delta)
    {
        bool any = _match.Sim.Projectiles.Count > 0;
        if (any || _drewAny)
            QueueRedraw();
        _drewAny = any;
    }

    public override void _Draw()
    {
        float alpha = _match.InterpolationAlpha;
        foreach (var p in _match.Sim.Projectiles)
        {
            if (!_match.Fog.IsVisible(p.Position))
                continue;
            var position = SimConvert.ToWorld(System.Numerics.Vector2.Lerp(p.PreviousPosition, p.Position, alpha));
            if (p.IsStone)
            {
                DrawStone(p, position);
                continue;
            }
            // From the shooter's height to the target's: a flyer is hit up where it flies, a dragon breathes from on high.
            float progress = Progress(p);
            float from = ChestHeight + ArtLayout.FlightAltitude(_match.Sim.GetUnit(p.SourceId)?.Type.Id);
            float to = ChestHeight + ArtLayout.FlightAltitude((p.Target as Sim.Units.Unit)?.Type.Id);
            var lifted = position + new Vector2(0, -Mathf.Lerp(from, to, progress));
            var target = SimConvert.ToWorld(p.Target.Position) + new Vector2(0, -to);
            if (p.IsFire)
            {
                DrawFireball(lifted, target - lifted);
                continue;
            }
            var direction = (target - lifted).Normalized();
            var tail = lifted - direction * Length;
            DrawLine(tail, lifted, Shaft, 1f);
            DrawLine(tail, tail + direction * 3f, Fletching, 2f);
            DrawRect(new Rect2((lifted - Vector2.One).Round(), new Vector2(2, 2)), Head);
        }
    }

    /// <summary>How much of its way a missile has flown (0 at the shooter, 1 at the target).</summary>
    private static float Progress(Sim.Combat.Projectile p)
    {
        float total = System.Numerics.Vector2.Distance(p.Origin, p.Target.Position);
        float left = System.Numerics.Vector2.Distance(p.Position, p.Target.Position);
        return total > 0.01f ? Mathf.Clamp(1f - left / total, 0f, 1f) : 1f;
    }

    /// <summary>A dragon's fireball: a white-hot core in an orange blaze, a tail of flame behind; bright day and night.</summary>
    private void DrawFireball(Vector2 at, Vector2 toward)
    {
        var back = toward.LengthSquared() > 0.01f ? -toward.Normalized() : Vector2.Zero;
        for (int i = 4; i >= 1; i--)
        {
            var trail = at + back * i * 4f + new Vector2(0, Mathf.Sin(i * 2.3f) * 1.5f);
            DrawCircle(trail.Round(), 5f - i, _match.Lit(FireOuter) with { A = 0.9f - i * 0.18f });
        }
        DrawCircle(at.Round(), 5f, _match.Lit(FireOuter));
        DrawCircle(at.Round(), 3f, _match.Lit(new Color("ffc04a")));
        DrawCircle(at.Round(), 1.5f, _match.Lit(FireCore));
    }

    private void DrawStone(Sim.Combat.Projectile p, Vector2 ground)
    {
        float progress = Progress(p);
        float height = Mathf.Sin(progress * Mathf.Pi) * ArcHeight + 8f;
        DrawSetTransform(ground, 0f, new Vector2(1f, 0.5f));
        DrawCircle(Vector2.Zero, 4f, Shadow);
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        var at = ground + new Vector2(0, -height);
        DrawCircle(at, 4f, Stone);
        DrawRect(new Rect2(at + new Vector2(-2, -2), new Vector2(2, 2)), StoneLight);
        DrawRect(new Rect2(at + new Vector2(1, 1), new Vector2(2, 2)), Stone.Darkened(0.3f));

        float night = _match.NightAmount;
        if (night <= 0.01f)
            return;
        // Burning pitch: a glow and a trail of embers along the arc, undoing the night's darkening.
        Color Lit(Color c, float a) => _match.Lit(c) with { A = a * night };
        var direction = SimConvert.ToWorld(p.Target.Position) - SimConvert.ToWorld(p.Origin);
        for (int i = 1; i <= 4; i++)
        {
            float q = Mathf.Max(0f, progress - i * 0.025f);
            var trail = ground - direction * (progress - q) + new Vector2(0, -(Mathf.Sin(q * Mathf.Pi) * ArcHeight + 8f));
            DrawRect(new Rect2((trail - Vector2.One * 2).Round(), new Vector2(4, 4) * (i < 3 ? 1f : 0.5f)), Lit(FireOuter, 0.8f - i * 0.15f));
        }
        DrawCircle(at, 6f, Lit(FireOuter, 0.85f));
        DrawCircle(at, 3f, Lit(FireCore, 1f));
    }
}

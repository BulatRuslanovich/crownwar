using Godot;
using Lordsio.Core;

namespace Lordsio.Units;

/// <summary>Draws arrows in flight as short strokes pointing at their target.</summary>
public partial class ProjectilesView : Node2D
{
    private static readonly Color Shaft = new("f2e6c9");
    private const float Length = 5f;

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
            var position = SimConvert.ToWorld(System.Numerics.Vector2.Lerp(p.PreviousPosition, p.Position, alpha));
            // Arrows fly at chest height, not along the ground.
            position += new Vector2(0, -6);
            var direction = (SimConvert.ToWorld(p.Target.Position) + new Vector2(0, -6) - position).Normalized();
            DrawLine(position - direction * Length, position, Shaft, 1f);
        }
    }
}

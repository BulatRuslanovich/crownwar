using Godot;

namespace Lordsio.Units;

/// <summary>
/// Placeholder pixel soldier drawn in code. The node origin is the unit's feet (its simulation position).
/// </summary>
public partial class UnitView : Node2D
{
    private static readonly Color Shadow = new(0f, 0f, 0f, 0.3f);
    private static readonly Color SelectionColor = new("7cff6b");
    private static readonly Color Skin = new("e8b98a");
    private static readonly Color Steel = new("b8bcc4");
    private static readonly Color Legs = new("3b2f2a");
    private static readonly Color Shaft = new("7a5530");

    private bool _selected;

    public Color FactionColor { get; set; } = new("3c6fd8");

    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value)
                return;
            _selected = value;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        // Flattened circles for the ground shadow and selection ring.
        DrawSetTransform(new Vector2(0, 0.5f), 0f, new Vector2(1f, 0.5f));
        DrawCircle(Vector2.Zero, 5f, Shadow);
        if (_selected)
            DrawArc(Vector2.Zero, 7f, 0f, Mathf.Tau, 24, SelectionColor, 1f);
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);

        DrawRect(new Rect2(-3, -3, 2, 3), Legs);
        DrawRect(new Rect2(1, -3, 2, 3), Legs);
        DrawRect(new Rect2(-3, -8, 6, 5), FactionColor);
        DrawRect(new Rect2(-2, -12, 4, 4), Skin);
        DrawRect(new Rect2(-2, -12, 4, 1), Steel);
        DrawRect(new Rect2(4, -13, 1, 11), Shaft);
        DrawRect(new Rect2(4, -14, 1, 1), Steel);
    }
}

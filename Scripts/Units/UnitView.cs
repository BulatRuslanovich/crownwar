using Godot;
using Lordsio.Core;
using Lordsio.Sim.Units;

namespace Lordsio.Units;

/// <summary>
/// Placeholder pixel soldier drawn in code, one look per unit type, with a health bar when hurt.
/// The node origin is the unit's feet (its simulation position).
/// </summary>
public partial class UnitView : Node2D
{
    private static readonly Color Shadow = new(0f, 0f, 0f, 0.3f);
    private static readonly Color SelectionColor = new("7cff6b");
    private static readonly Color Skin = new("e8b98a");
    private static readonly Color Steel = new("b8bcc4");
    private static readonly Color SteelDark = new("7d828c");
    private static readonly Color Legs = new("3b2f2a");
    private static readonly Color Wood = new("7a5530");
    private static readonly Color Hood = new("4d6b34");
    private static readonly Color BarBack = new(0f, 0f, 0f, 0.7f);

    private Unit _unit = null!;
    private Color _factionColor;
    private bool _selected;
    private int _drawnHealth;

    public void Initialize(Unit unit)
    {
        _unit = unit;
        _factionColor = FactionColors.Get(unit.FactionId);
        _drawnHealth = unit.Health;
    }

    public bool Selected
    {
        set
        {
            if (_selected == value)
                return;
            _selected = value;
            QueueRedraw();
        }
    }

    public override void _Process(double delta)
    {
        if (_unit.Health == _drawnHealth)
            return;
        _drawnHealth = _unit.Health;
        QueueRedraw();
    }

    public override void _Draw()
    {
        bool heavy = _unit.Type.Id == "heavy";
        float ring = heavy ? 8f : 7f;

        // Flattened circles for the ground shadow and selection ring.
        DrawSetTransform(new Vector2(0, 0.5f), 0f, new Vector2(1f, 0.5f));
        DrawCircle(Vector2.Zero, ring - 2f, Shadow);
        if (_selected)
            DrawArc(Vector2.Zero, ring, 0f, Mathf.Tau, 24, SelectionColor, 1f);
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);

        switch (_unit.Type.Id)
        {
            case "archer": DrawArcher(); break;
            case "heavy": DrawHeavy(); break;
            default: DrawInfantry(); break;
        }

        if (_unit.Health < _unit.Type.MaxHealth || _selected)
            DrawHealthBar(heavy ? -19 : -17);
    }

    private void DrawInfantry()
    {
        DrawRect(new Rect2(-3, -3, 2, 3), Legs);
        DrawRect(new Rect2(1, -3, 2, 3), Legs);
        DrawRect(new Rect2(-3, -8, 6, 5), _factionColor);
        DrawRect(new Rect2(-2, -12, 4, 4), Skin);
        DrawRect(new Rect2(-2, -12, 4, 1), Steel);
        DrawRect(new Rect2(4, -13, 1, 11), Wood);
        DrawRect(new Rect2(4, -14, 1, 1), Steel);
    }

    private void DrawArcher()
    {
        DrawRect(new Rect2(-3, -3, 2, 3), Legs);
        DrawRect(new Rect2(1, -3, 2, 3), Legs);
        DrawRect(new Rect2(-3, -8, 6, 5), _factionColor);
        DrawRect(new Rect2(-2, -12, 4, 4), Skin);
        DrawRect(new Rect2(-3, -13, 6, 2), Hood);
        DrawRect(new Rect2(-3, -11, 1, 3), Hood);
        // Bow: a bent stave and its string.
        DrawRect(new Rect2(5, -12, 1, 2), Wood);
        DrawRect(new Rect2(6, -10, 1, 5), Wood);
        DrawRect(new Rect2(5, -5, 1, 2), Wood);
        DrawRect(new Rect2(4, -11, 1, 7), Steel);
    }

    private void DrawHeavy()
    {
        DrawRect(new Rect2(-4, -3, 3, 3), Legs);
        DrawRect(new Rect2(1, -3, 3, 3), Legs);
        DrawRect(new Rect2(-4, -10, 8, 7), SteelDark);
        DrawRect(new Rect2(-1, -10, 2, 7), _factionColor);
        DrawRect(new Rect2(-3, -15, 6, 5), Steel);
        DrawRect(new Rect2(-2, -13, 4, 1), Legs);
        // Shield in faction colours, sword on the other side.
        DrawRect(new Rect2(-7, -10, 4, 7), _factionColor);
        DrawRect(new Rect2(-7, -10, 4, 1), Steel);
        DrawRect(new Rect2(5, -14, 1, 9), Steel);
        DrawRect(new Rect2(4, -6, 3, 1), Wood);
    }

    private void DrawHealthBar(float y)
    {
        float fraction = Mathf.Clamp((float)_unit.Health / _unit.Type.MaxHealth, 0f, 1f);
        var fill = new Color(1f - fraction, 0.3f + 0.6f * fraction, 0.2f);
        DrawRect(new Rect2(-6, y, 12, 3), BarBack);
        DrawRect(new Rect2(-5, y + 1, 10 * fraction, 1), fill);
    }
}

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

    private const float LungeDuration = 0.18f;
    private const float LungeDistance = 3f;
    private const float FlashDuration = 0.1f;
    private const float StepsPerSecond = 6f;

    private Unit _unit = null!;
    private Color _factionColor;
    private bool _selected;
    private int _drawnHealth;
    private Vector2 _lungeDirection;
    private float _lungeTime;
    private float _flashTime;
    private float _walkTime;
    private Vector2 _bodyOffset;
    private int _liftLeft, _liftRight;

    public void Initialize(Unit unit)
    {
        _unit = unit;
        _factionColor = FactionColors.Get(unit.FactionId);
        _drawnHealth = unit.Health;
    }

    /// <summary>Plays the strike animation towards <paramref name="worldDirection"/> (visual only).</summary>
    public void Lunge(Vector2 worldDirection)
    {
        _lungeDirection = worldDirection.Normalized();
        _lungeTime = LungeDuration;
    }

    /// <summary>Briefly flashes white after being hit (visual only).</summary>
    public void Flash() => _flashTime = FlashDuration;

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
        float dt = (float)delta;
        bool animating = _lungeTime > 0f || _flashTime > 0f || _unit.State == UnitState.Moving;

        _lungeTime = Mathf.Max(0f, _lungeTime - dt);
        _flashTime = Mathf.Max(0f, _flashTime - dt);
        Modulate = _flashTime > 0f ? new Color(2.2f, 2.2f, 2.2f) : Colors.White;

        // Walking: alternate the legs; striking: jab towards the target.
        if (_unit.State == UnitState.Moving)
        {
            _walkTime += dt * StepsPerSecond;
            int phase = (int)(_walkTime * 2f) % 4;
            _liftLeft = phase == 1 ? 1 : 0;
            _liftRight = phase == 3 ? 1 : 0;
        }
        else
        {
            _liftLeft = _liftRight = 0;
        }
        float lunge = _lungeTime > 0f ? Mathf.Sin(Mathf.Pi * (1f - _lungeTime / LungeDuration)) : 0f;
        _bodyOffset = (_lungeDirection * LungeDistance * lunge).Round();

        if (animating || _unit.Health != _drawnHealth)
        {
            _drawnHealth = _unit.Health;
            QueueRedraw();
        }
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
        DrawSetTransform(_bodyOffset, 0f, Vector2.One);

        switch (_unit.Type.Id)
        {
            case "archer": DrawArcher(); break;
            case "heavy": DrawHeavy(); break;
            default: DrawInfantry(); break;
        }
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);

        if (_unit.Health < _unit.Type.MaxHealth || _selected)
            DrawHealthBar(heavy ? -19 : -17);
    }

    private void DrawInfantry()
    {
        DrawRect(new Rect2(-3, -3 - _liftLeft, 2, 3), Legs);
        DrawRect(new Rect2(1, -3 - _liftRight, 2, 3), Legs);
        DrawRect(new Rect2(-3, -8, 6, 5), _factionColor);
        DrawRect(new Rect2(-2, -12, 4, 4), Skin);
        DrawRect(new Rect2(-2, -12, 4, 1), Steel);
        DrawRect(new Rect2(4, -13, 1, 11), Wood);
        DrawRect(new Rect2(4, -14, 1, 1), Steel);
    }

    private void DrawArcher()
    {
        DrawRect(new Rect2(-3, -3 - _liftLeft, 2, 3), Legs);
        DrawRect(new Rect2(1, -3 - _liftRight, 2, 3), Legs);
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
        DrawRect(new Rect2(-4, -3 - _liftLeft, 3, 3), Legs);
        DrawRect(new Rect2(1, -3 - _liftRight, 3, 3), Legs);
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

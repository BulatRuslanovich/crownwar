using Godot;
using Lordsio.Core;
using Lordsio.Dev;
using Lordsio.Sim.Units;

namespace Lordsio.Units;

/// <summary>
/// A unit drawn from Assets/Sprites/units.png: a body sprite plus a faction-colour mask sprite tinted
/// with the owner's colour, animated (idle / walk / strike) and turned towards where it goes.
/// Shadow and selection ring are drawn below, the health bar above. The node origin is the unit's feet.
/// All animation is cosmetic: hits are decided by the simulation.
/// </summary>
public partial class UnitView : Node2D
{
    private const float LungeDuration = 0.18f;
    private const float LungeDistance = 3f;
    private const float FlashDuration = 0.1f;
    private const float StepsPerSecond = 6f;

    private static readonly Color Shadow = new(0f, 0f, 0f, 0.3f);
    private static readonly Color SelectionColor = new("7cff6b");
    private static readonly Color BarBack = new(0f, 0f, 0f, 0.7f);

    private Unit _unit = null!;
    private Sprite2D _body = null!;
    private Sprite2D _team = null!;
    private Node2D _overlay = null!;
    private int _row;
    private bool _selected;
    private int _drawnHealth;
    private bool _facingLeft;
    private Vector2 _lungeDirection;
    private float _lungeTime;
    private float _flashTime;
    private float _walkTime;

    public void Initialize(Unit unit)
    {
        _unit = unit;
        _row = ArtLayout.UnitRow(unit.Type.Id);
        _drawnHealth = unit.Health;
        _body = MakeSprite("res://Assets/Sprites/units.png", Colors.White);
        _team = MakeSprite("res://Assets/Sprites/units_team.png", FactionColors.Get(unit.FactionId));
        _overlay = new Node2D { Name = "Overlay" };
        _overlay.Draw += DrawOverlay;
        AddChild(_overlay);
    }

    /// <summary>Plays the strike animation towards <paramref name="worldDirection"/> (visual only).</summary>
    public void Lunge(Vector2 worldDirection)
    {
        _lungeDirection = worldDirection.Normalized();
        _lungeTime = LungeDuration;
        if (Mathf.Abs(worldDirection.X) > 0.01f)
            _facingLeft = worldDirection.X < 0;
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
            _overlay.QueueRedraw();
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _lungeTime = Mathf.Max(0f, _lungeTime - dt);
        _flashTime = Mathf.Max(0f, _flashTime - dt);
        Modulate = _flashTime > 0f ? new Color(2.2f, 2.2f, 2.2f) : Colors.White;

        // Face the way we walk, or the enemy we fight.
        float dx = _unit.Position.X - _unit.PreviousPosition.X;
        if (Mathf.Abs(dx) > 0.001f)
            _facingLeft = dx < 0;
        else if (_unit.Target is { } target && Mathf.Abs(target.Position.X - _unit.Position.X) > 0.05f)
            _facingLeft = target.Position.X < _unit.Position.X;

        int frame = ArtLayout.FrameIdle;
        if (_lungeTime > 0f || _unit.State == UnitState.Attacking && _unit.Type.IsRanged)
        {
            frame = ArtLayout.FrameAttack;
        }
        else if (_unit.State == UnitState.Moving)
        {
            _walkTime += dt * StepsPerSecond;
            frame = (int)_walkTime % 2 == 0 ? ArtLayout.FrameWalk1 : ArtLayout.FrameWalk2;
        }

        float lunge = _lungeTime > 0f ? Mathf.Sin(Mathf.Pi * (1f - _lungeTime / LungeDuration)) : 0f;
        var offset = (_lungeDirection * LungeDistance * lunge).Round();
        foreach (var sprite in new[] { _body, _team })
        {
            sprite.Frame = _row * ArtLayout.UnitFrames + frame;
            sprite.FlipH = _facingLeft;
            sprite.Position = offset;
        }

        if (_unit.Health != _drawnHealth)
        {
            _drawnHealth = _unit.Health;
            _overlay.QueueRedraw();
        }
    }

    public override void _Draw()
    {
        // Flattened circles for the ground shadow and selection ring, under the sprites.
        float ring = _unit.Type.Radius * SimConvert.TileSize + 2f;
        DrawSetTransform(new Vector2(0, 0.5f), 0f, new Vector2(1f, 0.5f));
        DrawCircle(Vector2.Zero, ring - 2f, Shadow);
        if (_selected)
            DrawArc(Vector2.Zero, ring, 0f, Mathf.Tau, 24, SelectionColor, 1f);
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    private void DrawOverlay()
    {
        if (_unit.Health >= _unit.Type.MaxHealth && !_selected)
            return;
        float fraction = Mathf.Clamp((float)_unit.Health / _unit.Type.MaxHealth, 0f, 1f);
        var fill = new Color(1f - fraction, 0.3f + 0.6f * fraction, 0.2f);
        float y = -ArtLayout.UnitFeet.Y - 2;
        _overlay.DrawRect(new Rect2(-6, y, 12, 3), BarBack);
        _overlay.DrawRect(new Rect2(-5, y + 1, 10 * fraction, 1), fill);
    }

    private Sprite2D MakeSprite(string path, Color modulate)
    {
        var sprite = new Sprite2D
        {
            Texture = GD.Load<Texture2D>(path),
            Hframes = ArtLayout.UnitFrames,
            Vframes = ArtLayout.UnitRows,
            Centered = false,
            Offset = -new Vector2(ArtLayout.UnitFeet.X, ArtLayout.UnitFeet.Y),
            Modulate = modulate,
        };
        AddChild(sprite);
        return sprite;
    }
}

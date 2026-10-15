using Godot;
using Lordsio.Core;
using Lordsio.Sim.Core;
using Lordsio.Sim.Units;
using Lordsio.UI;
using Lordsio.World;

namespace Lordsio.Units;

/// <summary>
/// A unit drawn from Assets/Sprites/units.png: a body sprite plus a faction-colour mask sprite tinted
/// with the owner's colour, animated and turned towards where it goes. Standing, it breathes; marching, it steps
/// through a four-frame stride; fighting, it winds up as its next blow comes due (timed by the simulation's attack
/// cooldown), strikes, and an archer's bow recoils after the shot. A hit unit flashes white and is knocked back a
/// pixel. The selection ring is drawn below, the health bar above (the ground shadow is drawn by
/// <see cref="World.ShadowsView"/>). The node origin is the unit's feet. All animation is cosmetic: hits are decided
/// by the simulation.
/// </summary>
public partial class UnitView : Node2D
{
    private const float LungeDuration = 0.2f;
    private const float LungeDistance = 3f;
    private const float FlashDuration = 0.1f;
    private const float KnockDuration = 0.14f;
    private const float KnockDistance = 1.5f;
    /// <summary>Steps (frames) a second on the march.</summary>
    private const float StepsPerSecond = 9f;
    /// <summary>How long before its blow a fighter winds up (a melee swing, a ram drawing back its log).</summary>
    private const float WindupSeconds = 0.35f;
    /// <summary>How long before the shot an archer draws the bow, and how long the bow quivers after it.</summary>
    private const float DrawSeconds = 0.55f;
    private const float ReleaseSeconds = 0.22f;
    /// <summary>A breath every few seconds while standing.</summary>
    private const float BreathPeriod = 2.6f;
    private const float BreathHold = 0.7f;

    private static readonly Color SelectionColor = UiPalette.Friendly;
    private static readonly Color BarBack = new(0f, 0f, 0f, 0.7f);

    private Unit _unit = null!;
    /// <summary>The soldier in his faction's colour (see <see cref="UnitSheets"/>).</summary>
    private Sprite2D _sprite = null!;
    private Node2D _overlay = null!;
    private int _row;
    private bool _selected;
    private int _drawnHealth;
    private bool _facingLeft;
    private Vector2 _lungeDirection;
    private float _lungeTime;
    private float _flashTime;
    /// <summary>Picked as the target of an attack order: a red pulse, for this long.</summary>
    private const float MarkSeconds = 0.5f;
    private static readonly Color MarkTint = new(1.8f, 0.5f, 0.45f);
    private static readonly Color HitFlash = new(2.2f, 2.2f, 2.2f);
    private float _markTime;
    private Vector2 _knockDirection;
    private float _knockTime;
    private float _walkTime;
    private float _idleTime;

    /// <summary>The frame shown (column in the sheet), for the shadow.</summary>
    public int Frame { get; private set; }
    public bool FacingLeft => _facingLeft;
    /// <summary>Where the sprites are drawn relative to the feet (lunges and knock-backs move them).</summary>
    public Vector2 SpriteOffset { get; private set; }
    public int Row => _row;

    /// <summary>How much bigger than its cell the unit is drawn (see <see cref="ArtLayout.UnitScale"/>).</summary>
    public float BodyScale => _scale;
    private float _scale = 1f;

    public void Initialize(Unit unit, Texture2D sheet)
    {
        _unit = unit;
        _row = ArtLayout.UnitRow(unit.Type.Id);
        _scale = ArtLayout.UnitScale(unit.Type.Id);
        _drawnHealth = unit.Health;
        // Every soldier breathes in his own time.
        _idleTime = unit.Id * 0.73f % BreathPeriod;
        _sprite = MakeSprite(sheet);
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

    /// <summary>A monk's blessing: the staff held high for a moment, without stepping forward (visual only).</summary>
    public void Bless()
    {
        _lungeDirection = Vector2.Zero;
        _lungeTime = LungeDuration * 1.5f;
    }

    /// <summary>The player ordered an attack on this unit: a short red pulse, so it is clear who was clicked.</summary>
    public void MarkTarget() => _markTime = MarkSeconds;

    /// <summary>A recruit steps out: squashed flat, then springing up past its height and settling.</summary>
    public void Pop()
    {
        _sprite.Scale = new Vector2(1.45f, 0.5f) * _scale;
        CreateTween().TweenProperty(_sprite, "scale", Vector2.One * _scale, 0.32).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    /// <summary>Briefly flashes white and staggers away from the blow (visual only).</summary>
    public void Flash(Vector2 fromDirection)
    {
        _flashTime = FlashDuration;
        _knockTime = KnockDuration;
        _knockDirection = fromDirection.LengthSquared() > 0.001f ? fromDirection.Normalized() : Vector2.Zero;
    }

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
        _knockTime = Mathf.Max(0f, _knockTime - dt);
        _markTime = Mathf.Max(0f, _markTime - dt);
        var tint = _flashTime > 0f ? HitFlash : Colors.White;
        if (_markTime > 0f)
            tint *= Colors.White.Lerp(MarkTint, _markTime / MarkSeconds * (0.6f + 0.4f * Mathf.Sin(_markTime * 30f)));
        Modulate = tint;

        // Face the way we walk, or the enemy we fight.
        float dx = _unit.Position.X - _unit.PreviousPosition.X;
        if (Mathf.Abs(dx) > 0.001f)
            _facingLeft = dx < 0;
        else if (_unit.Target is { } target && Mathf.Abs(target.Position.X - _unit.Position.X) > 0.05f)
            _facingLeft = target.Position.X < _unit.Position.X;

        var offset = Vector2.Zero;
        Frame = PickFrame(dt, ref offset);
        if (_lungeTime > 0f && _lungeTime <= LungeDuration)
            offset += _lungeDirection * LungeDistance * Mathf.Sin(Mathf.Pi * (1f - _lungeTime / LungeDuration));
        if (_knockTime > 0f)
            offset += _knockDirection * KnockDistance * (_knockTime / KnockDuration);
        SpriteOffset = offset.Round();
        _sprite.Frame = _row * ArtLayout.UnitFrames + Frame;
        _sprite.FlipH = _facingLeft;
        _sprite.Position = SpriteOffset;

        if (_unit.Health != _drawnHealth)
        {
            _drawnHealth = _unit.Health;
            _overlay.QueueRedraw();
        }
    }

    private int PickFrame(float dt, ref Vector2 offset)
    {
        if (_lungeTime > 0f)
            return ArtLayout.FrameStrike;
        if (_unit.State == UnitState.Attacking)
        {
            float due = _unit.AttackCooldownTicks / (float)Simulation.TicksPerSecond;
            float since = _unit.Type.AttackCooldown - due;
            if (_unit.Type.IsRanged)
                return since < ReleaseSeconds ? ArtLayout.FrameStrike : due <= DrawSeconds ? ArtLayout.FrameWindup : ArtLayout.FrameIdle;
            if (due > WindupSeconds)
                return ArtLayout.FrameIdle;
            // A ram rolls back a little to gather speed for the blow.
            if (_unit.Type.IsSiege && _unit.Target is { } target)
            {
                var toward = new Vector2(target.Position.X - _unit.Position.X, target.Position.Y - _unit.Position.Y).Normalized();
                offset -= toward * 2f * (1f - due / WindupSeconds);
            }
            return ArtLayout.FrameWindup;
        }
        if (_unit.State == UnitState.Moving)
        {
            _walkTime += dt * StepsPerSecond * Mathf.Clamp(_unit.Type.Speed / 3.4f, 0.6f, 1.2f);
            return ArtLayout.FrameWalk1 + (int)_walkTime % 4;
        }
        _idleTime = (_idleTime + dt) % BreathPeriod;
        return _idleTime > BreathPeriod - BreathHold ? ArtLayout.FrameBreath : ArtLayout.FrameIdle;
    }

    public override void _Draw()
    {
        if (!_selected)
            return;
        // A flattened ring under the sprites.
        float ring = _unit.Type.Radius * SimConvert.TileSize + 2f;
        DrawSetTransform(new Vector2(0, 0.5f), 0f, new Vector2(1f, 0.5f));
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

    private Sprite2D MakeSprite(Texture2D sheet)
    {
        var sprite = new Sprite2D
        {
            Texture = sheet,
            Hframes = ArtLayout.UnitFrames,
            Vframes = ArtLayout.UnitRows,
            Centered = false,
            Offset = -new Vector2(ArtLayout.UnitFeet.X, ArtLayout.UnitFeet.Y),
            Scale = Vector2.One * _scale,
        };
        AddChild(sprite);
        return sprite;
    }
}

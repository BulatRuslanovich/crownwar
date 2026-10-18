using Godot;
using Warcrown.Core;
using Warcrown.Sim.Core;
using Warcrown.Sim.Units;
using Warcrown.UI;
using Warcrown.World;

namespace Warcrown.Units;

/// <summary>
/// A unit drawn from Assets/Sprites/units.png: a body sprite plus a faction-colour mask sprite tinted
/// with the owner's colour, animated and turned towards where it goes. Standing, it breathes; marching, it steps
/// through a four-frame stride; fighting, it winds up as its next blow comes due (timed by the simulation's attack
/// cooldown), strikes, and an archer's bow recoils after the shot. A hit unit flashes white and is knocked back a
/// pixel. The selection ring is drawn below, the health bar above with a veteran's stars over it (the ground shadow is drawn by
/// <see cref="World.ShadowsView"/>). The node origin is the unit's feet — for a flyer, the spot on the ground under it: its
/// picture and bars hang <see cref="ArtLayout.FlightAltitude"/> above, bobbing on its wing beats, which never stop.
/// All animation is cosmetic: hits are decided by the simulation.
/// </summary>
public partial class UnitView : Node2D
{
    /// <summary>How long a blow plays out: the strike, the follow-through and settling back.</summary>
    private const float LungeDuration = 0.32f;
    private const float LungeDistance = 6f;
    private const float FlashDuration = 0.1f;
    private const float KnockDuration = 0.14f;
    private const float KnockDistance = 3f;
    /// <summary>Frames of the stride a second on the march.</summary>
    private const float StepsPerSecond = 16f;
    /// <summary>Frames of the slow wing beat a second of a hovering flyer, and how far it rises and sinks with them.</summary>
    private const float HoverBeatsPerSecond = 7f;
    private const float HoverBob = 3f;
    /// <summary>How long before its blow a fighter winds up (a melee swing, a ram drawing back its log).</summary>
    private const float WindupSeconds = 0.35f;
    /// <summary>How long before the shot an archer draws the bow, and how long the bow quivers after it.</summary>
    private const float DrawSeconds = 0.55f;
    private const float ReleaseSeconds = 0.22f;
    /// <summary>After the bow quivers, the archer settles back for this long.</summary>
    private const float RecoverSeconds = 0.18f;
    /// <summary>A breath every few seconds while standing.</summary>
    private const float BreathPeriod = 2.6f;
    private const float BreathHold = 0.7f;

    private static readonly Color SelectionColor = UiPalette.Friendly;
    private static readonly Color BarBack = new(0f, 0f, 0f, 0.7f);

    private Unit _unit = null!;
    /// <summary>The soldier in his faction's colour (see <see cref="UnitSheets"/>).</summary>
    private Sprite2D _sprite = null!;
    private Node2D _overlay = null!;
    private ArtLayout.UnitArt _art;
    private float _altitude;
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
    /// <summary>Just promoted: a golden glow fading for this long.</summary>
    private const float PromoteSeconds = 0.8f;
    private static readonly Color PromoteTint = new(2f, 1.7f, 0.6f);
    private static readonly Color StarColor = UiPalette.Gold;
    private static readonly Color StarOutline = new(0.15f, 0.1f, 0f, 0.9f);
    private float _promoteTime;
    private int _drawnRank;
    private Vector2 _knockDirection;
    private float _knockTime;
    private float _walkTime;
    private float _idleTime;

    /// <summary>The frame shown (column in the sheet), for the shadow.</summary>
    public int Frame { get; private set; }
    public bool FacingLeft => _facingLeft;
    /// <summary>Where the sprites are drawn relative to the feet (lunges and knock-backs move them).</summary>
    public Vector2 SpriteOffset { get; private set; }

    /// <summary>Which sheet, row and cell the unit is drawn from.</summary>
    public ArtLayout.UnitArt Art => _art;

    /// <summary>How high its picture floats above the node now (a flyer's altitude with its bob; 0 on foot).</summary>
    public float Lift { get; private set; }

    public Unit Unit => _unit;

    public void Initialize(Unit unit, Texture2D sheet)
    {
        _unit = unit;
        _art = ArtLayout.UnitArtOf(unit.Type.Id);
        _altitude = ArtLayout.FlightAltitude(unit.Type.Id);
        _drawnHealth = unit.Health;
        // Every soldier breathes in his own time.
        _idleTime = unit.Id * 0.73f % BreathPeriod;
        _sprite = MakeSprite(sheet);
        _height = HeightOf(sheet, _art);
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

    /// <summary>Earned a rank: a golden glow and a proud hop up past its height.</summary>
    public void Promote()
    {
        _promoteTime = PromoteSeconds;
        _sprite.Scale = new Vector2(0.8f, 1.35f);
        CreateTween().TweenProperty(_sprite, "scale", Vector2.One, 0.4).SetTrans(Tween.TransitionType.Elastic).SetEase(Tween.EaseType.Out);
    }

    /// <summary>A recruit steps out: squashed flat, then springing up past its height and settling.</summary>
    public void Pop()
    {
        _sprite.Scale = new Vector2(1.45f, 0.5f);
        CreateTween().TweenProperty(_sprite, "scale", Vector2.One, 0.32).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
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
        _promoteTime = Mathf.Max(0f, _promoteTime - dt);
        var tint = _flashTime > 0f ? HitFlash : Colors.White.Lerp(PromoteTint, _promoteTime / PromoteSeconds);
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
            offset += _lungeDirection * LungeDistance * Mathf.Sin(Mathf.Pi * Mathf.Min(1f, 1.6f * (1f - _lungeTime / LungeDuration)));
        if (_knockTime > 0f)
            offset += _knockDirection * KnockDistance * (_knockTime / KnockDuration);
        SpriteOffset = offset.Round();
        if (_altitude > 0f)
        {
            // Rises on the down-stroke, sinks on the up-stroke.
            float phase = Frame < ArtLayout.FrameWalk ? (Frame - ArtLayout.FrameIdle) / (float)ArtLayout.IdleFrames
                : Frame < ArtLayout.FrameAttack ? (Frame - ArtLayout.FrameWalk) / (float)ArtLayout.WalkFrames
                : 0.25f;
            Lift = Mathf.Round(_altitude + HoverBob * (0.5f + 0.5f * Mathf.Sin(phase * Mathf.Tau)));
            _overlay.Position = new Vector2(0, -Lift);
        }
        _sprite.Frame = _art.Row * ArtLayout.UnitFrames + Frame;
        _sprite.FlipH = _facingLeft;
        _sprite.Position = SpriteOffset - new Vector2(0, Lift);

        if (_unit.Health != _drawnHealth || _unit.Rank != _drawnRank)
        {
            _drawnHealth = _unit.Health;
            _drawnRank = _unit.Rank;
            _overlay.QueueRedraw();
        }
    }

    /// <summary>
    /// The frame to show: after a blow, the strike, the follow-through and settling back; fighting, the wind-up as the
    /// next blow comes due (an archer drawing the bow); struck, a flinch; on the march, the stride (a flyer's wing
    /// beats, which go on while it hovers); standing, breathing.
    /// </summary>
    private int PickFrame(float dt, ref Vector2 offset)
    {
        if (_lungeTime > 0f)
            return Attack(LungeDuration - _lungeTime);
        bool fighting = _unit.State == UnitState.Attacking;
        if (fighting)
        {
            float due = _unit.AttackCooldownTicks / (float)Simulation.TicksPerSecond;
            float since = _unit.Type.AttackCooldown - due;
            if (_unit.Type.IsRanged)
            {
                if (since < ReleaseSeconds + RecoverSeconds)
                    return Attack(since * LungeDuration / (ReleaseSeconds + RecoverSeconds));
                if (due <= DrawSeconds)
                    return ArtLayout.FrameAttack + (due > DrawSeconds / 2f ? ArtLayout.AttackWindup : ArtLayout.AttackWindup + 1);
            }
            else if (due <= WindupSeconds)
            {
                // A ram rolls back a little to gather speed for the blow.
                if (_unit.Type.IsSiege && _unit.Target is { } target)
                {
                    var toward = new Vector2(target.Position.X - _unit.Position.X, target.Position.Y - _unit.Position.Y).Normalized();
                    offset -= toward * 4f * (1f - due / WindupSeconds);
                }
                return ArtLayout.FrameAttack + (due > WindupSeconds / 2f ? ArtLayout.AttackWindup : ArtLayout.AttackWindup + 1);
            }
        }
        if (_knockTime > 0f)
            return ArtLayout.FrameHurt;
        if (_unit.State == UnitState.Moving)
        {
            // Marching steps (wing beats), quicker for the quick.
            _walkTime += dt * StepsPerSecond * Mathf.Clamp(_unit.Type.Speed / 3.4f, 0.6f, 1.3f);
            return ArtLayout.FrameWalk + (int)_walkTime % ArtLayout.WalkFrames;
        }
        // Standing, breathing; a flyer hovering on slow beats.
        float rate = _altitude > 0f ? HoverBeatsPerSecond : ArtLayout.IdleFrames / BreathPeriod;
        _idleTime += dt * rate;
        return ArtLayout.FrameIdle + (int)_idleTime % ArtLayout.IdleFrames;

        // The frames after a blow, by the time since it: the strike, the follow-through, settling back.
        static int Attack(float since) => ArtLayout.FrameAttack + Mathf.Min(ArtLayout.AttackRecover,
            ArtLayout.AttackStrike + (int)(since / LungeDuration * (ArtLayout.AttackRecover - ArtLayout.AttackStrike + 1)));
    }

    public override void _Draw()
    {
        if (!_selected)
            return;
        // A flattened ring on the ground under the sprites (under a flyer's shadow too).
        float ring = _unit.Type.Radius * SimConvert.TileSize + 4f;
        DrawSetTransform(new Vector2(0, 1f), 0f, new Vector2(1f, 0.5f));
        DrawArc(Vector2.Zero, ring, 0f, Mathf.Tau, 32, SelectionColor, 2f);
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    /// <summary>Top of the picture above the feet: the soldiers' cell, or the (lower than its cell) back of a big beast.</summary>
    /// <summary>How far above the feet the picture's head reaches (kinds differ in height; see <see cref="HeightOf"/>).</summary>
    private float Height => _height;
    private float _height;

    /// <summary>The height of each kind's standing picture, measured once from its sheet.</summary>
    private static readonly System.Collections.Generic.Dictionary<(bool Big, int Row), float> Heights = new();

    /// <summary>
    /// How far above the feet the kind's idle picture reaches: the first row from the top of its cell with a few drawn
    /// pixels side by side (a helmet, a head), so a spear's thin point does not count.
    /// </summary>
    private static float HeightOf(Texture2D sheet, ArtLayout.UnitArt art)
    {
        if (Heights.TryGetValue((art.Big, art.Row), out float height))
            return height;
        var image = sheet.GetImage();
        int top = art.Feet.Y;
        for (int y = 0; y < art.CellHeight && top == art.Feet.Y; y++)
        {
            int run = 0, best = 0;
            for (int x = 0; x < art.CellWidth; x++)
            {
                run = image.GetPixel(ArtLayout.FrameIdle * art.CellWidth + x, art.Row * art.CellHeight + y).A > 0.5f ? run + 1 : 0;
                best = Mathf.Max(best, run);
            }
            if (best >= 4)
                top = y;
        }
        height = art.Feet.Y - top;
        Heights[(art.Big, art.Row)] = height;
        return height;
    }

    private void DrawOverlay()
    {
        float y = -Height - 4;
        DrawStars(y - 6);
        if (_unit.Health >= _unit.MaxHealth && !_selected)
            return;
        float fraction = Mathf.Clamp((float)_unit.Health / _unit.MaxHealth, 0f, 1f);
        var fill = new Color(1f - fraction, 0.3f + 0.6f * fraction, 0.2f);
        _overlay.DrawRect(new Rect2(-12, y, 24, 6), BarBack);
        _overlay.DrawRect(new Rect2(-10, y + 2, 20 * fraction, 2), fill);
    }

    /// <summary>A veteran's rank: one small golden star (with a dark rim) per rank, in a row centred over the bar.</summary>
    private void DrawStars(float y)
    {
        int stars = _unit.Rank;
        if (stars <= 0)
            return;
        const int spacing = 9;
        float x0 = -(stars - 1) * spacing / 2f;
        for (int pass = 0; pass < 2; pass++) // all the rims first, so a neighbour's rim never cuts into a star
        {
            for (int i = 0; i < stars; i++)
            {
                var c = new Vector2(Mathf.Floor(x0 + i * spacing), y);
                if (pass == 0)
                {
                    _overlay.DrawRect(new Rect2(c.X - 4, c.Y - 2, 8, 4), StarOutline);
                    _overlay.DrawRect(new Rect2(c.X - 2, c.Y - 4, 4, 9), StarOutline);
                    _overlay.DrawRect(new Rect2(c.X - 3, c.Y - 1, 6, 5), StarOutline);
                }
                else
                {
                    // A five-pointed star in 6×7 pixels with a glint at its top.
                    _overlay.DrawRect(new Rect2(c.X - 1, c.Y - 3, 2, 3), StarColor);
                    _overlay.DrawRect(new Rect2(c.X - 3, c.Y - 1, 6, 1), StarColor);
                    _overlay.DrawRect(new Rect2(c.X - 2, c.Y, 4, 2), StarColor);
                    _overlay.DrawRect(new Rect2(c.X - 2, c.Y + 2, 1, 1), StarColor);
                    _overlay.DrawRect(new Rect2(c.X + 1, c.Y + 2, 1, 1), StarColor);
                    _overlay.DrawRect(new Rect2(c.X - 1, c.Y - 3, 1, 1), Colors.White);
                }
            }
        }
    }

    private Sprite2D MakeSprite(Texture2D sheet)
    {
        var sprite = new Sprite2D
        {
            Texture = sheet,
            Hframes = ArtLayout.UnitFrames,
            Vframes = _art.Rows,
            Centered = false,
            Offset = -new Vector2(_art.Feet.X, _art.Feet.Y),
        };
        AddChild(sprite);
        return sprite;
    }
}

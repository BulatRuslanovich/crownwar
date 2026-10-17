using System.Collections.Generic;
using Godot;
using Warcrown.Core;
using Warcrown.Sim.Buildings;
using Warcrown.UI;
using Warcrown.World;

namespace Warcrown.Buildings;

/// <summary>
/// A building drawn from Assets/Sprites/&lt;type&gt;.png plus its faction-colour mask (&lt;type&gt;_team.png),
/// with scaffolding while under construction and construction / upgrade / health / training bars above.
/// Higher levels have their own pictures, &lt;type&gt;_t2.png and so on; the view swaps them when an upgrade completes.
/// Each picture is a sheet of <see cref="ArtLayout.BuildingFrames"/> frames that the finished building plays: flags
/// wave, a working mine's wheel turns, fires flicker. After dusk its windows and fires light up (&lt;type&gt;_lights.png,
/// added over the darkened world, with a warm halo around), and a barracks' chimney or a beacon smokes in the wind.
/// The node origin is the bottom-centre of the footprint, so tall buildings y-sort correctly with units.
/// </summary>
public partial class BuildingView : Node2D
{
    private static readonly Color Scaffold = new("c8a26a");
    private static readonly Color SelectionColor = UiPalette.Friendly;
    private static readonly Color BarBack = new(0f, 0f, 0f, 0.65f);
    private static readonly Color TrainingFill = UiPalette.Friendly;
    private static readonly Color ConstructionFill = new("ffd44a");
    private static readonly Color ReserveFill = new("e8b830");
    private static readonly Color RegenFill = new("5fb4ff");
    private static readonly Color UpgradeFill = new("9fd8ff");
    private static readonly Color HaloColor = new(1f, 0.62f, 0.25f);
    private static readonly Color SmokeColor = new(0.5f, 0.49f, 0.47f);
    /// <summary>A mine on an exhausted deposit stands idle: drawn dimmed.</summary>
    private static readonly Color IdleTint = new(0.42f, 0.42f, 0.5f);
    /// <summary>The reserve bar shows up by itself once a deposit is this close to running dry.</summary>
    private const float LowReserve = 0.25f;
    /// <summary>Waiting recruits drawn as dots above the training bar; a longer queue shows the rest as "+N".</summary>
    private const int MaxQueueDots = 5;
    private const float FramesPerSecond = 5f;
    private const int SmokePuffs = 7;
    private const float SmokeSeconds = 4.5f;

    /// <summary>Where smoke rises from, in the picture's pixels (from its top-left corner).</summary>
    private static readonly Dictionary<string, Vector2> Chimneys = new()
    {
        ["barracks"] = new Vector2(51f, 12f),
        ["watchtower"] = new Vector2(48f, 38f),
    };

    /// <summary>Middle and reach of each picture's lights (worked out once from its lights sheet).</summary>
    private static readonly Dictionary<string, (Vector2 Center, float Radius)> Halos = new();

    private Building _building = null!;
    private MatchController _match = null!;
    private Sprite2D _body = null!;
    private Sprite2D _team = null!;
    private Sprite2D _lights = null!;
    private Node2D _glow = null!;
    private Node2D _smoke = null!;
    private Node2D _overlay = null!;
    private Texture2D _haloTexture = null!;
    private Color _owner;
    private bool _selected;
    private (int Health, bool Complete, float Construction, int Queue, float Training, bool Idle, int Deposit, int Tier, float Upgrade) _drawn;
    private int _shownTier;
    private string _spriteName = "";
    private float _time;
    private bool _flickers;

    /// <summary>Struck: a white flash and a quiver, for this long.</summary>
    private const float HitSeconds = 0.18f;
    /// <summary>Picked as the target of an attack order: a red pulse, for this long.</summary>
    private const float MarkSeconds = 0.5f;
    private static readonly Color HitFlash = new(1.7f, 1.7f, 1.7f);
    private static readonly Color MarkTint = new(1.7f, 0.55f, 0.5f);
    private float _hitTime;
    private float _markTime;
    private bool _tinted;

    /// <param name="additive">Shared material that adds lights onto the picture below.</param>
    /// <param name="halo">Shared soft round glow.</param>
    public void Initialize(Building building, MatchController match, Material additive, Texture2D halo)
    {
        _building = building;
        _match = match;
        _haloTexture = halo;
        _owner = FactionColors.Get(building.FactionId);
        var bottom = building.Position + new System.Numerics.Vector2(0f, building.Type.Size / 2f);
        Position = SimConvert.ToWorld(bottom);
        // Neighbours do not wave their flags in step.
        _time = building.Id * 0.37f;
        _flickers = building.Type.Id is "watchtower" or "castle";

        _glow = new Node2D { Name = "Glow", Material = additive, Visible = false };
        _glow.Draw += DrawGlow;
        AddChild(_glow);
        _body = MakeSprite(Colors.White);
        _team = MakeSprite(_owner);
        _lights = MakeSprite(Colors.White);
        _lights.Material = additive;
        _lights.Visible = false;
        ShowLevel(building.Type);
        _smoke = new Node2D { Name = "Smoke" };
        _smoke.Draw += DrawSmoke;
        AddChild(_smoke);
        _overlay = new Node2D { Name = "Overlay" };
        _overlay.Draw += DrawOverlay;
        AddChild(_overlay);
    }

    public Building Building => _building;

    /// <summary>The picture now shown, for the shadow: body and mask sheets, the frame, and one frame's size.</summary>
    public (Texture2D Body, Texture2D Team, int Frame, Vector2 FrameSize) Picture =>
        (_body.Texture, _team.Texture, _body.Frame, new Vector2(_body.Texture.GetWidth() / (float)ArtLayout.BuildingFrames, _body.Texture.GetHeight()));

    /// <summary>
    /// Out of sight: drawn dimmed as last seen, without bars, and no longer updated from the live building.
    /// </summary>
    public bool Ghost
    {
        get => _ghost;
        set
        {
            if (_ghost == value)
                return;
            _ghost = value;
            Modulate = value ? GhostTint : Colors.White;
            _overlay.Visible = !value;
            _smoke.Visible = !value;
        }
    }

    private bool _ghost;
    private static readonly Color GhostTint = new(0.75f, 0.75f, 0.8f, 0.85f);

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

    private float SpriteHeight => _body.Texture.GetHeight();
    private float FrameWidth => _body.Texture.GetWidth() / (float)ArtLayout.BuildingFrames;

    public override void _Process(double delta)
    {
        if (_ghost)
        {
            _lights.Visible = _glow.Visible = false;
            return; // frozen as last seen
        }
        UpdateFeedback((float)delta);
        var b = _building;
        bool idle = b.Deposit is { IsExhausted: true };
        Animate((float)delta, b, idle);

        // Deposit bars change in whole percents, so the overlay is not redrawn every tick.
        int deposit = b.Deposit is { IsEndless: false } d ? (int)(DepositFraction(d) * 100f) : 0;
        var now = (b.Health, b.IsComplete, b.ConstructionProgress, b.Queue.Count, b.Queue.Count > 0 ? b.Queue[0].Progress : 0f, idle, deposit,
            b.Type.Tier, b.UpgradeProgress);
        if (now == _drawn)
            return;
        _drawn = now;
        if (b.Type.Tier != _shownTier)
            ShowLevel(b.Type);
        // Sites fade in as they are built; an idle mine is dimmed.
        float alpha = b.IsComplete ? 1f : 0.45f + 0.55f * b.ConstructionProgress;
        var tint = idle ? IdleTint : Colors.White;
        _body.SelfModulate = tint with { A = alpha };
        _team.SelfModulate = tint with { A = alpha };
        _overlay.QueueRedraw();
    }

    /// <summary>A blow landed: the walls flash and quiver for a moment (visual only).</summary>
    public void Hit() => _hitTime = HitSeconds;

    /// <summary>The player ordered an attack on it: a short red pulse, so it is clear what was clicked.</summary>
    public void MarkTarget() => _markTime = MarkSeconds;

    /// <summary>
    /// Squashed to <paramref name="squash"/> (feet planted: sprites scale about the bottom centre), then springing back
    /// past its size and settling: a site thumped down, a building finished or raised a level.
    /// </summary>
    public void Pop(Vector2 squash)
    {
        var tween = CreateTween().SetParallel();
        foreach (var sprite in new[] { _body, _team, _lights })
        {
            sprite.Scale = squash;
            tween.TweenProperty(sprite, "scale", Vector2.One, 0.42).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        }
    }

    private void UpdateFeedback(float delta)
    {
        if (_hitTime <= 0f && _markTime <= 0f && !_tinted)
            return;
        _hitTime = Mathf.Max(0f, _hitTime - delta);
        _markTime = Mathf.Max(0f, _markTime - delta);
        float hit = _hitTime / HitSeconds, mark = _markTime / MarkSeconds;
        // A quick shudder, a pixel or so, dying away; the flash and the pulse fade with it.
        var quiver = new Vector2(Mathf.Round(Mathf.Sin(_time * 95f) * 2.8f * hit), 0f);
        var tint = Colors.White.Lerp(HitFlash, hit) * Colors.White.Lerp(MarkTint, mark * (0.6f + 0.4f * Mathf.Sin(_time * 28f)));
        foreach (var sprite in new[] { _body, _team, _lights })
            sprite.Position = quiver;
        _body.Modulate = tint;
        _team.Modulate = _owner * tint;
        _tinted = hit > 0f || mark > 0f;
    }

    /// <summary>Plays the sheet, lights the windows at night and puffs the smoke.</summary>
    private void Animate(float delta, Building b, bool idle)
    {
        _time += delta;
        // Sites stand still; a mine moves only while it works.
        bool still = !b.IsComplete || idle && b.Type.IsMine;
        int frame = still ? 0 : (int)(_time * FramesPerSecond) % ArtLayout.BuildingFrames;
        _body.Frame = _team.Frame = _lights.Frame = frame;

        float night = _match.NightAmount;
        bool lit = night > 0.01f && b.IsComplete && !idle && _lights.Texture != null;
        _lights.Visible = lit;
        _glow.Visible = lit;
        if (lit)
        {
            float flicker = _flickers ? 0.82f + 0.18f * Mathf.Sin(_time * 9.1f) * Mathf.Sin(_time * 5.3f + 1f) : 1f;
            var light = _match.Lit(Colors.White) * night * flicker;
            _lights.Modulate = light;
            _glow.Modulate = light;
            _glow.QueueRedraw();
        }
        if (b.IsComplete && Chimneys.ContainsKey(b.Type.Id))
            _smoke.QueueRedraw();
    }

    public override void _Draw()
    {
        if (!_selected)
            return;
        float half = _building.Type.Size * SimConvert.TileSize / 2f;
        DrawSetTransform(new Vector2(0, -6), 0f, new Vector2(1f, 0.35f));
        DrawArc(Vector2.Zero, half + 12f, 0f, Mathf.Tau, 48, SelectionColor, 3f);
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    /// <summary>A soft warm halo round the lit windows, added over the dark (only drawn at night).</summary>
    private void DrawGlow()
    {
        if (!Halos.TryGetValue(_spriteName, out var halo) || halo.Radius <= 0f)
            return;
        var center = new Vector2(-FrameWidth / 2f, -SpriteHeight) + halo.Center;
        float r = halo.Radius;
        _glow.DrawTextureRect(_haloTexture, new Rect2(center - new Vector2(r, r), new Vector2(r, r) * 2f), false, HaloColor with { A = 0.55f });
        // A pool of light on the ground in front of the building.
        float g = _building.Type.Size * SimConvert.TileSize * 0.75f;
        _glow.DrawTextureRect(_haloTexture, new Rect2(new Vector2(-g, -g * 0.45f), new Vector2(g * 2f, g * 0.9f)), false, HaloColor with { A = 0.3f });
    }

    /// <summary>Puffs rising from the chimney and drifting with the wind, growing and thinning as they go.</summary>
    private void DrawSmoke()
    {
        if (!Chimneys.TryGetValue(_building.Type.Id, out var chimney) || !_building.IsComplete)
            return;
        var start = new Vector2(-FrameWidth / 2f, -SpriteHeight) + chimney;
        var wind = _match.Wind;
        for (int i = 0; i < SmokePuffs; i++)
        {
            float phase = (_time / SmokeSeconds + i / (float)SmokePuffs) % 1f;
            float age = phase * SmokeSeconds;
            var p = start + new Vector2(wind.X * age * 0.7f + Mathf.Sin(age * 1.7f + i) * 3f, -age * 12f + wind.Y * age * 0.3f);
            float size = Mathf.Round(4f + phase * 8f);
            float alpha = 0.55f * (1f - phase) * Mathf.Min(1f, phase * 6f);
            _smoke.DrawRect(new Rect2((p - new Vector2(size, size) / 2f).Round(), new Vector2(size, size)), SmokeColor with { A = alpha });
        }
    }

    private void DrawOverlay()
    {
        var b = _building;
        float half = b.Type.Size * SimConvert.TileSize / 2f;
        if (!b.IsComplete)
        {
            float height = SpriteHeight * 0.8f;
            // Poles and planks of the scaffolding, a timber's breadth thick.
            for (float x = -half + 4; x <= half - 4; x += 16)
                _overlay.DrawRect(new Rect2(x, -height, 2, height), Scaffold);
            _overlay.DrawRect(new Rect2(-half + 4, -height / 2, half * 2 - 8, 2), Scaffold);
            _overlay.DrawRect(new Rect2(-half + 4, -height, half * 2 - 8, 2), Scaffold);
        }

        const float width = 60f;
        const float step = 10f;
        float y = -SpriteHeight - 12f;
        if (!b.IsComplete)
        {
            Bar(y, b.ConstructionProgress, ConstructionFill);
            y -= step;
        }
        if (b.IsUpgrading)
        {
            Bar(y, b.UpgradeProgress, UpgradeFill);
            y -= step;
        }
        if (b.Health < b.MaxHealth || _selected)
        {
            float fraction = (float)b.Health / b.MaxHealth;
            Bar(y, fraction, new Color(1f - fraction, 0.3f + 0.6f * fraction, 0.2f));
            y -= step;
        }
        if (b.IsComplete && b.Deposit is { IsEndless: false } d)
        {
            if (d.IsExhausted)
            {
                Bar(y, d.RegenProgress, RegenFill);
                y -= step;
            }
            else if (_selected || DepositFraction(d) < LowReserve)
            {
                Bar(y, DepositFraction(d), ReserveFill);
                y -= step;
            }
        }
        if (b.Queue.Count > 0)
        {
            Bar(y, b.Queue[0].Progress, TrainingFill);
            int waiting = b.Queue.Count - 1;
            int dots = Mathf.Min(waiting, MaxQueueDots);
            for (int i = 0; i < dots; i++)
                _overlay.DrawRect(new Rect2(-width / 2 + i * 8, y - 8, 6, 6), TrainingFill);
            if (waiting > dots)
                _overlay.DrawString(ThemeDB.FallbackFont, new Vector2(-width / 2 + dots * 8 + 2, y - 1f), $"+{waiting - dots}",
                    fontSize: 16, modulate: TrainingFill);
        }

        void Bar(float barY, float fraction, Color fill)
        {
            _overlay.DrawRect(new Rect2(-width / 2, barY, width, 8), BarBack);
            _overlay.DrawRect(new Rect2(-width / 2 + 2, barY + 2, (width - 4) * Mathf.Clamp(fraction, 0f, 1f), 4), fill);
        }
    }

    /// <summary>Reserve left, or refill progress while exhausted, as 0..1.</summary>
    private static float DepositFraction(Warcrown.Sim.World.GoldDeposit d) =>
        d.IsExhausted ? d.RegenProgress : (float)d.Reserve / d.Capacity;

    private Sprite2D MakeSprite(Color modulate)
    {
        var sprite = new Sprite2D { Centered = false, Modulate = modulate, Hframes = ArtLayout.BuildingFrames };
        AddChild(sprite);
        return sprite;
    }

    /// <summary>The pictures of the building's level (the first level's if a higher one has none of its own).</summary>
    private void ShowLevel(BuildingType type)
    {
        _shownTier = type.Tier;
        _spriteName = Sprites.BuildingName(type);
        SetTexture(_body, Sprites.Path(_spriteName));
        SetTexture(_team, Sprites.Path(_spriteName, "_team"));
        string lights = Sprites.Path(_spriteName, "_lights");
        if (ResourceLoader.Exists(lights))
        {
            SetTexture(_lights, lights);
            if (!Halos.ContainsKey(_spriteName))
                Halos[_spriteName] = MeasureLights(_lights.Texture);
        }
        else
        {
            _lights.Texture = null;
        }
        QueueRedraw();
    }

    /// <summary>The middle of the lit pixels of the first frame, and how far round them the halo reaches.</summary>
    private static (Vector2 Center, float Radius) MeasureLights(Texture2D lights)
    {
        var image = lights.GetImage();
        image.Decompress();
        int w = image.GetWidth() / ArtLayout.BuildingFrames, h = image.GetHeight();
        var sum = Vector2.Zero;
        int count = 0;
        var min = new Vector2(w, h);
        var max = Vector2.Zero;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (image.GetPixel(x, y).A <= 0f)
                    continue;
                var p = new Vector2(x + 0.5f, y + 0.5f);
                sum += p;
                count++;
                min = min.Min(p);
                max = max.Max(p);
            }
        if (count == 0)
            return (Vector2.Zero, 0f);
        var size = max - min;
        return (sum / count, Mathf.Max(size.X, size.Y) / 2f + 28f);
    }

    private static void SetTexture(Sprite2D sprite, string path)
    {
        var texture = GD.Load<Texture2D>(path);
        sprite.Texture = texture;
        sprite.Offset = new Vector2(-texture.GetWidth() / (float)ArtLayout.BuildingFrames / 2f, -texture.GetHeight());
    }
}

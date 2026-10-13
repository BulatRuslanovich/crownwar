using Godot;
using Lordsio.Core;
using Lordsio.Sim.Buildings;

namespace Lordsio.Buildings;

/// <summary>
/// A building drawn from Assets/Sprites/&lt;type&gt;.png plus its faction-colour mask (&lt;type&gt;_team.png),
/// with scaffolding while under construction and construction / health / training bars above.
/// The node origin is the bottom-centre of the footprint, so tall buildings y-sort correctly with units.
/// </summary>
public partial class BuildingView : Node2D
{
    private static readonly Color Shadow = new(0f, 0f, 0f, 0.3f);
    private static readonly Color Scaffold = new("c8a26a");
    private static readonly Color SelectionColor = new("7cff6b");
    private static readonly Color BarBack = new(0f, 0f, 0f, 0.65f);
    private static readonly Color TrainingFill = new("7cff6b");
    private static readonly Color ConstructionFill = new("ffd44a");
    private static readonly Color ReserveFill = new("e8b830");
    private static readonly Color RegenFill = new("5fb4ff");
    /// <summary>A mine on an exhausted deposit stands idle: drawn dimmed.</summary>
    private static readonly Color IdleTint = new(0.42f, 0.42f, 0.5f);
    /// <summary>The reserve bar shows up by itself once a deposit is this close to running dry.</summary>
    private const float LowReserve = 0.25f;
    /// <summary>Waiting recruits drawn as dots above the training bar; a longer queue shows the rest as "+N".</summary>
    private const int MaxQueueDots = 5;

    private Building _building = null!;
    private Sprite2D _body = null!;
    private Sprite2D _team = null!;
    private Node2D _overlay = null!;
    private Color _owner;
    private bool _selected;
    private (int Health, bool Complete, float Construction, int Queue, float Training, bool Idle, int Deposit) _drawn;

    public void Initialize(Building building)
    {
        _building = building;
        _owner = FactionColors.Get(building.FactionId);
        var bottom = building.Position + new System.Numerics.Vector2(0f, building.Type.Size / 2f);
        Position = SimConvert.ToWorld(bottom);

        string id = building.Type.Id;
        _body = MakeSprite($"res://Assets/Sprites/{id}.png", Colors.White);
        _team = MakeSprite($"res://Assets/Sprites/{id}_team.png", _owner);
        _overlay = new Node2D { Name = "Overlay" };
        _overlay.Draw += DrawOverlay;
        AddChild(_overlay);
    }

    public Building Building => _building;

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

    public override void _Process(double delta)
    {
        if (_ghost)
            return; // frozen as last seen
        var b = _building;
        bool idle = b.Deposit is { IsExhausted: true };
        // Deposit bars change in whole percents, so the overlay is not redrawn every tick.
        int deposit = b.Deposit is { IsEndless: false } d ? (int)(DepositFraction(d) * 100f) : 0;
        var now = (b.Health, b.IsComplete, b.ConstructionProgress, b.Queue.Count, b.Queue.Count > 0 ? b.Queue[0].Progress : 0f, idle, deposit);
        if (now == _drawn)
            return;
        _drawn = now;
        // Sites fade in as they are built; an idle mine is dimmed.
        float alpha = b.IsComplete ? 1f : 0.45f + 0.55f * b.ConstructionProgress;
        var tint = idle ? IdleTint : Colors.White;
        _body.SelfModulate = tint with { A = alpha };
        _team.SelfModulate = tint with { A = alpha };
        _overlay.QueueRedraw();
    }

    public override void _Draw()
    {
        float half = _building.Type.Size * SimConvert.TileSize / 2f;
        DrawSetTransform(new Vector2(0, -3), 0f, new Vector2(1f, 0.35f));
        DrawCircle(Vector2.Zero, half + 2f, Shadow);
        if (_selected)
            DrawArc(Vector2.Zero, half + 6f, 0f, Mathf.Tau, 40, SelectionColor, 1.5f);
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    private void DrawOverlay()
    {
        var b = _building;
        float half = b.Type.Size * SimConvert.TileSize / 2f;
        if (!b.IsComplete)
        {
            float height = SpriteHeight * 0.8f;
            for (float x = -half + 2; x <= half - 2; x += 8)
                _overlay.DrawRect(new Rect2(x, -height, 1, height), Scaffold);
            _overlay.DrawRect(new Rect2(-half + 2, -height / 2, half * 2 - 4, 1), Scaffold);
            _overlay.DrawRect(new Rect2(-half + 2, -height, half * 2 - 4, 1), Scaffold);
        }

        const float width = 30f;
        float y = -SpriteHeight - 6f;
        if (!b.IsComplete)
        {
            Bar(y, b.ConstructionProgress, ConstructionFill);
            y -= 5f;
        }
        if (b.Health < b.MaxHealth || _selected)
        {
            float fraction = (float)b.Health / b.MaxHealth;
            Bar(y, fraction, new Color(1f - fraction, 0.3f + 0.6f * fraction, 0.2f));
            y -= 5f;
        }
        if (b.IsComplete && b.Deposit is { IsEndless: false } d)
        {
            if (d.IsExhausted)
            {
                Bar(y, d.RegenProgress, RegenFill);
                y -= 5f;
            }
            else if (_selected || DepositFraction(d) < LowReserve)
            {
                Bar(y, DepositFraction(d), ReserveFill);
                y -= 5f;
            }
        }
        if (b.Queue.Count > 0)
        {
            Bar(y, b.Queue[0].Progress, TrainingFill);
            int waiting = b.Queue.Count - 1;
            int dots = Mathf.Min(waiting, MaxQueueDots);
            for (int i = 0; i < dots; i++)
                _overlay.DrawRect(new Rect2(-width / 2 + i * 4, y - 4, 3, 3), TrainingFill);
            if (waiting > dots)
                _overlay.DrawString(ThemeDB.FallbackFont, new Vector2(-width / 2 + dots * 4 + 1, y - 0.5f), $"+{waiting - dots}",
                    fontSize: 8, modulate: TrainingFill);
        }

        void Bar(float barY, float fraction, Color fill)
        {
            _overlay.DrawRect(new Rect2(-width / 2, barY, width, 4), BarBack);
            _overlay.DrawRect(new Rect2(-width / 2 + 1, barY + 1, (width - 2) * Mathf.Clamp(fraction, 0f, 1f), 2), fill);
        }
    }

    /// <summary>Reserve left, or refill progress while exhausted, as 0..1.</summary>
    private static float DepositFraction(Lordsio.Sim.World.GoldDeposit d) =>
        d.IsExhausted ? d.RegenProgress : (float)d.Reserve / d.Capacity;

    private Sprite2D MakeSprite(string path, Color modulate)
    {
        var texture = GD.Load<Texture2D>(path);
        var sprite = new Sprite2D
        {
            Texture = texture,
            Centered = false,
            Offset = new Vector2(-texture.GetWidth() / 2f, -texture.GetHeight()),
            Modulate = modulate,
        };
        AddChild(sprite);
        return sprite;
    }
}

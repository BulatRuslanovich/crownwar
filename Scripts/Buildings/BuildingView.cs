using Godot;
using Lordsio.Core;
using Lordsio.Sim.Buildings;

namespace Lordsio.Buildings;

/// <summary>
/// Placeholder pixel building drawn in code, one look per type, with an owner-coloured flag,
/// scaffolding while under construction, and health / training bars.
/// The node origin is the bottom-center of the footprint, so tall buildings y-sort correctly with units.
/// </summary>
public partial class BuildingView : Node2D
{
    private static readonly Color Shadow = new(0f, 0f, 0f, 0.3f);
    private static readonly Color Stone = new("8c8781");
    private static readonly Color StoneLight = new("aaa59e");
    private static readonly Color StoneDark = new("625d58");
    private static readonly Color Gate = new("3a2a20");
    private static readonly Color Pole = new("4a3a2a");
    private static readonly Color Plank = new("8a6239");
    private static readonly Color PlankDark = new("5e4026");
    private static readonly Color Roof = new("a4523a");
    private static readonly Color RoofDark = new("7e3b29");
    private static readonly Color Gold = new("ffd44a");
    private static readonly Color Scaffold = new("c8a26a");
    private static readonly Color SelectionColor = new("7cff6b");
    private static readonly Color BarBack = new(0f, 0f, 0f, 0.65f);
    private static readonly Color TrainingFill = new("7cff6b");
    private static readonly Color ConstructionFill = new("ffd44a");

    private Building _building = null!;
    private Color _owner;
    private bool _selected;
    private (int Health, bool Complete, float Construction, int Queue, float Training) _drawn;

    public void Initialize(Building building)
    {
        _building = building;
        _owner = FactionColors.Get(building.FactionId);
        var bottom = building.Position + new System.Numerics.Vector2(0f, building.Type.Size / 2f);
        Position = SimConvert.ToWorld(bottom);
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
        var b = _building;
        var now = (b.Health, b.IsComplete, b.ConstructionProgress, b.Queue.Count, b.Queue.Count > 0 ? b.Queue[0].Progress : 0f);
        if (now == _drawn)
            return;
        _drawn = now;
        // Sites fade in as they are built.
        Modulate = b.IsComplete ? Colors.White : new Color(1f, 1f, 1f, 0.55f + 0.45f * b.ConstructionProgress);
        QueueRedraw();
    }

    public override void _Draw()
    {
        float half = _building.Type.Size * SimConvert.TileSize / 2f;
        DrawSetTransform(new Vector2(0, -2), 0f, new Vector2(1f, 0.35f));
        DrawCircle(Vector2.Zero, half + 2f, Shadow);
        if (_selected)
            DrawArc(Vector2.Zero, half + 6f, 0f, Mathf.Tau, 40, SelectionColor, 1.5f);
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);

        float top = _building.Type switch
        {
            { IsCapital: true } => DrawCastle(),
            { IsMine: true } => DrawMine(),
            { Attack: not null } => DrawTower(),
            _ => DrawBarracks(),
        };

        if (!_building.IsComplete)
            DrawScaffolding(half);
        DrawBars(top - 6f);
    }

    private float DrawCastle()
    {
        // 48 px footprint: curtain wall, gate, keep and two towers.
        DrawRect(new Rect2(-20, -26, 40, 24), Stone);
        DrawRect(new Rect2(-20, -4, 40, 3), StoneDark);
        DrawRect(new Rect2(-5, -14, 10, 13), Gate);
        DrawRect(new Rect2(-8, -42, 16, 20), StoneLight);
        DrawRect(new Rect2(-8, -42, 16, 2), StoneDark);
        foreach (int x in new[] { -24, 14 })
        {
            DrawRect(new Rect2(x, -34, 10, 33), StoneLight);
            DrawRect(new Rect2(x, -4, 10, 3), StoneDark);
            for (int c = 0; c < 10; c += 4)
                DrawRect(new Rect2(x + c, -38, 3, 4), StoneLight);
        }
        for (int c = -18; c < 18; c += 5)
            DrawRect(new Rect2(c, -30, 3, 4), Stone);
        return DrawFlag(0, -42);
    }

    private float DrawBarracks()
    {
        // 32 px footprint: long timber hall with a red roof.
        DrawRect(new Rect2(-14, -16, 28, 15), Plank);
        DrawRect(new Rect2(-14, -4, 28, 3), PlankDark);
        for (int x = -12; x < 14; x += 6)
            DrawRect(new Rect2(x, -16, 1, 13), PlankDark);
        DrawRect(new Rect2(-3, -10, 6, 9), Gate);
        for (int row = 0; row < 6; row++)
            DrawRect(new Rect2(-16 + row, -17 - row, 32 - 2 * row, 1), row == 0 ? RoofDark : Roof);
        return DrawFlag(10, -22);
    }

    private float DrawTower()
    {
        // 16 px footprint, tall: stone shaft with an archer platform.
        DrawRect(new Rect2(-6, -28, 12, 27), StoneLight);
        DrawRect(new Rect2(-6, -4, 12, 3), StoneDark);
        DrawRect(new Rect2(-1, -20, 2, 5), Gate);
        DrawRect(new Rect2(-8, -32, 16, 5), Stone);
        for (int c = -8; c < 8; c += 4)
            DrawRect(new Rect2(c, -35, 2, 3), Stone);
        DrawRect(new Rect2(-2, -36, 4, 4), _owner);
        return DrawFlag(3, -35);
    }

    private float DrawMine()
    {
        // 32 px footprint over the deposit: timber headframe, tunnel mouth and a cart of gold.
        DrawRect(new Rect2(-10, -12, 14, 11), StoneDark);
        DrawRect(new Rect2(-7, -9, 8, 8), Gate);
        DrawRect(new Rect2(-12, -26, 2, 25), Plank);
        DrawRect(new Rect2(4, -26, 2, 25), Plank);
        DrawRect(new Rect2(-13, -27, 20, 3), PlankDark);
        DrawLine(new Vector2(-11, -24), new Vector2(5, -13), Plank, 1f);
        DrawRect(new Rect2(7, -8, 9, 5), PlankDark);
        DrawRect(new Rect2(8, -10, 7, 2), Gold);
        DrawRect(new Rect2(8, -3, 2, 2), Gate);
        DrawRect(new Rect2(13, -3, 2, 2), Gate);
        return DrawFlag(-3, -27);
    }

    private void DrawScaffolding(float half)
    {
        for (float x = -half + 2; x <= half - 2; x += 8)
            DrawRect(new Rect2(x, -half * 1.6f, 1, half * 1.6f), Scaffold);
        DrawRect(new Rect2(-half + 2, -half, half * 2 - 4, 1), Scaffold);
        DrawRect(new Rect2(-half + 2, -half * 1.6f, half * 2 - 4, 1), Scaffold);
    }

    private float DrawFlag(float x, float baseY)
    {
        DrawRect(new Rect2(x, baseY - 10, 1, 10), Pole);
        DrawRect(new Rect2(x + 1, baseY - 10, 7, 4), _owner);
        return baseY - 10;
    }

    private void DrawBars(float y)
    {
        const float width = 30f;
        var b = _building;
        if (!b.IsComplete)
        {
            DrawBar(y, b.ConstructionProgress, ConstructionFill);
            y -= 5f;
        }
        if (b.Health < b.MaxHealth || _selected)
        {
            float fraction = (float)b.Health / b.MaxHealth;
            DrawBar(y, fraction, new Color(1f - fraction, 0.3f + 0.6f * fraction, 0.2f));
            y -= 5f;
        }
        if (b.Queue.Count > 0)
        {
            DrawBar(y, b.Queue[0].Progress, TrainingFill);
            for (int i = 1; i < b.Queue.Count; i++)
                DrawRect(new Rect2(-width / 2 + (i - 1) * 4, y - 4, 3, 3), TrainingFill);
        }

        void DrawBar(float barY, float fraction, Color fill)
        {
            DrawRect(new Rect2(-width / 2, barY, width, 4), BarBack);
            DrawRect(new Rect2(-width / 2 + 1, barY + 1, (width - 2) * Mathf.Clamp(fraction, 0f, 1f), 2), fill);
        }
    }
}

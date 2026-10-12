using Godot;
using Lordsio.Core;
using Lordsio.Sim.Territory;

namespace Lordsio.Territory;

/// <summary>
/// Placeholder pixel settlement drawn in code (castle, town or village) with an owner-coloured flag,
/// a production bar and a capture bar. The node origin is the settlement cell's center, so it
/// y-sorts together with units.
/// </summary>
public partial class SettlementView : Node2D
{
    private static readonly Color Shadow = new(0f, 0f, 0f, 0.3f);
    private static readonly Color Stone = new("8c8781");
    private static readonly Color StoneLight = new("aaa59e");
    private static readonly Color StoneDark = new("625d58");
    private static readonly Color Gate = new("3a2a20");
    private static readonly Color Pole = new("4a3a2a");
    private static readonly Color Plaster = new("d8c8a8");
    private static readonly Color Timber = new("6b4a2e");
    private static readonly Color Roof = new("a4523a");
    private static readonly Color RoofDark = new("7e3b29");
    private static readonly Color Thatch = new("c9a65a");
    private static readonly Color ThatchDark = new("9c7f3e");
    private static readonly Color SelectionColor = new("7cff6b");
    private static readonly Color BarBack = new(0f, 0f, 0f, 0.6f);
    private static readonly Color ProductionFill = new("7cff6b");
    private static readonly Color ContestedColor = new("ffd84a");

    private Settlement _settlement = null!;
    private bool _selected;
    private (int Owner, int Queue, float Production, int Capturer, float Capture, bool Contested) _drawn;

    public void Initialize(Settlement settlement)
    {
        _settlement = settlement;
        Position = SimConvert.ToWorld(settlement.Cell.Center);
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
        // Redraw only when something visible changed.
        var s = _settlement;
        var now = (s.OwnerId, s.Queue.Count, s.Queue.Count > 0 ? s.Queue[0].Progress : 0f,
            s.CapturingFactionId, s.CaptureProgress, s.IsContested);
        if (now == _drawn)
            return;
        _drawn = now;
        QueueRedraw();
    }

    public override void _Draw()
    {
        float size = _settlement.Type.Id switch { "castle" => 17f, "town" => 15f, _ => 11f };
        DrawSetTransform(new Vector2(0, 4), 0f, new Vector2(1f, 0.4f));
        DrawCircle(Vector2.Zero, size, Shadow);
        if (_selected)
            DrawArc(Vector2.Zero, size + 4f, 0f, Mathf.Tau, 32, SelectionColor, 1.5f);
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);

        float top = _settlement.Type.Id switch
        {
            "castle" => DrawCastle(),
            "town" => DrawTown(),
            _ => DrawVillage(),
        };
        DrawBars(top - 8f);
    }

    /// <summary>Each Draw* returns the y of the flag top, so the bars sit just above the building.</summary>
    private float DrawCastle()
    {
        DrawRect(new Rect2(-12, -14, 24, 18), Stone);
        DrawRect(new Rect2(-12, 2, 24, 2), StoneDark);
        DrawRect(new Rect2(-3, -5, 6, 9), Gate);
        DrawRect(new Rect2(-5, -26, 10, 14), StoneLight);
        DrawRect(new Rect2(-5, -26, 10, 2), StoneDark);
        foreach (int x in new[] { -16, 9 })
        {
            DrawRect(new Rect2(x, -20, 7, 24), StoneLight);
            DrawRect(new Rect2(x, 2, 7, 2), StoneDark);
            for (int c = 0; c < 7; c += 3)
                DrawRect(new Rect2(x + c, -23, 2, 3), StoneLight);
        }
        for (int c = -12; c < 12; c += 4)
            DrawRect(new Rect2(c, -17, 2, 3), Stone);
        return DrawFlag(0, -26);
    }

    private float DrawTown()
    {
        // Low palisade-like wall with two houses and a tower.
        DrawRect(new Rect2(-14, -4, 28, 7), Stone);
        DrawRect(new Rect2(-14, 1, 28, 2), StoneDark);
        DrawHouse(-11, -4, 9, Roof, RoofDark);
        DrawHouse(2, -4, 9, Roof, RoofDark);
        DrawRect(new Rect2(-3, -18, 6, 16), StoneLight);
        DrawRect(new Rect2(-3, -18, 6, 2), StoneDark);
        DrawRect(new Rect2(-1, -8, 2, 6), Gate);
        return DrawFlag(0, -18);
    }

    private float DrawVillage()
    {
        DrawHouse(-9, 2, 8, Thatch, ThatchDark);
        DrawHouse(1, 0, 8, Thatch, ThatchDark);
        return DrawFlag(-1, -10);
    }

    private void DrawHouse(float x, float baseY, float width, Color roof, Color roofDark)
    {
        DrawRect(new Rect2(x, baseY - 6, width, 6), Plaster);
        DrawRect(new Rect2(x + width / 2 - 1, baseY - 4, 2, 4), Timber);
        for (int row = 0; row < 4; row++)
        {
            float inset = row;
            DrawRect(new Rect2(x - 1 + inset, baseY - 7 - row, width + 2 - 2 * inset, 1), row == 0 ? roofDark : roof);
        }
    }

    private float DrawFlag(float x, float baseY)
    {
        DrawRect(new Rect2(x, baseY - 12, 1, 12), Pole);
        DrawRect(new Rect2(x + 1, baseY - 12, 8, 5), FactionColors.Get(_settlement.OwnerId));
        return baseY - 12;
    }

    private void DrawBars(float y)
    {
        var s = _settlement;
        if (s.CaptureProgress > 0f || s.IsContested)
        {
            var color = s.IsContested ? ContestedColor : FactionColors.Get(s.CapturingFactionId);
            DrawRect(new Rect2(-14, y, 28, 4), BarBack);
            DrawRect(new Rect2(-13, y + 1, 26 * s.CaptureProgress, 2), color);
            if (s.IsContested)
                DrawRect(new Rect2(-14, y, 28, 4), ContestedColor, filled: false, width: 1f);
            y -= 6f;
        }
        if (s.Queue.Count > 0)
        {
            DrawRect(new Rect2(-14, y, 28, 4), BarBack);
            DrawRect(new Rect2(-13, y + 1, 26 * s.Queue[0].Progress, 2), ProductionFill);
            for (int i = 1; i < s.Queue.Count; i++)
                DrawRect(new Rect2(-14 + (i - 1) * 4, y - 4, 3, 3), ProductionFill);
        }
    }
}

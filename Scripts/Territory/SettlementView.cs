using Godot;
using Lordsio.Core;
using Lordsio.Sim.Territory;

namespace Lordsio.Territory;

/// <summary>
/// Placeholder pixel castle drawn in code, with an owner-coloured flag and a production bar.
/// The node origin is the settlement cell's center, so it y-sorts together with units.
/// </summary>
public partial class SettlementView : Node2D
{
    private static readonly Color Shadow = new(0f, 0f, 0f, 0.3f);
    private static readonly Color Stone = new("8c8781");
    private static readonly Color StoneLight = new("aaa59e");
    private static readonly Color StoneDark = new("625d58");
    private static readonly Color Gate = new("3a2a20");
    private static readonly Color Pole = new("4a3a2a");
    private static readonly Color SelectionColor = new("7cff6b");
    private static readonly Color BarBack = new(0f, 0f, 0f, 0.6f);
    private static readonly Color BarFill = new("7cff6b");

    private Settlement _settlement = null!;
    private bool _selected;
    private int _drawnOwner = int.MinValue;
    private float _drawnProgress = -1f;
    private int _drawnQueue = -1;

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
        float progress = _settlement.Queue.Count > 0 ? _settlement.Queue[0].Progress : 0f;
        if (_settlement.OwnerId == _drawnOwner && _settlement.Queue.Count == _drawnQueue
            && Mathf.IsEqualApprox(progress, _drawnProgress))
            return;
        _drawnOwner = _settlement.OwnerId;
        _drawnQueue = _settlement.Queue.Count;
        _drawnProgress = progress;
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawSetTransform(new Vector2(0, 4), 0f, new Vector2(1f, 0.4f));
        DrawCircle(Vector2.Zero, 17f, Shadow);
        if (_selected)
            DrawArc(Vector2.Zero, 21f, 0f, Mathf.Tau, 32, SelectionColor, 1.5f);
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);

        // Curtain wall with gate, two towers and a keep.
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

        var color = FactionColors.Get(_settlement.OwnerId);
        DrawRect(new Rect2(0, -38, 1, 12), Pole);
        DrawRect(new Rect2(1, -38, 8, 5), color);

        if (_settlement.Queue.Count > 0)
        {
            DrawRect(new Rect2(-14, -46, 28, 4), BarBack);
            DrawRect(new Rect2(-13, -45, 26 * _settlement.Queue[0].Progress, 2), BarFill);
            for (int i = 1; i < _settlement.Queue.Count; i++)
                DrawRect(new Rect2(-14 + (i - 1) * 4, -50, 3, 3), BarFill);
        }
    }
}

using Godot;
using Lordsio.Core;
using Lordsio.Sim.World;

namespace Lordsio.World;

/// <summary>
/// Gold ore patches on the ground (Assets/Sprites/gold.png). Rich deposits get a golden glint so they
/// stand out when planning expansions; exhausted ones are greyed until they refill. A mine built on a
/// deposit is drawn on top of it.
/// </summary>
public partial class DepositsView : Node2D
{
    private static readonly Color Glint = new(1f, 0.85f, 0.3f, 0.35f);
    private static readonly Color Exhausted = new(0.45f, 0.45f, 0.5f, 0.8f);

    private MatchController _match = null!;
    private Texture2D _ore = null!;
    private bool[] _drawnExhausted = System.Array.Empty<bool>();

    public void Initialize(MatchController match)
    {
        _match = match;
        _ore = GD.Load<Texture2D>("res://Assets/Sprites/gold.png");
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        // Redraw only when some deposit ran dry or refilled.
        var deposits = _match.Sim.Deposits;
        if (_drawnExhausted.Length != deposits.Count)
            _drawnExhausted = new bool[deposits.Count];
        bool changed = false;
        for (int i = 0; i < deposits.Count; i++)
        {
            changed |= _drawnExhausted[i] != deposits[i].IsExhausted;
            _drawnExhausted[i] = deposits[i].IsExhausted;
        }
        if (changed)
            QueueRedraw();
    }

    public override void _Draw()
    {
        float size = GoldDeposit.Size * SimConvert.TileSize;
        foreach (var deposit in _match.Sim.Deposits)
        {
            var origin = SimConvert.ToWorld(new System.Numerics.Vector2(deposit.Anchor.X, deposit.Anchor.Y));
            if (deposit.IsExhausted)
            {
                DrawTexture(_ore, origin, Exhausted);
                continue;
            }
            if (deposit.Yield > MapLoader.DefaultDepositYield)
            {
                DrawSetTransform(origin + new Vector2(size / 2, size / 2 + 4), 0f, new Vector2(1f, 0.55f));
                DrawCircle(Vector2.Zero, size * 0.7f, Glint);
                DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
            }
            DrawTexture(_ore, origin);
        }
    }
}

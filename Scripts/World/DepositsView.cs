using Godot;
using Warcrown.Core;
using Warcrown.Sim.World;

namespace Warcrown.World;

/// <summary>
/// Gold ore outcrops on the ground (Assets/Sprites/gold.png). Rich deposits get a golden glint so they
/// stand out when planning expansions; exhausted ones are greyed until they refill. A finished mine is dug
/// into the deposit's own rocks (its sprite draws them), so the deposit under it is not drawn; under a mine
/// still being built it shows through the fading-in site.
/// </summary>
public partial class DepositsView : Node2D
{
    private static readonly Color Glint = new(1f, 0.85f, 0.3f, 0.35f);
    private static readonly Color Exhausted = new(0.45f, 0.45f, 0.5f, 0.8f);

    private enum Look : byte { Ore, Exhausted, UnderMine }

    private MatchController _match = null!;
    private Texture2D _ore = null!;
    private Look[] _drawn = System.Array.Empty<Look>();

    public void Initialize(MatchController match)
    {
        _match = match;
        _ore = GD.Load<Texture2D>(Sprites.Path("gold"));
        QueueRedraw();
    }

    private Look LookOf(GoldDeposit deposit)
    {
        if (deposit.MineId != 0 && _match.Sim.GetBuilding(deposit.MineId) is { IsAlive: true, IsComplete: true })
            return Look.UnderMine;
        return deposit.IsExhausted ? Look.Exhausted : Look.Ore;
    }

    public override void _Process(double delta)
    {
        // Redraw only when some deposit ran dry, refilled, or got or lost its mine.
        var deposits = _match.Sim.Deposits;
        if (_drawn.Length != deposits.Count)
            _drawn = new Look[deposits.Count];
        bool changed = false;
        for (int i = 0; i < deposits.Count; i++)
        {
            var look = LookOf(deposits[i]);
            changed |= _drawn[i] != look;
            _drawn[i] = look;
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
            switch (LookOf(deposit))
            {
                case Look.UnderMine:
                    continue;
                case Look.Exhausted:
                    DrawTexture(_ore, origin, Exhausted);
                    continue;
            }
            if (deposit.Yield > MapLoader.DefaultDepositYield)
            {
                DrawSetTransform(origin + new Vector2(size / 2, size / 2 + 8), 0f, new Vector2(1f, 0.55f));
                DrawCircle(Vector2.Zero, size * 0.7f, Glint);
                DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
            }
            DrawTexture(_ore, origin);
        }
    }
}

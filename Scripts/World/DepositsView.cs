using System;
using Godot;
using Lordsio.Core;
using Lordsio.Sim.World;

namespace Lordsio.World;

/// <summary>Gold ore patches on the ground. A mine built on one is drawn on top of it.</summary>
public partial class DepositsView : Node2D
{
    private static readonly Color Rock = new("6e6862");
    private static readonly Color RockLight = new("948d85");
    private static readonly Color Gold = new("ffd44a");
    private static readonly Color GoldDark = new("c79a22");

    private MatchController _match = null!;

    public void Initialize(MatchController match)
    {
        _match = match;
        QueueRedraw();
    }

    public override void _Draw()
    {
        int size = GoldDeposit.Size * SimConvert.TileSize;
        foreach (var deposit in _match.Sim.Deposits)
        {
            var origin = SimConvert.ToWorld(new System.Numerics.Vector2(deposit.Anchor.X, deposit.Anchor.Y));
            var rng = new Random(deposit.Id * 7919);
            // Scattered boulders with gold veins; richer deposits show more gold.
            for (int i = 0; i < 7; i++)
            {
                float x = rng.Next(2, size - 8), y = rng.Next(2, size - 7);
                float w = rng.Next(5, 9), h = rng.Next(4, 7);
                DrawRect(new Rect2(origin.X + x, origin.Y + y, w, h), Rock);
                DrawRect(new Rect2(origin.X + x, origin.Y + y, w, 1), RockLight);
            }
            for (int i = 0; i < 4 + deposit.Yield * 2; i++)
            {
                var c = rng.Next(3) == 0 ? GoldDark : Gold;
                DrawRect(new Rect2(origin.X + rng.Next(3, size - 4), origin.Y + rng.Next(3, size - 4), 2, 2), c);
            }
        }
    }
}

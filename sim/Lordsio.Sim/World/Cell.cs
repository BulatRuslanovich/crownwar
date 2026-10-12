using System.Numerics;

namespace Lordsio.Sim.World;

/// <summary>Integer tile coordinate. World positions are in tile units: cell (x, y) spans [x, x+1) × [y, y+1).</summary>
public readonly record struct Cell(int X, int Y)
{
    public Vector2 Center => new(X + 0.5f, Y + 0.5f);

    public static Cell FromPosition(Vector2 position) =>
        new((int)MathF.Floor(position.X), (int)MathF.Floor(position.Y));

    public override string ToString() => $"({X}, {Y})";
}

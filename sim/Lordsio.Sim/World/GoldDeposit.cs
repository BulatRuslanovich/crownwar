using System.Numerics;

namespace Lordsio.Sim.World;

/// <summary>
/// A patch of gold ore with a square footprint. Only a mine built exactly on it earns gold:
/// <see cref="Yield"/> per second, for as long as the mine stands. Deposits never run out.
/// </summary>
public sealed class GoldDeposit
{
    public const int Size = 2;

    public GoldDeposit(int id, Cell anchor, int yield)
    {
        if (yield <= 0)
            throw new ArgumentOutOfRangeException(nameof(yield), "Deposit yield must be positive.");
        Id = id;
        Anchor = anchor;
        Yield = yield;
    }

    public int Id { get; }

    /// <summary>Top-left cell of the footprint.</summary>
    public Cell Anchor { get; }

    public int Yield { get; }

    public Vector2 Center => new(Anchor.X + Size / 2f, Anchor.Y + Size / 2f);

    public bool Contains(Cell cell) =>
        cell.X >= Anchor.X && cell.Y >= Anchor.Y && cell.X < Anchor.X + Size && cell.Y < Anchor.Y + Size;

    /// <summary>Id of the mine standing on the deposit, or 0.</summary>
    public int MineId { get; internal set; }
}

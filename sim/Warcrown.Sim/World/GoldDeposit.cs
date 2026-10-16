using System.Numerics;

namespace Warcrown.Sim.World;

/// <summary>
/// A patch of gold ore with a square footprint. Only a mine built exactly on it earns gold:
/// <see cref="Yield"/> per second while the deposit has <see cref="Reserve"/> left. An emptied deposit is
/// <see cref="IsExhausted"/> for a while, then refills completely; the mine keeps standing and resumes.
/// A deposit with no capacity never runs out.
/// </summary>
public sealed class GoldDeposit
{
    public const int Size = 2;

    private int _mineRemainder;

    /// <param name="capacity">Gold the deposit holds when full; 0 for an endless deposit.</param>
    /// <param name="regenTicks">Ticks an exhausted deposit takes to refill.</param>
    public GoldDeposit(int id, Cell anchor, int yield, int capacity = 0, int regenTicks = 0)
    {
        if (yield <= 0)
            throw new ArgumentOutOfRangeException(nameof(yield), "Deposit yield must be positive.");
        if (capacity < 0 || regenTicks < 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity and regeneration time cannot be negative.");
        Id = id;
        Anchor = anchor;
        Yield = yield;
        Capacity = capacity;
        Reserve = capacity;
        RegenTicks = regenTicks;
    }

    public int Id { get; }

    /// <summary>Top-left cell of the footprint.</summary>
    public Cell Anchor { get; }

    public int Yield { get; }

    /// <summary>Gold when full; 0 means the deposit never runs out.</summary>
    public int Capacity { get; }

    public bool IsEndless => Capacity == 0;

    /// <summary>Gold left to mine before the deposit is exhausted.</summary>
    public int Reserve { get; private set; }

    /// <summary>Ticks an exhausted deposit needs to refill.</summary>
    public int RegenTicks { get; }

    /// <summary>Ticks until an exhausted deposit is full again; 0 while it can be mined.</summary>
    public int RegenTicksLeft { get; private set; }

    public bool IsExhausted => RegenTicksLeft > 0;

    /// <summary>How far an exhausted deposit has refilled, 0..1.</summary>
    public float RegenProgress => RegenTicks == 0 || !IsExhausted ? 1f : 1f - (float)RegenTicksLeft / RegenTicks;

    /// <summary>One tick of mining; true when this tick emptied the deposit.</summary>
    internal bool Mine(int ticksPerSecond)
    {
        if (IsEndless || IsExhausted)
            return false;
        _mineRemainder += Yield;
        Reserve -= _mineRemainder / ticksPerSecond;
        _mineRemainder %= ticksPerSecond;
        if (Reserve > 0)
            return false;
        Reserve = 0;
        _mineRemainder = 0;
        RegenTicksLeft = Math.Max(1, RegenTicks);
        return true;
    }

    /// <summary>One tick of refilling an exhausted deposit; true when it is full again.</summary>
    internal bool Regenerate()
    {
        if (!IsExhausted)
            return false;
        RegenTicksLeft--;
        if (RegenTicksLeft > 0)
            return false;
        Reserve = Capacity;
        return true;
    }

    public Vector2 Center => Anchor.CenterOfSquare(Size);

    public bool Contains(Cell cell) =>
        cell.X >= Anchor.X && cell.Y >= Anchor.Y && cell.X < Anchor.X + Size && cell.Y < Anchor.Y + Size;

    /// <summary>Id of the mine standing on the deposit, or 0.</summary>
    public int MineId { get; internal set; }
}

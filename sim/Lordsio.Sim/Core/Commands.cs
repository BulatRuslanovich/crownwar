using System.Numerics;

namespace Lordsio.Sim.Core;

/// <summary>
/// The only way players and bots change the world. Commands are queued with
/// <see cref="Simulation.Issue"/>, validated and applied at the start of the next tick.
/// </summary>
public abstract record Command(int FactionId);

public enum MoveMode
{
    /// <summary>Walk there ignoring enemies (retreat).</summary>
    Move,
    /// <summary>Walk there, fighting enemies met on the way.</summary>
    AttackMove,
}

/// <summary>Send the given units to a point (tile units), spreading them around it.</summary>
public sealed record MoveCommand(int FactionId, IReadOnlyList<int> UnitIds, Vector2 Target, MoveMode Mode = MoveMode.Move)
    : Command(FactionId);

/// <summary>Order the given units to hunt down one enemy unit.</summary>
public sealed record AttackUnitCommand(int FactionId, IReadOnlyList<int> UnitIds, int TargetUnitId) : Command(FactionId);

/// <summary>Pay for a unit now and queue it for production at an owned settlement.</summary>
public sealed record RecruitCommand(int FactionId, int SettlementId, string UnitTypeId) : Command(FactionId);

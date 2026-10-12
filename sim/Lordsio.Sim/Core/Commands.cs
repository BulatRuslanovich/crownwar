using System.Numerics;

namespace Lordsio.Sim.Core;

/// <summary>
/// The only way players and bots change the world. Commands are queued with
/// <see cref="Simulation.Issue"/>, validated and applied at the start of the next tick.
/// </summary>
public abstract record Command(int FactionId);

/// <summary>Move the given units to a point (tile units).</summary>
public sealed record MoveCommand(int FactionId, IReadOnlyList<int> UnitIds, Vector2 Target) : Command(FactionId);

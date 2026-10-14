using System.Numerics;
using Lordsio.Sim.World;

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

/// <summary>Order the given units to attack one enemy unit or building until it is destroyed.</summary>
public sealed record AttackCommand(int FactionId, IReadOnlyList<int> UnitIds, int TargetId) : Command(FactionId);

/// <summary>Pay for a unit now and queue it for training at an owned castle or barracks.</summary>
public sealed record RecruitCommand(int FactionId, int BuildingId, string UnitTypeId) : Command(FactionId);

/// <summary>Pay for the next level of one of the faction's buildings (an archer tower or a mine) and start upgrading it.</summary>
public sealed record UpgradeCommand(int FactionId, int BuildingId) : Command(FactionId);

/// <summary>Where a castle's or barracks' new recruits walk to once trained (null: they stay at the door).</summary>
public sealed record SetRallyCommand(int FactionId, int BuildingId, Vector2? Point) : Command(FactionId);

/// <summary>Pull down one of the faction's own buildings (not the castle) for part of what it cost.</summary>
public sealed record DemolishCommand(int FactionId, int BuildingId) : Command(FactionId);

/// <summary>Offer peace to another faction; it holds for the rules' offer time unless answered.</summary>
public sealed record ProposePeaceCommand(int FactionId, int OtherFactionId) : Command(FactionId);

/// <summary>Accept or refuse the peace <paramref name="OtherFactionId"/> offered.</summary>
public sealed record AnswerPeaceCommand(int FactionId, int OtherFactionId, bool Accept) : Command(FactionId);

/// <summary>Break the peace with another faction: war begins after the rules' warning time.</summary>
public sealed record DeclareWarCommand(int FactionId, int OtherFactionId) : Command(FactionId);

/// <summary>Pay for a building and start constructing it with its top-left corner at <paramref name="Anchor"/>.</summary>
public sealed record BuildCommand(int FactionId, string BuildingTypeId, Cell Anchor) : Command(FactionId);

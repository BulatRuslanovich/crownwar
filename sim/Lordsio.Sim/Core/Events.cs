using System.Numerics;

namespace Lordsio.Sim.Core;

/// <summary>Something that happened during a tick, for the view (messages, sounds, effects) and bots.</summary>
public abstract record SimEvent(int FactionId);

/// <summary>A mined deposit ran dry; <see cref="SimEvent.FactionId"/> owns the mine on it.</summary>
public sealed record DepositExhausted(int FactionId, int DepositId) : SimEvent(FactionId);

/// <summary>An exhausted deposit refilled; <see cref="SimEvent.FactionId"/> owns the mine on it, or -1.</summary>
public sealed record DepositRestored(int FactionId, int DepositId) : SimEvent(FactionId);

public enum RecruitError
{
    None,
    UnknownBuilding,
    NotOwner,
    NotComplete,
    CannotRecruitType,
    NotEnoughGold,
    ArmyCapReached,
}

public enum ResearchError
{
    None,
    UnknownUpgrade,
    NoCapital,
    AlreadyResearched,
    ResearchInProgress,
    MissingRequirement,
    NotEnoughGold,
}

public enum BuildError
{
    None,
    UnknownType,
    NotBuildable,
    NotEnoughGold,
    OutOfBounds,
    BlockedTerrain,
    Occupied,
    NeedsDeposit,
    OnDeposit,
    NoUnitsNearby,
    /// <summary>The faction already has as many of this building as the rules allow.</summary>
    LimitReached,
}

public sealed record RecruitRejected(int FactionId, int BuildingId, string UnitTypeId, RecruitError Reason) : SimEvent(FactionId);

public sealed record UnitRecruited(int FactionId, int BuildingId, int UnitId) : SimEvent(FactionId);

public sealed record BuildRejected(int FactionId, string BuildingTypeId, BuildError Reason) : SimEvent(FactionId);

public sealed record BuildingPlaced(int FactionId, int BuildingId) : SimEvent(FactionId);

public sealed record BuildingCompleted(int FactionId, int BuildingId) : SimEvent(FactionId);

public sealed record ResearchStarted(int FactionId, string UpgradeId) : SimEvent(FactionId);

public sealed record ResearchCompleted(int FactionId, string UpgradeId) : SimEvent(FactionId);

public sealed record ResearchRejected(int FactionId, string UpgradeId, ResearchError Reason) : SimEvent(FactionId);

/// <summary><see cref="SimEvent.FactionId"/> is the former owner.</summary>
public sealed record BuildingDestroyed(int FactionId, int BuildingId, string BuildingTypeId, int KillerFactionId, Vector2 Position, int Size)
    : SimEvent(FactionId);

/// <summary><see cref="SimEvent.FactionId"/> is the dead unit's faction; the killer may be -1 (unknown).</summary>
public sealed record UnitDied(int FactionId, int UnitId, int KillerFactionId, Vector2 Position) : SimEvent(FactionId);

/// <summary>The faction's capital fell: its army disbands and its buildings collapse.</summary>
public sealed record FactionEliminated(int FactionId) : SimEvent(FactionId);

/// <summary>The match is over. <paramref name="WinningTeam"/> is the team that won, or -1 if nobody survived.</summary>
public sealed record MatchEnded(int WinningTeam) : SimEvent(-1);

/// <summary>
/// A hit was applied (melee blow or arrow impact). Purely informational, for sounds and effects:
/// the damage is already done. <see cref="SimEvent.FactionId"/> is the attacker's faction.
/// </summary>
public sealed record AttackLanded(int FactionId, int AttackerId, int TargetId, int TargetFactionId, Vector2 Position, bool Ranged)
    : SimEvent(FactionId);

/// <summary>An archer or tower loosed an arrow from <paramref name="Position"/>.</summary>
public sealed record ProjectileFired(int FactionId, Vector2 Position) : SimEvent(FactionId);

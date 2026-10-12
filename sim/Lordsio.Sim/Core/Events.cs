namespace Lordsio.Sim.Core;

/// <summary>Something that happened during a tick, for the view (messages, sounds, effects) and bots.</summary>
public abstract record SimEvent(int FactionId);

public enum RecruitError
{
    None,
    UnknownBuilding,
    NotOwner,
    NotComplete,
    CannotRecruitType,
    NotEnoughGold,
    ArmyCapReached,
    QueueFull,
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
}

public sealed record RecruitRejected(int FactionId, int BuildingId, string UnitTypeId, RecruitError Reason) : SimEvent(FactionId);

public sealed record UnitRecruited(int FactionId, int BuildingId, int UnitId) : SimEvent(FactionId);

public sealed record BuildRejected(int FactionId, string BuildingTypeId, BuildError Reason) : SimEvent(FactionId);

public sealed record BuildingPlaced(int FactionId, int BuildingId) : SimEvent(FactionId);

public sealed record BuildingCompleted(int FactionId, int BuildingId) : SimEvent(FactionId);

/// <summary><see cref="SimEvent.FactionId"/> is the former owner.</summary>
public sealed record BuildingDestroyed(int FactionId, int BuildingId, string BuildingTypeId, int KillerFactionId) : SimEvent(FactionId);

/// <summary><see cref="SimEvent.FactionId"/> is the dead unit's faction; the killer may be -1 (unknown).</summary>
public sealed record UnitDied(int FactionId, int UnitId, int KillerFactionId) : SimEvent(FactionId);

/// <summary>The faction's capital fell: its army disbands and its buildings collapse.</summary>
public sealed record FactionEliminated(int FactionId) : SimEvent(FactionId);

/// <summary><see cref="SimEvent.FactionId"/> is the winner, or -1 if nobody survived.</summary>
public sealed record MatchEnded(int FactionId) : SimEvent(FactionId);

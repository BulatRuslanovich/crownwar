namespace Lordsio.Sim.Core;

/// <summary>Something that happened during a tick, for the view (messages, sounds, effects) and bots.</summary>
public abstract record SimEvent(int FactionId);

public enum RecruitError
{
    None,
    UnknownSettlement,
    NotOwner,
    CannotRecruitType,
    NotEnoughGold,
    ArmyCapReached,
    QueueFull,
}

public sealed record RecruitRejected(int FactionId, int SettlementId, string UnitTypeId, RecruitError Reason) : SimEvent(FactionId);

public sealed record UnitRecruited(int FactionId, int SettlementId, int UnitId) : SimEvent(FactionId);

/// <summary><see cref="SimEvent.FactionId"/> is the dead unit's faction; the killer may be -1 (neutral or unknown).</summary>
public sealed record UnitDied(int FactionId, int UnitId, int KillerFactionId) : SimEvent(FactionId);

/// <summary><see cref="SimEvent.FactionId"/> is the new owner.</summary>
public sealed record SettlementCaptured(int FactionId, int SettlementId, int PreviousOwnerId) : SimEvent(FactionId);

/// <summary>The faction lost its last capital: its army disbands and its settlements turn neutral.</summary>
public sealed record FactionEliminated(int FactionId) : SimEvent(FactionId);

/// <summary><see cref="SimEvent.FactionId"/> is the winner, or -1 if nobody survived.</summary>
public sealed record MatchEnded(int FactionId) : SimEvent(FactionId);

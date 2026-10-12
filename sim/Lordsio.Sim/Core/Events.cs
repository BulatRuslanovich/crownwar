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

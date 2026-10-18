using System.Numerics;

namespace Warcrown.Sim.Core;

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
    /// <summary>Not yet unlocked: the owner has too few finished barracks for this kind.</summary>
    Locked,
    NotEnoughGold,
    ArmyCapReached,
}

public enum DoctrineError
{
    None,
    UnknownDoctrine,
    NotOwner,
    /// <summary>The faction already committed to a doctrine: the choice is for the whole match.</summary>
    AlreadyChosen,
    /// <summary>Fewer finished barracks than the rules' DoctrineBarracks.</summary>
    NeedsBarracks,
}

public enum UpgradeError
{
    None,
    UnknownBuilding,
    NotOwner,
    NotComplete,
    /// <summary>The building is at its highest level, or its kind has no levels at all.</summary>
    MaxTier,
    InProgress,
    NotEnoughGold,
}

public enum DemolishError
{
    None,
    UnknownBuilding,
    NotOwner,
    /// <summary>The castle cannot be pulled down.</summary>
    Capital,
    /// <summary>A fort is held, not pulled down.</summary>
    Fort,
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

/// <summary>Nobody stood near the place: <paramref name="BuilderId"/> walks over to put up the (already paid) building.</summary>
public sealed record BuildPlanned(int FactionId, int PlanId, string BuildingTypeId, int BuilderId) : SimEvent(FactionId);

/// <summary>A planned building was given up (builder lost, sent elsewhere or stuck, or the place taken); the gold came back.</summary>
public sealed record BuildPlanCancelled(int FactionId, int PlanId, string BuildingTypeId, int Refund, BuildError Reason) : SimEvent(FactionId);

public sealed record BuildingCompleted(int FactionId, int BuildingId) : SimEvent(FactionId);

public sealed record BuildingUpgradeStarted(int FactionId, int BuildingId) : SimEvent(FactionId);

/// <summary>A building reached the next level (<paramref name="Tier"/>).</summary>
public sealed record BuildingUpgraded(int FactionId, int BuildingId, int Tier) : SimEvent(FactionId);

public sealed record UpgradeRejected(int FactionId, int BuildingId, UpgradeError Reason) : SimEvent(FactionId);

/// <summary>The owner pulled a building down and got <paramref name="Refund"/> gold back (part of its cost, plus queued recruits).</summary>
public sealed record BuildingDemolished(int FactionId, int BuildingId, string BuildingTypeId, int Refund, Vector2 Position, int Size)
    : SimEvent(FactionId);

public sealed record DemolishRejected(int FactionId, int BuildingId, DemolishError Reason) : SimEvent(FactionId);

/// <summary><paramref name="FactionId"/> destroyed a building of <paramref name="VictimFactionId"/> and took <paramref name="Amount"/> gold from it.</summary>
public sealed record GoldLooted(int FactionId, int VictimFactionId, int Amount, Vector2 Position) : SimEvent(FactionId);

/// <summary><see cref="SimEvent.FactionId"/> is the former owner.</summary>
public sealed record BuildingDestroyed(int FactionId, int BuildingId, string BuildingTypeId, int KillerFactionId, Vector2 Position, int Size)
    : SimEvent(FactionId);

/// <summary><see cref="SimEvent.FactionId"/> is the dead unit's faction; the killer may be -1 (unknown).</summary>
public sealed record UnitDied(int FactionId, int UnitId, int KillerFactionId, Vector2 Position, string UnitTypeId = "") : SimEvent(FactionId);

/// <summary>A unit's kills earned it veteran rank <paramref name="Rank"/> (1 and up).</summary>
public sealed record UnitPromoted(int FactionId, int UnitId, int Rank, Vector2 Position) : SimEvent(FactionId);

/// <summary>The faction's capital fell: its army disbands and its buildings collapse.</summary>
public sealed record FactionEliminated(int FactionId) : SimEvent(FactionId);

/// <summary>
/// A fort changed hands: <see cref="SimEvent.FactionId"/> took it from <paramref name="FromFactionId"/> (-1: it was
/// neutral); <see cref="SimEvent.FactionId"/> is -1 when the fort went back to nobody, its owner eliminated.
/// </summary>
public sealed record FortCaptured(int FactionId, int FromFactionId, int BuildingId, Vector2 Position) : SimEvent(FactionId);

public enum DiplomacyError
{
    None,
    UnknownFaction,
    Self,
    /// <summary>The same team from the lobby: allies for good.</summary>
    Allies,
    AlreadyAtPeace,
    AlreadyAtWar,
    AlreadyOffered,
    /// <summary>Nothing to answer: the offer ran out or was never made.</summary>
    NoOffer,
    /// <summary>War is already declared and on its way.</summary>
    WarComing,
}

/// <summary><see cref="SimEvent.FactionId"/> offered peace to <paramref name="OtherFactionId"/>, who has <paramref name="Seconds"/> to accept.</summary>
public sealed record PeaceOffered(int FactionId, int OtherFactionId, float Seconds) : SimEvent(FactionId);

/// <summary><see cref="SimEvent.FactionId"/> did not take the peace <paramref name="OtherFactionId"/> offered (refused, or let it run out).</summary>
public sealed record PeaceRefused(int FactionId, int OtherFactionId, bool Expired) : SimEvent(FactionId);

/// <summary>A treaty between <see cref="SimEvent.FactionId"/> (who offered) and <paramref name="OtherFactionId"/>: their fighting stops.</summary>
public sealed record PeaceMade(int FactionId, int OtherFactionId) : SimEvent(FactionId);

/// <summary><see cref="SimEvent.FactionId"/> declared war on <paramref name="OtherFactionId"/>; it begins in <paramref name="Seconds"/>.</summary>
public sealed record WarDeclared(int FactionId, int OtherFactionId, float Seconds) : SimEvent(FactionId);

/// <summary>The war <see cref="SimEvent.FactionId"/> declared on <paramref name="OtherFactionId"/> has begun.</summary>
public sealed record WarBegan(int FactionId, int OtherFactionId) : SimEvent(FactionId);

public sealed record DiplomacyRejected(int FactionId, int OtherFactionId, DiplomacyError Reason) : SimEvent(FactionId);

/// <summary>A monk (<paramref name="HealerId"/>) mended <paramref name="Amount"/> health of <paramref name="TargetId"/> at <paramref name="Position"/>.</summary>
public sealed record UnitHealed(int FactionId, int HealerId, int TargetId, int Amount, Vector2 Position) : SimEvent(FactionId);

/// <summary>Defense: wave <paramref name="Wave"/> comes in <paramref name="Seconds"/>, from these points by the edge of the map.</summary>
public sealed record WaveIncoming(int Wave, IReadOnlyList<Vector2> From, float Seconds) : SimEvent(-1);

/// <summary>Defense: wave <paramref name="Wave"/> has come, <paramref name="Units"/> strong.</summary>
public sealed record WaveStarted(int Wave, int Units, IReadOnlyList<Vector2> From) : SimEvent(-1);

/// <summary>Defense: wave <paramref name="Wave"/> is wiped out; every player got <paramref name="Reward"/> gold.</summary>
public sealed record WaveCleared(int Wave, int Reward) : SimEvent(-1);

/// <summary>The match is over. <paramref name="WinningTeam"/> is the team that won, or -1 if nobody survived.</summary>
public sealed record MatchEnded(int WinningTeam) : SimEvent(-1);

/// <summary>
/// A hit was applied (melee blow or arrow impact). Purely informational, for sounds and effects:
/// the damage is already done. <see cref="SimEvent.FactionId"/> is the attacker's faction.
/// </summary>
/// <param name="Splash">One of the units caught by a catapult's stone (the stone's own event is <see cref="SplashLanded"/>).</param>
public sealed record AttackLanded(int FactionId, int AttackerId, int TargetId, int TargetFactionId, Vector2 Position, bool Ranged,
    bool Splash = false) : SimEvent(FactionId);

/// <summary>
/// A catapult's stone came down at <paramref name="Position"/>, hurting enemies within <paramref name="Radius"/> tiles
/// (or a dragon's fire burst there, <paramref name="Fire"/>).
/// </summary>
public sealed record SplashLanded(int FactionId, int SourceId, Vector2 Position, float Radius, bool Fire = false) : SimEvent(FactionId);

/// <summary>
/// An archer or tower loosed an arrow (or a catapult a stone, <paramref name="Splash"/>; or a dragon breathed fire,
/// <paramref name="Fire"/>) from <paramref name="Position"/>.
/// </summary>
public sealed record ProjectileFired(int FactionId, Vector2 Position, bool Splash = false, bool Fire = false) : SimEvent(FactionId);

/// <summary>The faction committed to <paramref name="DoctrineId"/>: its units open and every new recruit follows it.</summary>
public sealed record DoctrineChosen(int FactionId, string DoctrineId) : SimEvent(FactionId);

public sealed record DoctrineRejected(int FactionId, string DoctrineId, DoctrineError Reason) : SimEvent(FactionId);

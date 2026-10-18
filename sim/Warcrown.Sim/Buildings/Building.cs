using System.Numerics;
using Warcrown.Sim.Core;
using Warcrown.Sim.Units;
using Warcrown.Sim.World;

namespace Warcrown.Sim.Buildings;

/// <summary>
/// Ranged attack of a defensive building. An archer tower hits its target; a catapult (<paramref name="SplashRadius"/>
/// above zero) hits every enemy unit on the ground within that many tiles of where its stone lands. Only an attack that
/// <paramref name="HitsAir"/> (arrows) can reach flying units.
/// </summary>
public sealed record BuildingAttack(int Damage, float Range, float Cooldown, float ProjectileSpeed, float SplashRadius = 0f,
    bool HitsAir = false)
{
    public bool IsSplash => SplashRadius > 0f;
}

/// <summary>
/// Static stats of a building kind. Size is the side of its square footprint in tiles.
/// Income is gold per second; a mine instead earns the yield of the deposit it stands on.
/// A faction whose capital is destroyed is eliminated. <paramref name="MaxCount"/> caps how many of the kind one
/// faction may have at once, sites included (0 = no limit).
/// A kind may have upgrade levels: each level is a type of its own (same id, <paramref name="Tier"/> 1, 2, 3…) that
/// knows the <paramref name="NextTier"/> and what it costs to get there; a mine's level adds <paramref name="YieldBonus"/>
/// gold per second on top of its deposit.
/// </summary>
public sealed record BuildingType(
    string Id,
    string Name,
    int Size,
    int Cost,
    float BuildTime,
    int MaxHealth,
    float RangedArmor,
    int Income,
    int ArmyCap,
    IReadOnlyList<string> Recruits,
    bool IsCapital = false,
    bool IsMine = false,
    BuildingAttack? Attack = null,
    float SightRange = 0f,
    int MaxCount = 0,
    int Tier = 1,
    BuildingType? NextTier = null,
    int UpgradeCost = 0,
    float UpgradeTime = 0f,
    int YieldBonus = 0,
    bool UnlocksUnits = false,
    bool IsCitadel = false,
    int CostGrowthPercent = 100,
    bool IsCapturable = false,
    float LightRadius = 0f)
{
    // IsCapturable: a fort. It stands on the map from the start, held by a neutral guard; whoever brings it down takes
    // it over instead of razing it (see Simulation.Capture), and it pays its owner Income like any building.

    // LightRadius: tiles round the building lit at night (a watchtower's beacon): the owner's and allies' towers there
    // shoot as far as by day (see Simulation.AttackRangeOf).

    // CostGrowthPercent: every one the owner already has (or has ordered) makes the next this much dearer, in
    // percent of the previous price (125: 150, 190, 230, 290...). 100 keeps the price flat.

    // UnlocksUnits: each finished building of such a kind (a barracks) opens one more step of the units the owner can
    // train (see UnitType.BarracksRequired).

    /// <summary>
    /// Capitals are placed at match start and cannot be built by players (except a new outpost in the Defense mode,
    /// see <c>Simulation.CanBuild</c>); nor can the Defense mode's citadel, nor forts (they are only ever captured).
    /// </summary>
    public bool IsBuildable => !IsCapital && !IsCitadel && !IsCapturable;

    /// <summary>The highest level this kind can reach.</summary>
    public int MaxTier => NextTier?.MaxTier ?? Tier;

    /// <summary>
    /// How far the building sees (tiles from its center), for the fog of war: <see cref="SightRange"/> if set,
    /// otherwise a little beyond its walls, and always at least as far as it shoots.
    /// </summary>
    public float EffectiveSight => MathF.Max(SightRange > 0f ? SightRange : WallSight, Attack is { } a ? a.Range + 1f : 0f);

    /// <summary>Sight just beyond the walls: what a building without its own sight range (or still being built) sees.</summary>
    public float WallSight => Size / 2f + 3f;
}

public sealed class ProductionItem
{
    internal ProductionItem(UnitType type, int totalTicks)
    {
        Type = type;
        TotalTicks = totalTicks;
        TicksLeft = totalTicks;
    }

    public UnitType Type { get; }
    public int TotalTicks { get; }
    public int TicksLeft { get; internal set; }
    public float Progress => TotalTicks == 0 ? 1f : 1f - (float)TicksLeft / TotalTicks;
}

public sealed class Building : Entity
{
    /// <summary>A construction site starts with this share of its health and gains the rest while building.</summary>
    private const int StartingHealthPercent = 10;

    private readonly List<ProductionItem> _queue = new();
    private readonly int _constructionTicks;

    internal Building(int id, BuildingType type, int factionId, Cell anchor, int constructionTicks, GoldDeposit? deposit)
        : base(id, factionId, CenterOf(anchor, type.Size), constructionTicks > 0 ? Math.Max(1, type.MaxHealth * StartingHealthPercent / 100) : type.MaxHealth)
    {
        Type = type;
        Anchor = anchor;
        Deposit = deposit;
        _constructionTicks = constructionTicks;
        ConstructionTicksLeft = constructionTicks;
    }

    /// <summary>The building's kind at its current level: replaced by the next level when an upgrade completes.</summary>
    public BuildingType Type { get; private set; }

    /// <summary>Top-left cell of the footprint.</summary>
    public Cell Anchor { get; }

    /// <summary>The deposit a mine works, otherwise null.</summary>
    public GoldDeposit? Deposit { get; }

    public int ConstructionTicksLeft { get; private set; }
    public bool IsComplete => ConstructionTicksLeft == 0;
    public float ConstructionProgress => _constructionTicks == 0 ? 1f : 1f - (float)ConstructionTicksLeft / _constructionTicks;

    /// <summary>
    /// Gold per second this building earns: once complete, and for a mine while its deposit is not exhausted
    /// (the deposit's yield plus the mine's level bonus; only the yield drains the deposit).
    /// </summary>
    public int Income => !IsComplete ? 0 : Type.IsMine ? Deposit is { IsExhausted: false } d ? d.Yield + Type.YieldBonus : 0 : Type.Income;

    /// <summary>Gold its owner has put into it: the price, plus every upgrade paid for (the base of loot and refunds).</summary>
    public int Invested { get; internal set; }

    /// <summary>Ticks until the upgrade under way completes; 0 when none.</summary>
    public int UpgradeTicksLeft { get; private set; }
    private int _upgradeTicks;

    public bool IsUpgrading => UpgradeTicksLeft > 0;
    public float UpgradeProgress => _upgradeTicks == 0 || !IsUpgrading ? 0f : 1f - (float)UpgradeTicksLeft / _upgradeTicks;

    public override int MaxHealth => Type.MaxHealth;
    public override float RangedArmor => Type.RangedArmor;

    /// <summary>How far the building sees now: a construction site only looks around itself.</summary>
    public float SightRange => IsComplete ? Type.EffectiveSight : Type.WallSight;

    /// <summary>Units being trained; only the first one makes progress. Any length: queued units count against the army cap.</summary>
    public IReadOnlyList<ProductionItem> Queue => _queue;

    /// <summary>Where new recruits walk to once trained; null to stay at the door.</summary>
    public Vector2? RallyPoint { get; internal set; }

    /// <summary>Enemy unit a tower is shooting at.</summary>
    public Unit? Target { get; internal set; }
    internal int AttackCooldownTicks { get; set; }

    public IEnumerable<Cell> Footprint => FootprintOf(Anchor, Type.Size);

    public override float SurfaceDistance(Vector2 point)
    {
        float half = Type.Size / 2f;
        float dx = MathF.Max(0f, MathF.Abs(point.X - Position.X) - half);
        float dy = MathF.Max(0f, MathF.Abs(point.Y - Position.Y) - half);
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    public static Vector2 CenterOf(Cell anchor, int size) => anchor.CenterOfSquare(size);

    /// <summary>Whether <paramref name="cell"/> lies in the footprint.</summary>
    public bool Covers(Cell cell) =>
        cell.X >= Anchor.X && cell.Y >= Anchor.Y && cell.X < Anchor.X + Type.Size && cell.Y < Anchor.Y + Type.Size;

    public static IEnumerable<Cell> FootprintOf(Cell anchor, int size)
    {
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                yield return new Cell(anchor.X + x, anchor.Y + y);
    }

    /// <summary>Advances construction by one tick; health grows to full along with progress.</summary>
    /// <returns>True on the tick the building is finished.</returns>
    internal bool AdvanceConstruction()
    {
        if (ConstructionTicksLeft == 0)
            return false;
        // Distribute the missing 90% of health exactly over the build ticks (integer maths, no drift).
        int done = _constructionTicks - ConstructionTicksLeft;
        int growth = MaxHealth - MaxHealth * StartingHealthPercent / 100;
        Health += growth * (done + 1) / _constructionTicks - growth * done / _constructionTicks;
        Health = Math.Min(Health, MaxHealth);
        ConstructionTicksLeft--;
        return ConstructionTicksLeft == 0;
    }

    internal void StartUpgrade(int ticks)
    {
        _upgradeTicks = Math.Max(1, ticks);
        UpgradeTicksLeft = _upgradeTicks;
    }

    /// <summary>
    /// One tick of the upgrade under way. On the last one the building becomes the next level; it keeps the damage
    /// it had taken, so the extra health of the new level is added on top.
    /// </summary>
    /// <returns>True on the tick the upgrade completes.</returns>
    internal bool AdvanceUpgrade()
    {
        if (UpgradeTicksLeft == 0 || --UpgradeTicksLeft > 0)
            return false;
        var next = Type.NextTier!;
        int gained = next.MaxHealth - Type.MaxHealth;
        Type = next;
        Health = Math.Clamp(Health + gained, 1, MaxHealth);
        return true;
    }

    internal void Enqueue(ProductionItem item) => _queue.Add(item);

    /// <summary>
    /// Advances production by one tick: the first <paramref name="slots"/> recruits in the queue train side by side.
    /// Those that finish are added to <paramref name="finished"/>, first in line first.
    /// </summary>
    internal void AdvanceProduction(int slots, List<UnitType> finished)
    {
        int active = Math.Min(slots, _queue.Count);
        for (int i = 0; i < active; i++)
            _queue[i].TicksLeft--;
        for (int i = 0; i < active; i++)
            if (_queue[i].TicksLeft <= 0)
                finished.Add(_queue[i].Type);
        _queue.RemoveAll(item => item.TicksLeft <= 0);
    }
}

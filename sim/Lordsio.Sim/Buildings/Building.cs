using System.Numerics;
using Lordsio.Sim.Core;
using Lordsio.Sim.Units;
using Lordsio.Sim.World;

namespace Lordsio.Sim.Buildings;

/// <summary>Ranged attack of a defensive building (archer tower).</summary>
public sealed record BuildingAttack(int Damage, float Range, float Cooldown, float ProjectileSpeed);

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
    int YieldBonus = 0)
{
    /// <summary>Capitals are placed at match start and cannot be built by players.</summary>
    public bool IsBuildable => !IsCapital;

    /// <summary>The highest level this kind can reach.</summary>
    public int MaxTier => NextTier?.MaxTier ?? Tier;

    /// <summary>
    /// How far the building sees (tiles from its center), for the fog of war: <see cref="SightRange"/> if set,
    /// otherwise a little beyond its walls, and always at least as far as it shoots.
    /// </summary>
    public float EffectiveSight => MathF.Max(SightRange > 0f ? SightRange : Size / 2f + 3f, Attack is { } a ? a.Range + 1f : 0f);
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

    /// <summary>Ticks until the upgrade under way completes; 0 when none.</summary>
    public int UpgradeTicksLeft { get; private set; }
    private int _upgradeTicks;

    public bool IsUpgrading => UpgradeTicksLeft > 0;
    public float UpgradeProgress => _upgradeTicks == 0 || !IsUpgrading ? 0f : 1f - (float)UpgradeTicksLeft / _upgradeTicks;

    public override int MaxHealth => Type.MaxHealth;
    public override float RangedArmor => Type.RangedArmor;

    /// <summary>How far the building sees now: a construction site only looks around itself.</summary>
    public float SightRange => IsComplete ? Type.EffectiveSight : Type.Size / 2f + 3f;

    /// <summary>Units being trained; only the first one makes progress. Any length: queued units count against the army cap.</summary>
    public IReadOnlyList<ProductionItem> Queue => _queue;

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

    public static Vector2 CenterOf(Cell anchor, int size) => new(anchor.X + size / 2f, anchor.Y + size / 2f);

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

    /// <summary>Advances production by one tick and returns the unit type that finished, if any.</summary>
    internal UnitType? AdvanceProduction()
    {
        if (_queue.Count == 0)
            return null;
        var current = _queue[0];
        if (--current.TicksLeft > 0)
            return null;
        _queue.RemoveAt(0);
        return current.Type;
    }
}

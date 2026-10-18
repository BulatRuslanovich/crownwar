namespace Warcrown.Sim.Units;

/// <summary>
/// Static stats of a unit kind. Distances are in tiles, times in seconds, speeds in tiles per second.
/// <list type="bullet">
/// <item><see cref="AttackRange"/> is measured edge to edge between the two bodies.</item>
/// <item><see cref="ProjectileSpeed"/> of 0 means a melee attack that lands instantly.</item>
/// <item><see cref="RangedArmor"/> is the fraction of projectile damage ignored (0..1).</item>
/// <item><see cref="Upkeep"/> is gold per minute the owner pays while the unit lives.</item>
/// <item>A <see cref="IsSiege"/> unit (a ram, a trebuchet) only ever attacks buildings; against them every blow deals
/// <see cref="Damage"/> × <see cref="BuildingDamageMultiplier"/>.</item>
/// <item><see cref="BonusAgainst"/> multiplies the damage dealt to units of the given types (spearmen against horses).</item>
/// <item><see cref="ArmorPiercing"/> is the share of the target's ranged armour a missile ignores (crossbow bolts).</item>
/// <item>A unit with <see cref="Heal"/> (a monk) does not fight: it mends wounded friends near it.</item>
/// <item><see cref="BarracksRequired"/>: how many finished barracks its owner needs before it can be trained.</item>
/// <item>A <see cref="IsFlying"/> unit (a griffin, a dragon) flies straight over water, mountains and buildings; only
/// those that <see cref="HitsAir"/> (archers, crossbowmen) and shooting buildings with an anti-air attack can hurt it.</item>
/// <item>A missile with a <see cref="SplashRadius"/> (a dragon's fire) also burns every enemy on the ground that close to its target.</item>
/// <item>A kind with a <see cref="Doctrine"/> is only open to factions that chose that doctrine (see <see cref="Units.Doctrine"/>).</item>
/// </list>
/// </summary>
public sealed record UnitType(
    string Id,
    string Name,
    float Speed,
    float Radius,
    int Cost,
    float BuildTime,
    int MaxHealth,
    int Damage,
    float AttackRange,
    float AttackCooldown,
    float SightRange,
    float ProjectileSpeed = 0f,
    float RangedArmor = 0f,
    int Upkeep = 0,
    bool IsSiege = false,
    float BuildingDamageMultiplier = 1f,
    IReadOnlyDictionary<string, float>? BonusAgainst = null,
    float ArmorPiercing = 0f,
    HealAbility? Heal = null,
    int BarracksRequired = 0,
    bool IsFlying = false,
    bool HitsAir = false,
    float SplashRadius = 0f,
    string? Doctrine = null)
{
    public bool IsRanged => ProjectileSpeed > 0f;

    public bool IsHealer => Heal != null;

    /// <summary>What one blow does to <paramref name="target"/>.</summary>
    public int DamageAgainst(Core.Entity target) => target switch
    {
        Buildings.Building => (int)MathF.Round(Damage * BuildingDamageMultiplier),
        Unit unit when BonusAgainst != null && BonusAgainst.TryGetValue(unit.Type.Id, out float bonus) => (int)MathF.Round(Damage * bonus),
        _ => Damage,
    };
}

/// <summary>Mending: <paramref name="Amount"/> health to a wounded friend within <paramref name="Range"/> tiles (edge to edge),
/// once every <paramref name="Cooldown"/> seconds.</summary>
public sealed record HealAbility(int Amount, float Range, float Cooldown);

public sealed class UnitCatalog
{
    private readonly Dictionary<string, UnitType> _types;

    public UnitCatalog(IEnumerable<UnitType> types)
    {
        _types = new Dictionary<string, UnitType>();
        foreach (var type in types)
        {
            Validate(type);
            if (!_types.TryAdd(type.Id, type))
                throw new ArgumentException($"Duplicate unit type id '{type.Id}'.");
        }
    }

    public IEnumerable<UnitType> All => _types.Values;

    public bool Contains(string id) => _types.ContainsKey(id);

    public UnitType Get(string id) =>
        _types.TryGetValue(id, out var type) ? type : throw new KeyNotFoundException($"Unknown unit type '{id}'.");

    private static void Validate(UnitType t)
    {
        void Require(bool ok, string what)
        {
            if (!ok)
                throw new ArgumentException($"Unit type '{t.Id}': {what}.");
        }

        Require(t.Speed > 0f, "speed must be > 0");
        Require(t.Radius > 0f && t.Radius < 0.5f, "radius must be in (0, 0.5)");
        Require(t.Cost >= 0 && t.BuildTime >= 0f, "cost and build time cannot be negative");
        Require(t.MaxHealth > 0 && t.Damage >= 0, "health must be > 0 and damage >= 0");
        Require(t.AttackRange >= 0f && t.AttackCooldown > 0f, "attack range >= 0 and cooldown > 0");
        Require(t.SightRange >= t.AttackRange, "sight range must cover the attack range");
        Require(t.ProjectileSpeed >= 0f && t.RangedArmor is >= 0f and <= 1f, "projectile speed >= 0 and ranged armor in [0, 1]");
        Require(t.ArmorPiercing is >= 0f and <= 1f, "armor piercing in [0, 1]");
        Require(t.BarracksRequired >= 0, "barracks required cannot be negative");
        Require(t.SplashRadius >= 0f && (t.SplashRadius == 0f || t.IsRanged), "splash must be >= 0, and only a missile splashes");
        Require(t.BonusAgainst == null || t.BonusAgainst.Values.All(b => b > 0f), "damage bonuses must be > 0");
        Require(t.Heal is not { } heal || heal.Amount > 0 && heal.Range >= 0f && heal.Cooldown > 0f && t.SightRange >= heal.Range,
            "heal needs an amount > 0, range >= 0 within sight, cooldown > 0");
    }
}

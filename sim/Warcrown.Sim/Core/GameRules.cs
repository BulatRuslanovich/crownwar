using System.Text.Json;
using Warcrown.Sim.Buildings;
using Warcrown.Sim.Units;

namespace Warcrown.Sim.Core;

public sealed record StartingUnits(string UnitTypeId, int Count);

/// <summary>All tunable game data, loaded from Data/rules.json.</summary>
public sealed class GameRules
{
    /// <summary>How long an offer of peace stands, and the warning before a war, unless the rules say otherwise.</summary>
    public const float DefaultPeaceOfferSeconds = 30f, DefaultWarWarningSeconds = 30f;

    private readonly Dictionary<string, BuildingType> _buildingTypes = new();
    /// <summary>The same types in the order of the rules file, so that every lookup over them is deterministic.</summary>
    private readonly List<BuildingType> _buildingTypeOrder = new();

    public GameRules(int startingGold, float buildRadius, IReadOnlyList<StartingUnits> startingUnits,
        UnitCatalog units, IEnumerable<BuildingType> buildingTypes,
        float depositReserveSeconds = 0f, float depositRegenSeconds = 0f, int upkeepAllowance = 0,
        int goldCap = 0, int lootPercent = 0, int demolishRefundPercent = 0,
        float daySeconds = 0f, float nightSeconds = 0f, float nightSightFactor = 1f,
        float peaceOfferSeconds = DefaultPeaceOfferSeconds, float warWarningSeconds = DefaultWarWarningSeconds,
        DefenseRules? defense = null, VeterancyRules? veterancy = null,
        int upkeepSurchargeFrom = 0, int upkeepSurchargePercent = 0, int mineRaidPercent = 0)
    {
        if (startingGold < 0)
            throw new ArgumentOutOfRangeException(nameof(startingGold));
        if (buildRadius <= 0f)
            throw new ArgumentOutOfRangeException(nameof(buildRadius));
        if (depositReserveSeconds < 0f || depositRegenSeconds < 0f)
            throw new ArgumentOutOfRangeException(nameof(depositReserveSeconds), "Deposit timings cannot be negative.");
        if (upkeepAllowance < 0)
            throw new ArgumentOutOfRangeException(nameof(upkeepAllowance));
        if (goldCap < 0)
            throw new ArgumentOutOfRangeException(nameof(goldCap));
        if (lootPercent is < 0 or > 100 || demolishRefundPercent is < 0 or > 100 || mineRaidPercent is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(lootPercent), "Loot, refund and mine raids are percentages.");
        if (upkeepSurchargeFrom < 0 || upkeepSurchargePercent < 0)
            throw new ArgumentOutOfRangeException(nameof(upkeepSurchargePercent), "The upkeep surcharge cannot be negative.");
        if (daySeconds < 0f || nightSeconds < 0f || nightSightFactor is <= 0f or > 1f)
            throw new ArgumentOutOfRangeException(nameof(nightSeconds), "Day and night last a non-negative time; night sight is a factor in (0, 1].");
        if (peaceOfferSeconds <= 0f || warWarningSeconds < 0f)
            throw new ArgumentOutOfRangeException(nameof(warWarningSeconds), "An offer of peace must stand a while; the war warning cannot be negative.");
        Defense = defense ?? DefenseRules.Default;
        if (Defense.FirstWaveSeconds <= Defense.WarningSeconds || Defense.WaveIntervalSeconds <= Defense.WarningSeconds
            || Defense.BaseBudget <= 0 || Defense.Growth < 1f || Defense.MaxUnitsPerWave <= 0)
            throw new ArgumentOutOfRangeException(nameof(defense), "Waves must come after their warning, cost something and not shrink.");
        Veterancy = veterancy ?? VeterancyRules.None;
        Veterancy.Validate();
        PeaceOfferSeconds = peaceOfferSeconds;
        WarWarningSeconds = warWarningSeconds;
        StartingGold = startingGold;
        GoldCap = goldCap;
        LootPercent = lootPercent;
        MineRaidPercent = mineRaidPercent;
        UpkeepSurchargeFrom = upkeepSurchargeFrom;
        UpkeepSurchargePercent = upkeepSurchargePercent;
        DemolishRefundPercent = demolishRefundPercent;
        DaySeconds = daySeconds;
        NightSeconds = nightSeconds;
        NightSightFactor = nightSightFactor;
        DepositReserveSeconds = depositReserveSeconds;
        DepositRegenSeconds = depositRegenSeconds;
        UpkeepAllowance = upkeepAllowance;
        BuildRadius = buildRadius;
        StartingUnits = startingUnits;
        Units = units;

        foreach (var start in startingUnits)
        {
            if (!units.Contains(start.UnitTypeId))
                throw new ArgumentException($"Starting units refer to unknown unit type '{start.UnitTypeId}'.");
        }

        foreach (var type in buildingTypes)
        {
            foreach (string unitId in type.Recruits)
            {
                if (!units.Contains(unitId))
                    throw new ArgumentException($"Building type '{type.Id}' recruits unknown unit type '{unitId}'.");
            }
            if (type.Size < 1 || type.MaxHealth <= 0 || type.Cost < 0 || type.BuildTime < 0f)
                throw new ArgumentException($"Building type '{type.Id}': size and health must be positive, cost and build time non-negative.");
            if (type.RangedArmor is < 0f or > 1f)
                throw new ArgumentException($"Building type '{type.Id}': ranged armor must be in [0, 1].");
            if (type.CostGrowthPercent < 100)
                throw new ArgumentException($"Building type '{type.Id}': cost growth cannot be below 1.");
            for (var level = type.NextTier; level != null; level = level.NextTier)
            {
                if (level.Id != type.Id || level.MaxHealth <= 0 || level.Size != type.Size)
                    throw new ArgumentException($"Building type '{type.Id}': every level keeps the id and size and has health.");
            }
            for (var level = type; level.NextTier != null; level = level.NextTier)
            {
                if (level.UpgradeCost < 0 || level.UpgradeTime < 0f)
                    throw new ArgumentException($"Building type '{type.Id}': upgrade cost and time cannot be negative.");
            }
            if (type.IsMine && type.Size != Warcrown.Sim.World.GoldDeposit.Size)
                throw new ArgumentException($"Mine type '{type.Id}' must match the deposit size {Warcrown.Sim.World.GoldDeposit.Size}.");
            if (!_buildingTypes.TryAdd(type.Id, type))
                throw new ArgumentException($"Duplicate building type id '{type.Id}'.");
            _buildingTypeOrder.Add(type);
        }
        Capital = _buildingTypeOrder.SingleOrDefault(t => t.IsCapital)
                  ?? throw new ArgumentException("Exactly one building type must be the capital.");
        Citadel = _buildingTypeOrder.SingleOrDefault(t => t.IsCitadel);
    }

    public int StartingGold { get; }

    /// <summary>
    /// Seconds of mining a deposit holds (its reserve is yield × this); 0 means deposits never run out.
    /// </summary>
    public float DepositReserveSeconds { get; }

    /// <summary>Seconds an exhausted deposit needs to refill completely; its mine earns nothing meanwhile.</summary>
    public float DepositRegenSeconds { get; }

    /// <summary>Most gold a faction can hold; income beyond it is lost (0 = no limit).</summary>
    public int GoldCap { get; }

    /// <summary>Army upkeep (gold per minute) each faction gets for free: a small garrison costs nothing.</summary>
    public int UpkeepAllowance { get; }

    /// <summary>A building may only be placed this close (tiles, to its center) to one of the builder's units.</summary>
    public float BuildRadius { get; }

    /// <summary>Share (percent) of what a building cost its owner, with its upgrades, that the faction destroying it takes.</summary>
    public int LootPercent { get; }

    /// <summary>Share (percent) of what is left in a deposit that whoever destroys the enemy mine on it carries off.</summary>
    public int MineRaidPercent { get; }

    /// <summary>
    /// A big army costs more to keep: past <see cref="UpkeepSurchargeFrom"/> living units, the upkeep of the whole army
    /// grows by <see cref="UpkeepSurchargePercent"/> percent per unit beyond. The leader pays for his lead.
    /// </summary>
    public int UpkeepSurchargeFrom { get; }
    public int UpkeepSurchargePercent { get; }

    /// <summary>Share (percent) of what a building cost that its owner gets back for pulling it down.</summary>
    public int DemolishRefundPercent { get; }

    /// <summary>Seconds of day, then of night, repeating from the start of the match; no night if 0.</summary>
    public float DaySeconds { get; }
    public float NightSeconds { get; }

    /// <summary>At night units see this share of their sight range (buildings see as far as by day).</summary>
    public float NightSightFactor { get; }

    /// <summary>Seconds an offer of peace waits for an answer.</summary>
    public float PeaceOfferSeconds { get; }

    /// <summary>Seconds between declaring war on a faction at peace and the fighting.</summary>
    public float WarWarningSeconds { get; }

    /// <summary>Army each faction starts with next to its capital.</summary>
    public IReadOnlyList<StartingUnits> StartingUnits { get; }

    public UnitCatalog Units { get; }

    /// <summary>The building type every faction starts with; losing it eliminates the faction.</summary>
    public BuildingType Capital { get; }

    /// <summary>The Defense mode's shared citadel; null if the rules have none (then that mode cannot be played).</summary>
    public BuildingType? Citadel { get; }

    /// <summary>The numbers of the Defense mode.</summary>
    public DefenseRules Defense { get; }

    /// <summary>The ranks units climb by killing enemies (none if the rules have no "veterancy").</summary>
    public VeterancyRules Veterancy { get; }

    /// <summary>Every building type, in the order of the rules file.</summary>
    public IReadOnlyList<BuildingType> BuildingTypes => _buildingTypeOrder;

    public bool HasBuildingType(string id) => _buildingTypes.ContainsKey(id);

    public BuildingType GetBuildingType(string id) =>
        _buildingTypes.TryGetValue(id, out var type)
            ? type
            : throw new KeyNotFoundException($"Unknown building type '{id}'.");

    /// <summary>
    /// Parses the rules file. Read through JsonDocument rather than reflection-based deserialization:
    /// Godot reloads game assemblies in the editor, and the reflection serializer's type caches can block
    /// that unload. Keep the file strict JSON without comments: the Godot editor imports *.json files
    /// and warns on anything non-standard.
    /// </summary>
    public static GameRules Parse(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true });
        var root = doc.RootElement;

        var units = root.GetProperty("units").EnumerateArray().Select(ParseUnit).ToList();
        var buildings = root.GetProperty("buildings").EnumerateArray().Select(ParseBuilding).ToList();
        var startingUnits = root.GetProperty("startingUnits").EnumerateArray()
            .Select(s => new StartingUnits(RequiredString(s, "unit"), s.GetProperty("count").GetInt32()))
            .ToArray();
        // Optional sections: no "dayNight" means no night, no "diplomacy" the default timings.
        var dayNight = root.TryGetProperty("dayNight", out var d) ? d : default;
        var diplomacy = root.TryGetProperty("diplomacy", out var p) ? p : default;
        var defense = root.TryGetProperty("defense", out var w) ? ParseDefense(w) : null;
        var veterancy = root.TryGetProperty("veterancy", out var v) ? ParseVeterancy(v) : null;

        return new GameRules(
            root.GetProperty("startingGold").GetInt32(),
            root.GetProperty("buildRadius").GetSingle(),
            startingUnits,
            new UnitCatalog(units),
            buildings,
            OptionalSingle(root, "depositReserveSeconds"),
            OptionalSingle(root, "depositRegenSeconds"),
            OptionalInt(root, "upkeepAllowance"),
            OptionalInt(root, "goldCap"),
            OptionalInt(root, "lootPercent"),
            OptionalInt(root, "demolishRefundPercent"),
            OptionalSingle(dayNight, "day"),
            OptionalSingle(dayNight, "night"),
            OptionalSingle(dayNight, "sightFactor", 1f),
            OptionalSingle(diplomacy, "offerSeconds", DefaultPeaceOfferSeconds),
            OptionalSingle(diplomacy, "warWarningSeconds", DefaultWarWarningSeconds),
            defense,
            veterancy,
            OptionalInt(root, "upkeepSurchargeFrom"),
            OptionalInt(root, "upkeepSurchargePercent"),
            OptionalInt(root, "mineRaidPercent"));
    }

    private static VeterancyRules ParseVeterancy(JsonElement v) => new(v.GetProperty("ranks").EnumerateArray()
        .Select(r => new VeteranRank(
            r.GetProperty("experience").GetInt32(),
            OptionalSingle(r, "health", 1f),
            OptionalSingle(r, "damage", 1f)))
        .ToArray());

    private static DefenseRules ParseDefense(JsonElement d)
    {
        var x = DefenseRules.Default;
        int Int(string name, int fallback) => d.TryGetProperty(name, out var v) ? v.GetInt32() : fallback;
        return new DefenseRules(
            OptionalSingle(d, "firstWaveSeconds", x.FirstWaveSeconds),
            OptionalSingle(d, "waveIntervalSeconds", x.WaveIntervalSeconds),
            OptionalSingle(d, "warningSeconds", x.WarningSeconds),
            Int("baseBudget", x.BaseBudget),
            OptionalSingle(d, "growth", x.Growth),
            OptionalSingle(d, "extraPlayerShare", x.ExtraPlayerShare),
            Int("maxUnitsPerWave", x.MaxUnitsPerWave),
            Int("rewardBase", x.RewardBase),
            Int("rewardPerWave", x.RewardPerWave),
            Int("outpostCost", x.OutpostCost),
            OptionalSingle(d, "outpostBuildSeconds", x.OutpostBuildSeconds),
            Int("twoSidesFrom", x.TwoSidesFrom),
            Int("threeSidesFrom", x.ThreeSidesFrom),
            d.TryGetProperty("bossUnit", out var boss) && boss.GetString() is { Length: > 0 } bossId ? bossId : x.BossUnit);
    }

    private static UnitType ParseUnit(JsonElement u) => new(
        Id: RequiredString(u, "id"),
        Name: RequiredString(u, "name"),
        Speed: u.GetProperty("speed").GetSingle(),
        Radius: u.GetProperty("radius").GetSingle(),
        Cost: u.GetProperty("cost").GetInt32(),
        BuildTime: u.GetProperty("buildTime").GetSingle(),
        MaxHealth: u.GetProperty("health").GetInt32(),
        Damage: u.GetProperty("damage").GetInt32(),
        AttackRange: u.GetProperty("attackRange").GetSingle(),
        AttackCooldown: u.GetProperty("attackCooldown").GetSingle(),
        SightRange: u.GetProperty("sightRange").GetSingle(),
        ProjectileSpeed: OptionalSingle(u, "projectileSpeed"),
        RangedArmor: OptionalSingle(u, "rangedArmor"),
        Upkeep: OptionalInt(u, "upkeep"),
        IsSiege: OptionalBool(u, "siege"),
        BuildingDamageMultiplier: OptionalSingle(u, "buildingMultiplier", 1f),
        BonusAgainst: u.TryGetProperty("bonusAgainst", out var bonus)
            ? bonus.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetSingle())
            : null,
        ArmorPiercing: OptionalSingle(u, "armorPiercing"),
        Heal: u.TryGetProperty("heal", out var heal)
            ? new HealAbility(heal.GetProperty("amount").GetInt32(), heal.GetProperty("range").GetSingle(), heal.GetProperty("cooldown").GetSingle())
            : null,
        BarracksRequired: OptionalInt(u, "barracksRequired"),
        IsFlying: OptionalBool(u, "flying"),
        HitsAir: OptionalBool(u, "antiAir"),
        SplashRadius: OptionalSingle(u, "splash"));

    private static BuildingType ParseBuilding(JsonElement b)
    {
        var type = new BuildingType(
            Id: RequiredString(b, "id"),
            Name: RequiredString(b, "name"),
            Size: b.GetProperty("size").GetInt32(),
            Cost: b.GetProperty("cost").GetInt32(),
            BuildTime: b.GetProperty("buildTime").GetSingle(),
            MaxHealth: b.GetProperty("health").GetInt32(),
            RangedArmor: OptionalSingle(b, "rangedArmor"),
            Income: OptionalInt(b, "income"),
            ArmyCap: OptionalInt(b, "armyCap"),
            Recruits: b.TryGetProperty("recruits", out var r)
                ? r.EnumerateArray().Select(e => e.GetString() ?? "").ToArray()
                : Array.Empty<string>(),
            IsCapital: OptionalBool(b, "capital"),
            IsMine: OptionalBool(b, "mine"),
            Attack: b.TryGetProperty("attack", out var a)
                ? new BuildingAttack(
                    a.GetProperty("damage").GetInt32(),
                    a.GetProperty("range").GetSingle(),
                    a.GetProperty("cooldown").GetSingle(),
                    a.GetProperty("projectileSpeed").GetSingle(),
                    OptionalSingle(a, "splash"),
                    OptionalBool(a, "antiAir"))
                : null,
            SightRange: OptionalSingle(b, "sight"),
            MaxCount: OptionalInt(b, "maxCount"),
            UnlocksUnits: OptionalBool(b, "unlocksUnits"),
            IsCitadel: OptionalBool(b, "citadel"),
            // Kept as an integer percentage: prices must come out the same on every lockstep peer.
            CostGrowthPercent: b.TryGetProperty("costGrowth", out var growth) ? (int)Math.Round(growth.GetDouble() * 100) : 100);
        return b.TryGetProperty("tiers", out var tiers) ? WithTiers(type, tiers.EnumerateArray().ToList()) : type;
    }

    /// <summary>
    /// Chains a building's upgrade levels. Each entry of "tiers" is the next level: what it costs and how long it
    /// takes to reach ("cost", "time"), and the stats that change ("health", "damage", "range", "splash", "yieldBonus");
    /// the rest carries over from the level below.
    /// </summary>
    private static BuildingType WithTiers(BuildingType baseType, List<JsonElement> tiers)
    {
        var levels = new List<BuildingType> { baseType };
        foreach (var t in tiers)
        {
            var below = levels[^1];
            levels.Add(below with
            {
                Tier = below.Tier + 1,
                MaxHealth = t.TryGetProperty("health", out var health) ? health.GetInt32() : below.MaxHealth,
                Attack = below.Attack is { } attack
                    ? attack with
                    {
                        Damage = t.TryGetProperty("damage", out var damage) ? damage.GetInt32() : attack.Damage,
                        Range = t.TryGetProperty("range", out var range) ? range.GetSingle() : attack.Range,
                        SplashRadius = t.TryGetProperty("splash", out var splash) ? splash.GetSingle() : attack.SplashRadius,
                    }
                    : null,
                YieldBonus = t.TryGetProperty("yieldBonus", out var bonus) ? bonus.GetInt32() : below.YieldBonus,
            });
        }
        // Link from the top down, so each level points at the finished level above it.
        for (int i = levels.Count - 2; i >= 0; i--)
        {
            levels[i] = levels[i] with
            {
                NextTier = levels[i + 1],
                UpgradeCost = tiers[i].GetProperty("cost").GetInt32(),
                UpgradeTime = tiers[i].GetProperty("time").GetSingle(),
            };
        }
        return levels[0];
    }

    /// <summary>A number from <paramref name="element"/>, or <paramref name="fallback"/> if it (or the element itself) is missing.</summary>
    private static float OptionalSingle(JsonElement element, string name, float fallback = 0f) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value.GetSingle() : fallback;

    private static int OptionalInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.GetInt32() : 0;

    private static bool OptionalBool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.GetBoolean();

    private static string RequiredString(JsonElement element, string name) =>
        element.GetProperty(name).GetString() is { Length: > 0 } s
            ? s
            : throw new FormatException($"Missing '{name}' in rules entry {element}.");
}

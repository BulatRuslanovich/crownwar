using System.Collections.Generic;
using Godot;
using Warcrown.Sim.Buildings;
using Warcrown.Sim.Core;
using Warcrown.Sim.Units;
using Warcrown.Sim.World;

namespace Warcrown.UI;

/// <summary>
/// The HUD's descriptions, built from the rules so new kinds and levels describe themselves: building and unit
/// tooltips, what an upgrade changes, and why an order was refused. All text goes through the translation tables.
/// </summary>
public static class HudText
{
    /// <summary>What the next level changes, "before → after", built from the rules so new levels describe themselves.</summary>
    public static string DescribeUpgrade(BuildingType from, BuildingType to)
    {
        var lines = new List<string> { string.Format(T("UPGRADE_HEALTH"), from.MaxHealth, to.MaxHealth) };
        if (from.Attack is { } a && to.Attack is { } b)
        {
            if (a.Damage != b.Damage)
                lines.Add(string.Format(T("UPGRADE_DAMAGE"), a.Damage, b.Damage));
            if (!Mathf.IsEqualApprox(a.Range, b.Range))
                lines.Add(string.Format(T("UPGRADE_RANGE"), a.Range, b.Range));
            if (!Mathf.IsEqualApprox(a.SplashRadius, b.SplashRadius))
                lines.Add(string.Format(T("UPGRADE_SPLASH"), a.SplashRadius, b.SplashRadius));
        }
        if (to.YieldBonus != from.YieldBonus)
            lines.Add(string.Format(T("UPGRADE_YIELD"), from.YieldBonus, to.YieldBonus));
        lines.Add(string.Format(T("UPGRADE_COST_TIME"), from.UpgradeCost, from.UpgradeTime));
        return string.Join("\n", lines);
    }

    /// <summary>The translation key of veteran rank <paramref name="rank"/> (1 and up); ranks beyond the named ones keep the top name.</summary>
    public static string RankKey(int rank) => $"VETERAN_RANK_{Mathf.Clamp(rank, 1, NamedRanks)}";

    private const int NamedRanks = 3;

    public static string Describe(UpgradeError error) => T(error switch
    {
        UpgradeError.NotEnoughGold => "ERR_NOT_ENOUGH_GOLD",
        UpgradeError.InProgress => "ERR_UPGRADE_BUSY",
        UpgradeError.MaxTier => "ERR_UPGRADE_MAX",
        UpgradeError.NotComplete => "ERR_NOT_COMPLETE",
        _ => "ERR_NOT_OWNER",
    });

    public static string DescribeBuildingType(Simulation sim, BuildingType type, Building? instance = null)
    {
        var parts = new List<string>();
        if (type.IsMine)
            parts.Add(instance?.Deposit is { } d ? DescribeDeposit(d, type.YieldBonus) : T("INFO_MINE"));
        if (type.Income > 0)
            parts.Add(string.Format(T("INFO_INCOME"), type.Income));
        if (type.ArmyCap > 0)
            parts.Add(string.Format(T("INFO_ARMY_CAP"), type.ArmyCap));
        if (type.Recruits.Count > 0)
        {
            parts.Add(T("INFO_TRAINS"));
            if (instance != null)
                parts.Add(string.Format(T("INFO_TRAINING_SLOTS"), sim.TrainingSlots(instance)));
        }
        if (type.UnlocksUnits)
            parts.Add(T("INFO_UNLOCKS_UNITS"));
        if (type.Attack is { IsSplash: true } splash)
            parts.Add(string.Format(T("INFO_SPLASH"), splash.Range, splash.Damage, splash.SplashRadius));
        else if (type.Attack is { } attack)
            parts.Add(string.Format(T("INFO_SHOOTS"), attack.Range, attack.Damage));
        if (type.Attack is { } shooting)
            parts.Add(T(shooting.HitsAir ? "INFO_HITS_AIR" : "INFO_NO_AIR"));
        else if (type.SightRange > 0f && !type.IsCapital)
            parts.Add(string.Format(T("INFO_SIGHT"), type.SightRange));
        if (type.LightRadius > 0f)
            parts.Add(string.Format(T("INFO_LIGHT"), type.LightRadius));
        if (type.IsCapturable)
            parts.Add(T("INFO_FORT"));
        if (type.IsCapital)
            parts.Add(T("INFO_CAPITAL"));
        return string.Join("  ·  ", parts);
    }

    /// <summary>A recruit button's tooltip: the unit, its numbers, what is special about it, its upkeep.</summary>
    public static string DescribeUnitType(Simulation sim, UnitType type)
    {
        var lines = new List<string> { Names.Of(type) };
        lines.Add(type.IsHealer
            ? string.Format(T("INFO_UNIT_HEALER_STATS"), type.MaxHealth, type.Speed)
            : string.Format(T("INFO_UNIT_STATS"), type.MaxHealth, type.Damage, type.Speed));
        if (type.Heal is { } heal)
            lines.Add(string.Format(T("INFO_HEALS"), heal.Amount, heal.Cooldown));
        if (type.IsRanged)
            lines.Add(string.Format(T("INFO_RANGE"), type.AttackRange));
        if (type.ArmorPiercing > 0f)
            lines.Add(string.Format(T("INFO_PIERCING"), type.ArmorPiercing * 100f));
        if (type.RangedArmor > 0f && !type.IsSiege)
            lines.Add(string.Format(T("INFO_ARROW_ARMOR"), type.RangedArmor * 100f));
        if (type.BonusAgainst != null)
            foreach (var (against, bonus) in type.BonusAgainst)
                if (sim.Rules.Units.Contains(against))
                    lines.Add(string.Format(T("INFO_BONUS_AGAINST"), bonus, Names.Of(sim.Rules.Units.Get(against))));
        if (type.IsSiege)
            lines.Add(string.Format(T("INFO_SIEGE"), type.BuildingDamageMultiplier));
        if (type.SplashRadius > 0f)
            lines.Add(string.Format(T("INFO_FIRE"), type.SplashRadius));
        if (type.IsFlying)
            lines.Add(T("INFO_FLYING"));
        if (type.HitsAir)
            lines.Add(T("INFO_HITS_AIR"));
        lines.Add(string.Format(T("INFO_UPKEEP"), type.Upkeep));
        lines.Add(T("HUD_RECRUIT_SHIFT"));
        return string.Join("\n", lines);
    }

    /// <summary>The translation key of a doctrine's name (DOCTRINE_RAID…); its description is the key with _DESC.</summary>
    public static string DoctrineKey(Doctrine doctrine) => "DOCTRINE_" + doctrine.Id.ToUpperInvariant();

    /// <summary>A doctrine button's tooltip: what it is about, how it changes recruits, which kinds it opens.</summary>
    public static string DescribeDoctrine(Simulation sim, Doctrine doctrine)
    {
        var lines = new List<string> { T(DoctrineKey(doctrine)), T(DoctrineKey(doctrine) + "_DESC") };
        void Factor(string key, float factor)
        {
            if (!Mathf.IsEqualApprox(factor, 1f))
                lines.Add(string.Format(T(key), (factor - 1f) * 100f));
        }
        Factor("DOCTRINE_SPEED", doctrine.SpeedFactor);
        Factor("DOCTRINE_HEALTH", doctrine.HealthFactor);
        Factor("DOCTRINE_DAMAGE", doctrine.DamageFactor);
        Factor("DOCTRINE_COST", doctrine.CostFactor);
        Factor("DOCTRINE_UPKEEP", doctrine.UpkeepFactor);
        Factor("DOCTRINE_TRAINING", doctrine.BuildTimeFactor);
        var kinds = new List<string>();
        foreach (var type in sim.Rules.Units.All)
            if (type.Doctrine == doctrine.Id)
                kinds.Add(Names.Of(type));
        if (kinds.Count > 0)
            lines.Add(string.Format(T("DOCTRINE_OPENS"), string.Join(", ", kinds)));
        lines.Add(T("DOCTRINE_ONCE"));
        return string.Join("\n", lines);
    }

    public static string Describe(DoctrineError error, int barracks) => error switch
    {
        DoctrineError.NeedsBarracks => string.Format(T("ERR_DOCTRINE_BARRACKS"), barracks),
        DoctrineError.AlreadyChosen => T("ERR_DOCTRINE_CHOSEN"),
        DoctrineError.UnknownDoctrine => T("ERR_UNKNOWN_BUILDING"),
        _ => T("ERR_NOT_OWNER"),
    };

    /// <summary>A working mine's line: what it brings in and how much the deposit has left (or when it refills).</summary>
    public static string DescribeDeposit(GoldDeposit d, int levelBonus)
    {
        if (d.IsExhausted)
            return string.Format(T("INFO_DEPOSIT_EXHAUSTED"), Mathf.CeilToInt(d.RegenTicksLeft / (float)Simulation.TicksPerSecond));
        string mining = string.Format(T("INFO_MINING"), d.Yield + levelBonus);
        return d.IsEndless ? mining : mining + "  ·  " + string.Format(T("INFO_DEPOSIT_RESERVE"), d.Reserve, d.Capacity);
    }

    public static string Describe(RecruitError error) => T(error switch
    {
        RecruitError.NotEnoughGold => "ERR_NOT_ENOUGH_GOLD",
        RecruitError.ArmyCapReached => "ERR_ARMY_CAP",
        RecruitError.NotComplete => "ERR_NOT_COMPLETE",
        RecruitError.NotOwner => "ERR_NOT_OWNER",
        RecruitError.CannotRecruitType => "ERR_CANNOT_TRAIN",
        RecruitError.Locked => "ERR_LOCKED_SHORT",
        _ => "ERR_UNKNOWN_BUILDING",
    });

    public static string DescribePlacement(BuildError error) => T(error switch
    {
        BuildError.None => "BUILD_OK",
        BuildError.NotEnoughGold => "ERR_NOT_ENOUGH_GOLD",
        BuildError.NoUnitsNearby => "BUILD_ERR_NO_UNITS",
        BuildError.NeedsDeposit => "BUILD_ERR_NEEDS_DEPOSIT",
        BuildError.OnDeposit => "BUILD_ERR_ON_DEPOSIT",
        BuildError.Occupied => "BUILD_ERR_OCCUPIED",
        BuildError.BlockedTerrain => "BUILD_ERR_TERRAIN",
        BuildError.OutOfBounds => "BUILD_ERR_OUT_OF_BOUNDS",
        BuildError.LimitReached => "BUILD_ERR_LIMIT",
        _ => "BUILD_ERR_NOT_BUILDABLE",
    });

    private static string T(string key) => TranslationServer.Translate(key);
}

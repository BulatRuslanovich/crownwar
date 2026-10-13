using Lordsio.Sim.Buildings;
using Lordsio.Sim.Units;

namespace Lordsio.Sim.Core;

public enum UpgradeStat
{
    /// <summary>Percent more damage for the target unit or building types (towers).</summary>
    Damage,
    /// <summary>Percent more health for the target unit or building types.</summary>
    Health,
    /// <summary>Tiles more attack range for the target unit or building types.</summary>
    Range,
    /// <summary>More army limit from each building of the target types.</summary>
    ArmyCap,
    /// <summary>Gold per second more from every working mine (the deposit does not drain faster).</summary>
    MineYield,
    /// <summary>Percent change of the army's upkeep (negative means cheaper).</summary>
    Upkeep,
}

/// <param name="Targets">Unit or building type ids the effect applies to (unused for faction-wide stats).</param>
public sealed record UpgradeEffect(UpgradeStat Stat, float Amount, IReadOnlyList<string> Targets);

/// <summary>
/// A one-time improvement a faction researches at its capital: paid up front, takes
/// <see cref="ResearchTime"/> seconds, then applies to all its units and buildings, present and future.
/// </summary>
/// <param name="Requires">Id of an upgrade that must be researched first, or null.</param>
public sealed record UpgradeType(string Id, string Name, int Cost, float ResearchTime, IReadOnlyList<UpgradeEffect> Effects,
    string? Requires = null);

/// <summary>
/// What a faction has researched, the research under way, and the unit and building stats that result.
/// Stats are rebuilt from the base types whenever research completes, so effects never compound by accident:
/// percentages of the same stat add up and apply once to the base value.
/// </summary>
public sealed class Technology
{
    private readonly GameRules _rules;
    private readonly List<UpgradeType> _researched = new();
    private readonly Dictionary<string, UnitType> _units = new();
    private readonly Dictionary<string, BuildingType> _buildings = new();

    internal Technology(GameRules rules)
    {
        _rules = rules;
        Rebuild();
    }

    public IReadOnlyList<UpgradeType> Researched => _researched;

    public bool Has(string upgradeId) => _researched.Exists(u => u.Id == upgradeId);

    /// <summary>The upgrade being researched, or null.</summary>
    public UpgradeType? Current { get; private set; }

    public int ResearchTicksLeft { get; private set; }
    private int _researchTicks;

    public float ResearchProgress => Current == null || _researchTicks == 0 ? 0f : 1f - (float)ResearchTicksLeft / _researchTicks;

    /// <summary>Extra gold per second from each working mine.</summary>
    public int MineYieldBonus { get; private set; }

    /// <summary>Percent change of army upkeep (e.g. −30).</summary>
    public int UpkeepPercent { get; private set; }

    /// <summary>The faction's version of a unit type, with its upgrades applied.</summary>
    public UnitType Unit(string id) => _units[id];

    /// <summary>The faction's version of a building type, with its upgrades applied.</summary>
    public BuildingType Building(string id) => _buildings[id];

    internal void Start(UpgradeType upgrade, int ticks)
    {
        Current = upgrade;
        _researchTicks = Math.Max(1, ticks);
        ResearchTicksLeft = _researchTicks;
    }

    /// <summary>One tick of research; returns the upgrade when it completes (stats are already rebuilt).</summary>
    internal UpgradeType? Advance()
    {
        if (Current == null || --ResearchTicksLeft > 0)
            return null;
        var done = Current;
        Current = null;
        _researched.Add(done);
        Rebuild();
        return done;
    }

    private void Rebuild()
    {
        float Sum(UpgradeStat stat, string target) => _researched
            .SelectMany(u => u.Effects)
            .Where(e => e.Stat == stat && e.Targets.Contains(target))
            .Sum(e => e.Amount);

        static int Percent(int value, float percent) => (int)MathF.Round(value * (100f + percent) / 100f);

        foreach (var type in _rules.Units.All)
        {
            _units[type.Id] = type with
            {
                Damage = Percent(type.Damage, Sum(UpgradeStat.Damage, type.Id)),
                MaxHealth = Percent(type.MaxHealth, Sum(UpgradeStat.Health, type.Id)),
                AttackRange = type.AttackRange + Sum(UpgradeStat.Range, type.Id),
                SightRange = type.SightRange + Sum(UpgradeStat.Range, type.Id), // still sees what it can shoot
            };
        }
        foreach (var type in _rules.BuildingTypes)
        {
            _buildings[type.Id] = type with
            {
                MaxHealth = Percent(type.MaxHealth, Sum(UpgradeStat.Health, type.Id)),
                ArmyCap = type.ArmyCap + (int)Sum(UpgradeStat.ArmyCap, type.Id),
                Attack = type.Attack is { } attack
                    ? attack with
                    {
                        Damage = Percent(attack.Damage, Sum(UpgradeStat.Damage, type.Id)),
                        Range = attack.Range + Sum(UpgradeStat.Range, type.Id),
                    }
                    : null,
            };
        }
        var factionWide = _researched.SelectMany(u => u.Effects).ToList();
        MineYieldBonus = (int)factionWide.Where(e => e.Stat == UpgradeStat.MineYield).Sum(e => e.Amount);
        UpkeepPercent = (int)factionWide.Where(e => e.Stat == UpgradeStat.Upkeep).Sum(e => e.Amount);
    }
}

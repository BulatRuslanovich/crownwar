namespace Lordsio.Sim.Core;

/// <summary>
/// The numbers of the Defense mode (the "defense" section of the rules file): the players hold a shared citadel
/// against endless waves of a horde coming in from the edge of the map.
/// </summary>
/// <param name="FirstWaveSeconds">From the start of the match to the first wave.</param>
/// <param name="WaveIntervalSeconds">Between one wave and the next.</param>
/// <param name="WarningSeconds">How long before a wave its sides are announced.</param>
/// <param name="BaseBudget">What the first wave's troops cost, in gold, for one player.</param>
/// <param name="Growth">Each wave is worth this many times the one before.</param>
/// <param name="ExtraPlayerShare">Each player beyond the first adds this share of the budget.</param>
/// <param name="MaxUnitsPerWave">A cap on a wave's numbers (late waves bring stronger troops instead of ever more).</param>
/// <param name="RewardBase">Gold every player gets for a wave wiped out, plus <paramref name="RewardPerWave"/> × its number.</param>
/// <param name="OutpostCost">Gold to build a new outpost (a castle) once the old one has fallen.</param>
/// <param name="OutpostBuildSeconds">How long a new outpost takes to build.</param>
/// <param name="TwoSidesFrom">From this wave on, waves come from two sides of the map.</param>
/// <param name="ThreeSidesFrom">From this wave on, from three.</param>
/// <param name="BossUnit">The unit type of the warlord leading every tenth wave.</param>
public sealed record DefenseRules(
    float FirstWaveSeconds = 240f,
    float WaveIntervalSeconds = 150f,
    float WarningSeconds = 30f,
    int BaseBudget = 400,
    float Growth = 1.12f,
    float ExtraPlayerShare = 0.5f,
    int MaxUnitsPerWave = 150,
    int RewardBase = 100,
    int RewardPerWave = 15,
    int OutpostCost = 400,
    float OutpostBuildSeconds = 45f,
    int TwoSidesFrom = 6,
    int ThreeSidesFrom = 13,
    string BossUnit = "warlord")
{
    public static readonly DefenseRules Default = new();
}

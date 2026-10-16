using Warcrown.Sim.AI;
using Warcrown.Sim.Buildings;
using Warcrown.Sim.Core;
using Warcrown.Sim.World;

namespace Warcrown.Sim.Tests;

/// <summary>Shared plumbing for tests: the files shipped with the game and the usual ways to run a match.</summary>
internal static class TestSim
{
    private static string? _rulesJson;
    private static string? _defaultMap;

    /// <summary>The text of <c>Data/rules.json</c> (read once).</summary>
    public static string RulesJson => _rulesJson ??= File.ReadAllText(Path.Combine(TestMaps.ProjectRoot(), "Data", "rules.json"));

    /// <summary>The rules the game ships with (a fresh instance each call).</summary>
    public static GameRules RealRules() => GameRules.Parse(RulesJson);

    /// <summary>The reference map <c>Maps/default.txt</c> (a fresh instance each call).</summary>
    public static MapDefinition DefaultMap() =>
        MapLoader.Parse(_defaultMap ??= File.ReadAllText(Path.Combine(TestMaps.ProjectRoot(), "Maps", "default.txt")));

    /// <summary>The reference map with the real rules and shuffled capitals.</summary>
    public static Simulation CreateDefaultMatch(int factions, int seed = 1, int[]? teams = null) =>
        new(DefaultMap(), RealRules(), new MatchSetup(factions, seed, Teams: teams));

    /// <summary>Steps <paramref name="seconds"/> of game time, or until <paramref name="stop"/> says so.</summary>
    public static void RunSeconds(Simulation sim, float seconds, Func<bool>? stop = null)
    {
        for (int i = 0; i < seconds * Simulation.TicksPerSecond && !(stop?.Invoke() ?? false); i++)
            sim.Step();
    }

    public static void RunTicks(Simulation sim, int ticks)
    {
        for (int i = 0; i < ticks; i++)
            sim.Step();
    }

    /// <summary>One tick, then every bot gets its turn — the order the game uses.</summary>
    public static void StepWithBots(Simulation sim, IReadOnlyList<BotBrain> bots)
    {
        sim.Step();
        foreach (var bot in bots)
            bot.Update();
    }

    public static Building CapitalOf(Simulation sim, int factionId) => sim.GetBuilding(sim.GetFaction(factionId)!.CapitalId)!;
}

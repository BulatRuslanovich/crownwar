using System.Numerics;
using System.Text.Json.Nodes;
using Warcrown.Sim.Combat;
using Warcrown.Sim.Core;
using Warcrown.Sim.Units;
using Warcrown.Sim.World;

namespace Warcrown.Sim.Tests;

/// <summary>The Defense mode: a team of players holds a shared citadel against endless waves of a horde.</summary>
public class DefenseTests
{
    private const int Players = 2;
    private static readonly string[] Objects = { "start 18 28", "start 38 28", "citadel 28 28" };

    /// <summary>The real rules with quick waves: the first after 10 s (announced 4 s before), then every 10 s.</summary>
    private static GameRules QuickRules(Action<JsonObject>? tune = null)
    {
        var json = JsonNode.Parse(TestSim.RulesJson)!.AsObject();
        var defense = json["defense"]!.AsObject();
        defense["firstWaveSeconds"] = 10;
        defense["waveIntervalSeconds"] = 10;
        defense["warningSeconds"] = 4;
        tune?.Invoke(defense);
        return GameRules.Parse(json.ToJsonString());
    }

    private static Simulation Defense(GameRules? rules = null, int seed = 1) =>
        new(TestMaps.Parse(TestMaps.Field(64, 64), Objects), rules ?? QuickRules(), new MatchSetup(Players, seed, Mode: MatchMode.Defense));

    private static List<SimEvent> Run(Simulation sim, float seconds)
    {
        var events = new List<SimEvent>();
        for (int i = 0; i < seconds * Simulation.TicksPerSecond && !sim.IsOver; i++)
        {
            sim.Step();
            events.AddRange(sim.TakeEvents());
        }
        return events;
    }

    private static IEnumerable<Unit> Horde(Simulation sim) => sim.Units.Where(u => u.FactionId == sim.Defense!.HordeFactionId);

    [Test]
    public void PlayersAndGarrisonStandTogetherAgainstTheHorde()
    {
        var sim = Defense();
        var d = sim.Defense!;

        var citadel = sim.GetBuilding(d.CitadelId)!;
        Assert.That(citadel.Type.IsCitadel, Is.True);
        Assert.That(citadel.FactionId, Is.EqualTo(d.GarrisonFactionId));
        Assert.That(citadel.Anchor, Is.EqualTo(new Cell(28, 28)));
        Assert.That(sim.AreEnemies(0, 1), Is.False, "the players are one team");
        Assert.That(sim.AreEnemies(0, d.GarrisonFactionId), Is.False);
        Assert.That(sim.AreEnemies(0, d.HordeFactionId), Is.True);
        Assert.That(sim.AreEnemies(d.GarrisonFactionId, d.HordeFactionId), Is.True, "the citadel shoots the horde");
        Assert.That(sim.Factions.Where(f => d.IsPlayer(f.Id)).All(f => f.CapitalId != 0), Is.True, "every player starts with an outpost");
    }

    [Test]
    public void AMapWithoutACitadelCannotHostTheMode()
    {
        Assert.Throws<ArgumentException>(() => new Simulation(TestMaps.Parse(TestMaps.Field(40, 40), "start 5 5", "start 30 30"),
            QuickRules(), new MatchSetup(2, 1, Mode: MatchMode.Defense)));
    }

    [Test]
    public void AWaveIsAnnouncedThenComesFromTheEdgeAndMarchesOnTheCitadel()
    {
        var sim = Defense();

        var warned = Run(sim, 6.2f);
        var incoming = warned.OfType<WaveIncoming>().Single();
        Assert.That(incoming.Wave, Is.EqualTo(1));
        Assert.That(incoming.From, Has.Count.EqualTo(1), "the first waves come from one side");
        Assert.That(incoming.Seconds, Is.EqualTo(4f).Within(0.1f));
        Assert.That(Horde(sim), Is.Empty, "nobody yet: only a warning");
        Assert.That(sim.Defense!.IncomingFrom, Has.Count.EqualTo(1));

        var started = Run(sim, 4f).OfType<WaveStarted>().Single();
        Assert.That(started.Wave, Is.EqualTo(1));
        var horde = Horde(sim).ToList();
        Assert.That(horde, Has.Count.EqualTo(started.Units).And.Count.GreaterThan(0));
        var middle = new Vector2(32f, 32f);
        Assert.That(horde.All(u => Vector2.Distance(u.Position, middle) > 20f), Is.True, "they come in from the edge");
        Assert.That(horde.All(u => u.Order == UnitOrder.AttackMove), Is.True, "and advance, fighting what they meet");
        Assert.That(sim.Defense!.IncomingFrom, Is.Empty);
        Assert.That(sim.SecondsToNextWave, Is.EqualTo(10f).Within(0.2f));
    }

    [Test]
    public void WavesGrowAndComeFromMoreSides()
    {
        var sim = Defense(QuickRules(d =>
        {
            d["growth"] = 1.5;
            d["twoSidesFrom"] = 3;
        }));

        var started = Run(sim, 31f).OfType<WaveStarted>().ToList();

        Assert.That(started.Select(s => s.Wave), Is.EqualTo(new[] { 1, 2, 3 }));
        Assert.That(started[2].Units, Is.GreaterThan(started[0].Units), "each wave bigger than the last");
        Assert.That(started[0].From, Has.Count.EqualTo(1));
        Assert.That(started[2].From, Has.Count.EqualTo(2), "two sides from the third wave");
    }

    [Test]
    public void MorePlayersFaceBiggerWaves()
    {
        var two = Run(Defense(), 10.2f).OfType<WaveStarted>().Single().Units;
        var four = new Simulation(TestMaps.Parse(TestMaps.Field(64, 64), "start 18 28", "start 38 28", "start 18 40", "start 38 40", "citadel 28 28"),
            QuickRules(), new MatchSetup(4, 1, Mode: MatchMode.Defense));
        Assert.That(Run(four, 10.2f).OfType<WaveStarted>().Single().Units, Is.GreaterThan(two));
    }

    [Test]
    public void AWaveWipedOutPaysEveryPlayer()
    {
        var sim = Defense();
        Run(sim, 10.2f);
        int[] gold = Enumerable.Range(0, Players).Select(f => sim.GetFaction(f)!.Treasury.Gold).ToArray();

        foreach (var unit in Horde(sim).ToList())
            CombatSystem.ApplyDamage(unit, unit.MaxHealth, ranged: false, attackerFactionId: 0);
        var cleared = Run(sim, 0.1f).OfType<WaveCleared>().Single();

        var rules = sim.Rules.Defense;
        Assert.That(cleared.Wave, Is.EqualTo(1));
        Assert.That(cleared.Reward, Is.EqualTo(rules.RewardBase + rules.RewardPerWave));
        for (int f = 0; f < Players; f++)
            Assert.That(sim.GetFaction(f)!.Treasury.Gold, Is.GreaterThanOrEqualTo(gold[f] + cleared.Reward - 1), $"player {f} paid");
        Assert.That(sim.Defense!.WavesCleared, Is.EqualTo(1));
    }

    [Test]
    public void TheCitadelFallingEndsTheMatchForEveryone()
    {
        var sim = Defense();
        var citadel = sim.GetBuilding(sim.Defense!.CitadelId)!;

        CombatSystem.ApplyDamage(citadel, citadel.MaxHealth, ranged: false, attackerFactionId: sim.Defense.HordeFactionId);
        var ended = Run(sim, 0.1f).OfType<MatchEnded>().Single();

        Assert.That(sim.IsOver, Is.True);
        Assert.That(ended.WinningTeam, Is.EqualTo(-1), "nobody wins a defense: the score is the waves held");
    }

    [Test]
    public void AFallenOutpostLeavesThePlayerInAndCanBeBuiltAgain()
    {
        var sim = Defense();
        var faction = sim.GetFaction(0)!;
        var outpost = sim.GetBuilding(faction.CapitalId)!;
        int army = sim.Units.Count(u => u.FactionId == 0);

        CombatSystem.ApplyDamage(outpost, outpost.MaxHealth, ranged: false, attackerFactionId: sim.Defense!.HordeFactionId);
        Run(sim, 0.1f);

        Assert.That(faction.IsEliminated, Is.False);
        Assert.That(sim.IsOver, Is.False);
        Assert.That(sim.Units.Count(u => u.FactionId == 0), Is.EqualTo(army), "the army stays");

        // A new outpost next to the troops, at the mode's price; the other player, who has one, may not build a second.
        var rules = sim.Rules.Defense;
        faction.Treasury.Add(rules.OutpostCost);
        int gold = faction.Treasury.Gold;
        var near = sim.Units.First(u => u.FactionId == 0).Position;
        var anchor = Enumerable.Range(-5, 11).SelectMany(dy => Enumerable.Range(-5, 11).Select(dx => new Cell((int)near.X + dx, (int)near.Y + dy)))
            .First(c => sim.CanBuild(0, sim.Rules.Capital.Id, c) == BuildError.None);
        Assert.That(sim.CanBuild(1, sim.Rules.Capital.Id, anchor), Is.Not.EqualTo(BuildError.None));

        sim.Issue(new BuildCommand(0, sim.Rules.Capital.Id, anchor));
        Run(sim, 0.1f);
        var rebuilt = sim.GetBuilding(faction.CapitalId)!;
        Assert.That(rebuilt.Anchor, Is.EqualTo(anchor));
        Assert.That(rebuilt.IsComplete, Is.False, "an outpost takes time to build");
        Assert.That(faction.Treasury.Gold, Is.LessThanOrEqualTo(gold - rules.OutpostCost + 5));
        Assert.That(sim.CanBuild(0, sim.Rules.Capital.Id, new Cell(anchor.X + 4, anchor.Y)), Is.Not.EqualTo(BuildError.None), "one outpost at a time");

        Run(sim, rules.OutpostBuildSeconds + 0.5f);
        Assert.That(rebuilt.IsComplete, Is.True);
    }

    [Test]
    public void ACappedWaveSpendsTheRestOnTougherTroops()
    {
        var sim = Defense(QuickRules(d =>
        {
            d["baseBudget"] = 100000;
            d["maxUnitsPerWave"] = 10;
        }));

        Run(sim, 10.2f);

        var horde = Horde(sim).ToList();
        Assert.That(horde, Has.Count.EqualTo(15), "the cap, raised by half for the second player");
        Assert.That(horde.All(u => u.Type.Id == "heavy"), Is.True, "the first wave's spearmen all became men-at-arms");
    }

    [Test]
    public void ABotBuildsItsFallenOutpostAgain()
    {
        var sim = Defense(QuickRules(d => d["firstWaveSeconds"] = 600));
        var faction = sim.GetFaction(0)!;
        var bot = new AI.BotBrain(sim, 0, AI.BotDifficulty.Normal, 1);
        for (int i = 0; i < Simulation.TicksPerSecond * 3; i++)
        {
            sim.Step();
            bot.Update();
        }
        var outpost = sim.GetBuilding(faction.CapitalId)!;
        CombatSystem.ApplyDamage(outpost, outpost.MaxHealth, ranged: false, attackerFactionId: sim.Defense!.HordeFactionId);
        faction.Treasury.Add(sim.Rules.Defense.OutpostCost);

        for (int i = 0; i < Simulation.TicksPerSecond * 30 && sim.GetBuilding(faction.CapitalId) == null; i++)
        {
            sim.Step();
            bot.Update();
        }

        var rebuilt = sim.GetBuilding(faction.CapitalId);
        Assert.That(rebuilt, Is.Not.Null, "a new outpost stands (or is going up)");
        Assert.That(Vector2.Distance(rebuilt!.Position, outpost.Position), Is.LessThan(12f), "near the old one");
    }

    [Test]
    public void IdleHordeTroopsMarchOnTheCitadel()
    {
        var sim = Defense();
        var straggler = sim.SpawnUnit("infantry", sim.Defense!.HordeFactionId, new Vector2(3.5f, 3.5f));

        Run(sim, 1.1f);

        Assert.That(straggler.Order, Is.EqualTo(UnitOrder.AttackMove));
        Assert.That(Vector2.Distance(straggler.Destination!.Value, sim.GetBuilding(sim.Defense.CitadelId)!.Position), Is.LessThan(6f));
    }

    [Test]
    public void EveryTenthWaveBringsAWarlord()
    {
        var sim = Defense(QuickRules(d =>
        {
            d["firstWaveSeconds"] = 6;
            d["waveIntervalSeconds"] = 5;
        }));
        var boss = sim.Rules.Defense.BossUnit;
        bool seen = false;
        for (int i = 0; i < 52 * Simulation.TicksPerSecond && !sim.IsOver && !seen; i++)
        {
            sim.Step();
            foreach (var e in sim.TakeEvents().OfType<WaveStarted>())
            {
                bool has = Horde(sim).Any(u => u.Type.Id == boss);
                if (e.Wave < 10)
                    Assert.That(has, Is.False, $"wave {e.Wave} has no warlord");
                else
                    seen = has;
            }
            // Keep the field clear so the waves can be told apart.
            if (sim.Tick % Simulation.TicksPerSecond == 0)
                foreach (var unit in Horde(sim).Where(u => u.Type.Id != boss).ToList())
                    CombatSystem.ApplyDamage(unit, unit.MaxHealth, ranged: false, attackerFactionId: 0);
        }
        Assert.That(seen, Is.True, "the tenth wave is led by a warlord");
    }

    [Test]
    public void TheHordeReallyAttacks()
    {
        var sim = Defense();
        foreach (var unit in sim.Units.Where(u => sim.Defense!.IsPlayer(u.FactionId)).ToList())
            CombatSystem.ApplyDamage(unit, unit.MaxHealth, ranged: false, attackerFactionId: sim.Defense!.HordeFactionId);

        Run(sim, 60f);

        // With nobody to stop them, the waves reach the walls (the citadel and the outposts shoot back).
        var defenders = sim.Buildings.Where(b => !sim.AreEnemies(b.FactionId, sim.Defense!.HordeFactionId)).ToList();
        Assert.That(defenders.Any(b => b.Health < b.MaxHealth) || defenders.Count < Players + 1, Is.True, "the horde fights its way in");
    }

    [Test]
    public void ThereIsNoDiplomacyInDefense()
    {
        var sim = Defense();
        sim.Issue(new ProposePeaceCommand(0, sim.Defense!.HordeFactionId));

        Assert.That(Run(sim, 0.1f).OfType<DiplomacyRejected>().Single().Reason, Is.EqualTo(DiplomacyError.UnknownFaction));
    }

    [Test]
    public void TheSameMatchPlaysTheSameEverywhere()
    {
        var a = Defense(seed: 4);
        var b = Defense(seed: 4);
        for (int i = 0; i < 40 * Simulation.TicksPerSecond; i++)
        {
            a.Step();
            b.Step();
        }
        Assert.That(b.ComputeChecksum(), Is.EqualTo(a.ComputeChecksum()));
        Assert.That(a.Defense!.WavesLaunched, Is.EqualTo(4));
    }
}

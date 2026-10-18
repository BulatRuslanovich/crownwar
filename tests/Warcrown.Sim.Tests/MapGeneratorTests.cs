using Warcrown.Sim.Core;
using Warcrown.Sim.World;

namespace Warcrown.Sim.Tests;

public class MapGeneratorTests
{
    private static GameRules Rules() =>
        TestSim.RealRules();

    private static string Fingerprint(MapDefinition map)
    {
        var tiles = new char[map.Grid.Width * map.Grid.Height];
        for (int y = 0; y < map.Grid.Height; y++)
            for (int x = 0; x < map.Grid.Width; x++)
                tiles[y * map.Grid.Width + x] = (char)('a' + (int)map.Grid.GetTerrain(new Cell(x, y)));
        return new string(tiles) + "|" + string.Join(";", map.StartPositions) + "|" + string.Join(";", map.Deposits);
    }

    [Test]
    public void BiomesMixOnEveryKindOfMap()
    {
        int mixed = 0;
        var seen = new HashSet<Biome>();
        for (int seed = 1; seed <= 30; seed++)
        {
            var map = MapGenerator.Generate(seed);
            var g = map.Grid;
            Assert.That(map.Biomes, Has.Count.EqualTo(g.Width * g.Height));
            var kinds = map.Biomes!.Distinct().ToList();
            seen.UnionWith(kinds);
            if (kinds.Count > 1)
                mixed++;
        }
        Assert.That(seen, Is.EquivalentTo(new[] { Biome.Meadow, Biome.Winter, Biome.Desert }), "every biome turns up");
        Assert.That(mixed, Is.GreaterThan(20), "most maps mix two or three biomes");
    }

    [TestCase(2, 96)]
    [TestCase(3, 112)]
    [TestCase(4, 128)]
    public void MapsAreSquaresSizedForThePlayers(int players, int size)
    {
        var map = MapGenerator.Generate(5, players);
        Assert.That((map.Grid.Width, map.Grid.Height), Is.EqualTo((size, size)));
        Assert.That(map.StartPositions, Has.Count.EqualTo(players), "one castle per side");
        Assert.That(map.Deposits.Count, Is.InRange(players * 3, players * 6), "gold is scarce: own two, the rich ones between, a couple scattered");
    }

    /// <summary>
    /// What players complained of, three in a network game: the start must be fair. Every castle as far from the middle
    /// and from its neighbours as the others (within the few tiles each castle may shift to dry land), two deposits of
    /// its own close by, and no gold out in the corners where only some castles could reach it first.
    /// </summary>
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    public void EveryCastleStartsAlike(int players)
    {
        for (int seed = 1; seed <= 25; seed++)
        {
            var map = MapGenerator.Generate(seed, players);
            float size = map.Grid.Width;
            var middle = new System.Numerics.Vector2(size / 2f, size / 2f);
            var castles = map.StartPositions.Select(s => new System.Numerics.Vector2(s.X + 1.5f, s.Y + 1.5f)).ToList();

            var toMiddle = castles.Select(c => System.Numerics.Vector2.Distance(c, middle)).ToList();
            Assert.That(toMiddle.Max() - toMiddle.Min(), Is.LessThan(12f), $"seed {seed}: as far from the middle");
            var toNeighbour = castles.Select((c, i) => castles.Where((_, j) => j != i).Min(o => System.Numerics.Vector2.Distance(c, o))).ToList();
            Assert.That(toNeighbour.Max() - toNeighbour.Min(), Is.LessThan(14f), $"seed {seed}: as far from the nearest enemy");
            Assert.That(toNeighbour.Min(), Is.GreaterThan(55f), $"seed {seed}: room to grow (tight maps, but not on top of each other)");

            foreach (var c in castles)
            {
                int own = map.Deposits.Count(d => System.Numerics.Vector2.Distance(new(d.Anchor.X + 1, d.Anchor.Y + 1), c) <= 23f);
                Assert.That(own, Is.GreaterThanOrEqualTo(2), $"seed {seed}: castle at {c} has its two deposits");
            }
            // Gold away from the castles (all but their own two) lies inside the circle, none out in the corners.
            foreach (var d in map.Deposits)
            {
                var at = new System.Numerics.Vector2(d.Anchor.X + 1, d.Anchor.Y + 1);
                if (castles.Min(c => System.Numerics.Vector2.Distance(at, c)) > 23f)
                    Assert.That(System.Numerics.Vector2.Distance(at, middle), Is.LessThanOrEqualTo(size / 2f), $"seed {seed}: deposit {d} inside the circle");
            }
        }
    }

    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    public void FortsStandInTheGapsAsFarFromEitherNeighbour(int players)
    {
        var rules = TestSim.RealRules();
        for (int seed = 1; seed <= 25; seed++)
        {
            var map = MapGenerator.Generate(seed, players);
            var castles = map.StartPositions.Select(s => new System.Numerics.Vector2(s.X + 1.5f, s.Y + 1.5f)).ToList();
            Assert.That(map.FortAnchors, Has.Count.InRange(players - 1, players == 2 ? 2 : players), $"seed {seed}: a fort in (nearly) every gap");
            foreach (var fort in map.FortAnchors)
            {
                var at = new System.Numerics.Vector2(fort.X + 1.5f, fort.Y + 1.5f);
                var nearest = castles.Select(c => System.Numerics.Vector2.Distance(at, c)).Order().ToList();
                Assert.That(nearest[1] - nearest[0], Is.LessThan(12f), $"seed {seed}: fort at {fort} as far from both neighbours");
                Assert.That(nearest[0], Is.GreaterThan(25f), $"seed {seed}: fort at {fort} is nobody's back yard");
            }

            // The real rules put a fort with its guard on every spot.
            var sim = new Simulation(map, rules, new MatchSetup(players, seed));
            Assert.That(sim.Buildings.Count(b => b.Type.IsCapturable && b.FactionId == Simulation.NeutralFactionId), Is.EqualTo(map.FortAnchors.Count));
            int guard = rules.Forts.Guard.Sum(g => g.Count);
            Assert.That(sim.Units.Count(u => u.FactionId == Simulation.NeutralFactionId), Is.EqualTo(guard * map.FortAnchors.Count));
        }
    }

    [Test]
    public void SameSeedSameMapOtherSeedOtherMap()
    {
        string first = Fingerprint(MapGenerator.Generate(7, 3));
        string again = Fingerprint(MapGenerator.Generate(7, 3));
        string other = Fingerprint(MapGenerator.Generate(8, 3));
        string forFour = Fingerprint(MapGenerator.Generate(7, 4));

        Assert.That(again, Is.EqualTo(first));
        Assert.That(other, Is.Not.EqualTo(first));
        Assert.That(forFour, Is.Not.EqualTo(first), "the number of players shapes the map too");
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void MapsAreNotMirroredButEveryCastleHasAFairChance(int seed)
    {
        var map = MapGenerator.Generate(seed);
        var g = map.Grid;

        // No symmetry: luck decides what lies around each castle.
        int differ = 0;
        for (int y = 0; y < g.Height; y++)
            for (int x = 0; x < g.Width / 2; x++)
                if (g.GetTerrain(new Cell(x, y)) != g.GetTerrain(new Cell(g.Width - 1 - x, y)))
                    differ++;
        Assert.That(differ, Is.GreaterThan(g.Width * g.Height / 8), "the halves differ");

        // But no hopeless start: castles far apart, each with a deposit close by.
        var starts = map.StartPositions;
        for (int i = 0; i < starts.Count; i++)
        {
            for (int j = i + 1; j < starts.Count; j++)
                Assert.That(System.Numerics.Vector2.Distance(new(starts[i].X, starts[i].Y), new(starts[j].X, starts[j].Y)), Is.GreaterThan(60f));
            var s = starts[i];
            float nearest = map.Deposits.Min(d => System.Numerics.Vector2.Distance(new(d.Anchor.X + 1, d.Anchor.Y + 1), new(s.X + 1.5f, s.Y + 1.5f)));
            Assert.That(nearest, Is.LessThan(16f), $"start {s}: gold nearby");
        }
    }

    /// <summary>
    /// Defense maps: the citadel in the very middle, the outposts in a close ring round it at equal distances, a way in
    /// for the waves from every edge, two deposits of its own by every outpost and the rest out beyond the ring.
    /// </summary>
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    public void DefenseMapsRingTheCitadel(int players)
    {
        var rules = Rules();
        for (int seed = 1; seed <= 20; seed++)
        {
            var map = MapGenerator.Generate(seed, players, MatchMode.Defense);
            var g = map.Grid;
            int size = g.Width;
            var middle = new System.Numerics.Vector2(size / 2f, size / 2f);
            Assert.That(map.Citadel, Is.EqualTo(new Cell(size / 2 - 2, size / 2 - 2)), $"seed {seed}: the citadel in the middle");
            Assert.That(map.StartPositions, Has.Count.EqualTo(players));

            var outposts = map.StartPositions.Select(s => new System.Numerics.Vector2(s.X + 1.5f, s.Y + 1.5f)).ToList();
            var toMiddle = outposts.Select(o => System.Numerics.Vector2.Distance(o, middle)).ToList();
            Assert.That(toMiddle.Min(), Is.GreaterThan(15f), $"seed {seed}: clear of the citadel");
            Assert.That(toMiddle.Max() - toMiddle.Min(), Is.LessThan(5f), $"seed {seed}: the outposts alike");

            // The waves can reach the citadel from every edge.
            int home = g.ComponentOf(new Cell(size / 2, size / 2 + 3));
            Assert.That(home, Is.GreaterThanOrEqualTo(0), $"seed {seed}: open ground at the gate");
            foreach (var edge in new[] { new Cell(size / 2, 3), new Cell(size - 4, size / 2), new Cell(size / 2, size - 4), new Cell(3, size / 2) })
                Assert.That(g.FindNearestInComponent(edge, home, 24), Is.Not.Null, $"seed {seed}: a way in from {edge}");
            foreach (var start in map.StartPositions)
                Assert.That(g.ComponentOf(new Cell(start.X + 1, start.Y + 3)), Is.EqualTo(home), $"seed {seed}: outpost {start} linked");

            foreach (var o in outposts)
                Assert.That(map.Deposits.Count(d => System.Numerics.Vector2.Distance(new(d.Anchor.X + 1, d.Anchor.Y + 1), o) <= 21f),
                    Is.GreaterThanOrEqualTo(2), $"seed {seed}: outpost at {o} has its own gold");
            Assert.That(map.Deposits.Count(d => System.Numerics.Vector2.Distance(new(d.Anchor.X + 1, d.Anchor.Y + 1), middle) > 30f),
                Is.GreaterThanOrEqualTo(players * 3), $"seed {seed}: riches out towards the edge");

            Assert.DoesNotThrow(() => new Simulation(map, rules, new MatchSetup(players, seed, Mode: MatchMode.Defense)), $"seed {seed}");
        }
    }

    [Test]
    public void TheHordeComesOnAGeneratedDefenseMap()
    {
        var sim = new Simulation(MapGenerator.Generate(3, 3, MatchMode.Defense), Rules(), new MatchSetup(3, 3, Mode: MatchMode.Defense));
        var rules = sim.Rules.Defense;
        int ticks = Warcrown.Sim.Core.Ticks.FromSeconds(rules.FirstWaveSeconds + rules.WaveIntervalSeconds) + 20;
        for (int i = 0; i < ticks && !sim.IsOver; i++)
            sim.Step();

        Assert.That(sim.Defense!.WavesLaunched, Is.EqualTo(2));
        Assert.That(sim.Units.Count(u => u.FactionId == sim.Defense.HordeFactionId) + sim.Defense.WavesCleared, Is.GreaterThan(0));
        Assert.That(sim.IsOver, Is.False, "the citadel holds the first waves");
    }

    [Test]
    public void EveryMapIsPlayable()
    {
        var rules = Rules();
        for (int seed = 0; seed < 200; seed++)
        {
            int players = 2 + seed % 3;
            var map = MapGenerator.Generate(seed, players);
            var g = map.Grid;
            Assert.That(map.StartPositions, Has.Count.EqualTo(players), $"seed {seed}");

            // Castles fit and the match can be created for that many players.
            Assert.DoesNotThrow(() => new Simulation(MapGenerator.Generate(seed, players), rules, new MatchSetup(players, seed)), $"seed {seed}");

            // All castles are linked by land (roads bridge the rivers).
            int home = g.ComponentOf(new Cell(map.StartPositions[0].X + 1, map.StartPositions[0].Y + 3));
            foreach (var start in map.StartPositions)
                Assert.That(g.ComponentOf(new Cell(start.X + 1, start.Y + 3)), Is.EqualTo(home), $"seed {seed}: start {start}");

            // Enough gold, all of it on open ground the castles can reach.
            Assert.That(map.Deposits.Count, Is.GreaterThanOrEqualTo(3 * players), $"seed {seed}: safe and exposed deposits for everyone, plus rich ones");
            Assert.That(map.Deposits.Any(d => d.Yield > MapLoader.DefaultDepositYield), $"seed {seed}: a rich deposit");
            foreach (var deposit in map.Deposits)
            {
                for (int y = deposit.Anchor.Y; y < deposit.Anchor.Y + GoldDeposit.Size; y++)
                    for (int x = deposit.Anchor.X; x < deposit.Anchor.X + GoldDeposit.Size; x++)
                        Assert.That(TerrainInfo.IsBuildable(g.GetTerrain(new Cell(x, y))), $"seed {seed}: deposit {deposit} on open ground");
                bool reachable = Enumerable.Range(deposit.Anchor.Y - 1, GoldDeposit.Size + 2)
                    .SelectMany(y => Enumerable.Range(deposit.Anchor.X - 1, GoldDeposit.Size + 2).Select(x => new Cell(x, y)))
                    .Any(c => g.InBounds(c) && g.IsWalkable(c) && g.ComponentOf(c) == home);
                Assert.That(reachable, $"seed {seed}: deposit {deposit} reachable");
            }
            Assert.That(map.Deposits.Select(d => d.Anchor).Distinct().Count(), Is.EqualTo(map.Deposits.Count), $"seed {seed}: no duplicates");
        }
    }

    /// <summary>Prints maps as text (run by hand: --filter "Name~DumpMap").</summary>
    [Explicit("prints maps for a look")]
    [TestCase(70, 4)]
    [TestCase(1, 3)]
    [TestCase(2, 2)]
    [TestCase(5, 3, MatchMode.Defense)]
    public void DumpMap(int seed, int players, MatchMode mode = MatchMode.Versus)
    {
        var map = MapGenerator.Generate(seed, players, mode);
        var g = map.Grid;
        var rows = new List<char[]>();
        for (int y = 0; y < g.Height; y++)
            rows.Add(Enumerable.Range(0, g.Width).Select(x => g.GetTerrain(new Cell(x, y)) switch
            {
                Terrain.Grass => '.', Terrain.Forest => 'f', Terrain.Road => '=', Terrain.Water => '~', Terrain.Bridge => '#', _ => '^',
            }).ToArray());
        foreach (var d in map.Deposits)
            for (int y = d.Anchor.Y; y < d.Anchor.Y + 2; y++)
                for (int x = d.Anchor.X; x < d.Anchor.X + 2; x++)
                    rows[y][x] = d.Yield > MapLoader.DefaultDepositYield ? 'G' : 'g';
        foreach (var s in map.StartPositions)
            for (int y = s.Y; y < s.Y + 3; y++)
                for (int x = s.X; x < s.X + 3; x++)
                    rows[y][x] = 'C';
        if (map.Citadel is { } k)
            for (int y = k.Y; y < k.Y + 4; y++)
                for (int x = k.X; x < k.X + 4; x++)
                    rows[y][x] = 'K';
        TestContext.Out.WriteLine($"seed {seed}: {map.Deposits.Count} deposits");
        foreach (var row in rows)
            TestContext.Out.WriteLine(new string(row));
    }

    [Test]
    public void BotsFinishAMatchOnAGeneratedMap()
    {
        var rules = Rules();
        var sim = new Simulation(MapGenerator.Generate(11), rules, new MatchSetup(4, 11));
        var bots = Enumerable.Range(0, 4).Select(i => new AI.BotBrain(sim, i, AI.BotDifficulty.Hard, i)).ToList();

        while (!sim.IsOver && sim.Tick < 40 * 60 * Simulation.TicksPerSecond)
        {
            sim.Step();
            foreach (var bot in bots)
                bot.Update();
        }

        Assert.That(sim.IsOver, Is.True, "a winner within 40 minutes");
        Assert.That(sim.WinningTeam, Is.GreaterThanOrEqualTo(0));
    }
}

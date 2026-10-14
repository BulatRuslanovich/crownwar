using Lordsio.Sim.Core;
using Lordsio.Sim.World;

namespace Lordsio.Sim.Tests;

public class MapGeneratorTests
{
    private static GameRules Rules() =>
        GameRules.Parse(File.ReadAllText(Path.Combine(TestMaps.ProjectRoot(), "Data", "rules.json")));

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

    [Test]
    public void GeneratedMapsAreLargerAndRichInGold()
    {
        var map = MapGenerator.Generate(5);
        Assert.That((map.Grid.Width, map.Grid.Height), Is.EqualTo((192, 128)));
        Assert.That(map.Deposits.Count, Is.GreaterThanOrEqualTo(16));
    }

    [Test]
    public void SameSeedSameMapOtherSeedOtherMap()
    {
        string first = Fingerprint(MapGenerator.Generate(7));
        string again = Fingerprint(MapGenerator.Generate(7));
        string other = Fingerprint(MapGenerator.Generate(8));

        Assert.That(again, Is.EqualTo(first));
        Assert.That(other, Is.Not.EqualTo(first));
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

        // But no hopeless start: castles far apart, in their own corners, each with a deposit close by.
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

    [Test]
    public void EveryMapIsPlayable()
    {
        var rules = Rules();
        for (int seed = 0; seed < 200; seed++)
        {
            var map = MapGenerator.Generate(seed);
            var g = map.Grid;
            Assert.That(map.StartPositions, Has.Count.EqualTo(4), $"seed {seed}");

            // Castles fit and the match can be created for four players.
            Assert.DoesNotThrow(() => new Simulation(MapGenerator.Generate(seed), rules, new MatchSetup(4, seed)), $"seed {seed}");

            // All castles are linked by land (roads bridge the rivers).
            int home = g.ComponentOf(new Cell(map.StartPositions[0].X + 1, map.StartPositions[0].Y + 3));
            foreach (var start in map.StartPositions)
                Assert.That(g.ComponentOf(new Cell(start.X + 1, start.Y + 3)), Is.EqualTo(home), $"seed {seed}: start {start}");

            // Enough gold, all of it on open ground the castles can reach.
            Assert.That(map.Deposits.Count, Is.GreaterThanOrEqualTo(9), $"seed {seed}: safe and exposed deposits for everyone, plus rich ones");
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
    [TestCase(70)]
    [TestCase(1)]
    [TestCase(2)]
    public void DumpMap(int seed)
    {
        var map = MapGenerator.Generate(seed);
        var g = map.Grid;
        var rows = new List<char[]>();
        for (int y = 0; y < g.Height; y++)
            rows.Add(Enumerable.Range(0, g.Width).Select(x => g.GetTerrain(new Cell(x, y)) switch
            {
                Terrain.Grass => '.', Terrain.Forest => 'f', Terrain.Road => '=', Terrain.Water => '~', _ => '^',
            }).ToArray());
        foreach (var d in map.Deposits)
            for (int y = d.Anchor.Y; y < d.Anchor.Y + 2; y++)
                for (int x = d.Anchor.X; x < d.Anchor.X + 2; x++)
                    rows[y][x] = d.Yield > MapLoader.DefaultDepositYield ? 'G' : 'g';
        foreach (var s in map.StartPositions)
            for (int y = s.Y; y < s.Y + 3; y++)
                for (int x = s.X; x < s.X + 3; x++)
                    rows[y][x] = 'C';
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

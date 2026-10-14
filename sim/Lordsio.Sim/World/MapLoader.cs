using System.Globalization;

namespace Lordsio.Sim.World;

/// <summary><paramref name="Count"/> units of one type, spread in formation around <paramref name="Cell"/>.</summary>
public sealed record UnitSpawn(string TypeId, int FactionId, Cell Cell, int Count = 1);

/// <summary>A gold deposit's top-left cell and its yield in gold per second once mined.</summary>
public sealed record DepositSpawn(Cell Anchor, int Yield);

/// <param name="StartPositions">Top-left cells where capitals may be placed; factions are assigned to them at match start.</param>
/// <param name="Biomes">The biome of every cell, row by row (generated maps); null means all meadow.</param>
public sealed record MapDefinition(
    TileGrid Grid,
    IReadOnlyList<UnitSpawn> UnitSpawns,
    IReadOnlyList<Cell> StartPositions,
    IReadOnlyList<DepositSpawn> Deposits,
    IReadOnlyList<Biome>? Biomes = null)
{
    public Biome BiomeAt(Cell cell) => Biomes?[cell.Y * Grid.Width + cell.X] ?? Biome.Meadow;
}

/// <summary>
/// Parses the text map format:
/// <code>
/// # comment
/// [tiles]
/// ..ff~~^^==        one character per tile, all rows the same length
/// [objects]
/// start &lt;x&gt; &lt;y&gt;                         possible capital location (top-left cell)
/// gold &lt;x&gt; &lt;y&gt; [yield]                  2×2 gold deposit (top-left cell), default yield 3
/// unit &lt;typeId&gt; &lt;faction&gt; &lt;x&gt; &lt;y&gt; [count]  pre-placed units (tests, scenarios)
/// </code>
/// Unit type ids and whether a capital fits at a start are checked when the match is created.
/// </summary>
public static class MapLoader
{
    public const int DefaultDepositYield = 3;

    public static MapDefinition Parse(string text)
    {
        var rows = new List<string>();
        var units = new List<UnitSpawn>();
        var starts = new List<Cell>();
        var deposits = new List<DepositSpawn>();
        string? section = null;
        int lineNo = 0;

        foreach (string rawLine in text.Split('\n'))
        {
            lineNo++;
            string line = rawLine.TrimEnd('\r', ' ', '\t');
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim().ToLowerInvariant();
                if (section is not ("tiles" or "objects"))
                    throw Error(lineNo, $"unknown section [{section}]");
                continue;
            }

            switch (section)
            {
                case "tiles":
                    rows.Add(line);
                    break;
                case "objects":
                    ParseObject(line, lineNo, units, starts, deposits);
                    break;
                default:
                    throw Error(lineNo, "content before the first section header");
            }
        }

        if (rows.Count == 0)
            throw new FormatException("Map has no [tiles] section.");

        int width = rows[0].Length;
        int height = rows.Count;
        var tiles = new Terrain[width * height];
        for (int y = 0; y < height; y++)
        {
            if (rows[y].Length != width)
                throw new FormatException($"Map row {y} has length {rows[y].Length}, expected {width}.");
            for (int x = 0; x < width; x++)
            {
                if (!TerrainInfo.TryParse(rows[y][x], out var terrain))
                    throw new FormatException($"Map row {y}, column {x}: unknown tile '{rows[y][x]}'.");
                tiles[y * width + x] = terrain;
            }
        }

        var grid = new TileGrid(width, height, tiles);
        foreach (var spawn in units)
        {
            if (!grid.IsWalkable(spawn.Cell))
                throw new FormatException($"Unit spawn at {spawn.Cell} is not on a walkable tile.");
        }
        var depositCells = new HashSet<Cell>();
        foreach (var deposit in deposits)
        {
            for (int y = 0; y < GoldDeposit.Size; y++)
            {
                for (int x = 0; x < GoldDeposit.Size; x++)
                {
                    var cell = new Cell(deposit.Anchor.X + x, deposit.Anchor.Y + y);
                    if (!grid.InBounds(cell) || !TerrainInfo.IsBuildable(grid.GetTerrain(cell)))
                        throw new FormatException($"Gold deposit at {deposit.Anchor} must lie on open ground (grass or forest).");
                    if (!depositCells.Add(cell))
                        throw new FormatException($"Gold deposit at {deposit.Anchor} overlaps another deposit.");
                }
            }
        }
        return new MapDefinition(grid, units, starts, deposits);
    }

    private static void ParseObject(string line, int lineNo, List<UnitSpawn> units, List<Cell> starts, List<DepositSpawn> deposits)
    {
        string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        switch (parts[0])
        {
            case "unit":
                if (parts.Length is not (5 or 6))
                    throw Error(lineNo, "expected: unit <typeId> <faction> <x> <y> [count]");
                int count = parts.Length == 6 ? ParseInt(parts[5], lineNo) : 1;
                if (count < 1)
                    throw Error(lineNo, "unit count must be at least 1");
                units.Add(new UnitSpawn(parts[1], ParseInt(parts[2], lineNo), ParseCell(parts, 3, lineNo), count));
                break;
            case "start":
                if (parts.Length != 3)
                    throw Error(lineNo, "expected: start <x> <y>");
                starts.Add(ParseCell(parts, 1, lineNo));
                break;
            case "gold":
                if (parts.Length is not (3 or 4))
                    throw Error(lineNo, "expected: gold <x> <y> [yield]");
                int yield = parts.Length == 4 ? ParseInt(parts[3], lineNo) : DefaultDepositYield;
                if (yield <= 0)
                    throw Error(lineNo, "deposit yield must be positive");
                deposits.Add(new DepositSpawn(ParseCell(parts, 1, lineNo), yield));
                break;
            default:
                throw Error(lineNo, $"unknown object '{parts[0]}'");
        }
    }

    private static Cell ParseCell(string[] parts, int index, int lineNo) =>
        new(ParseInt(parts[index], lineNo), ParseInt(parts[index + 1], lineNo));

    private static int ParseInt(string s, int lineNo) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)
            ? v
            : throw Error(lineNo, $"'{s}' is not an integer");

    private static FormatException Error(int lineNo, string message) =>
        new($"Map line {lineNo}: {message}.");
}

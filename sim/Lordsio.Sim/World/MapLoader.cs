using System.Globalization;

namespace Lordsio.Sim.World;

/// <summary><paramref name="Count"/> units of one type, spread in formation around <paramref name="Cell"/>.</summary>
public sealed record UnitSpawn(string TypeId, int FactionId, Cell Cell, int Count = 1);

public sealed record MapDefinition(TileGrid Grid, IReadOnlyList<UnitSpawn> UnitSpawns);

/// <summary>
/// Parses the text map format:
/// <code>
/// # comment
/// [tiles]
/// ..ff~~^^==        one character per tile, all rows the same length
/// [objects]
/// unit &lt;typeId&gt; &lt;faction&gt; &lt;x&gt; &lt;y&gt; [count]
/// </code>
/// </summary>
public static class MapLoader
{
    public static MapDefinition Parse(string text)
    {
        var rows = new List<string>();
        var spawns = new List<UnitSpawn>();
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
                    spawns.Add(ParseObject(line, lineNo));
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
        foreach (var spawn in spawns)
        {
            if (!grid.IsWalkable(spawn.Cell))
                throw new FormatException($"Unit spawn at {spawn.Cell} is not on a walkable tile.");
        }
        return new MapDefinition(grid, spawns);
    }

    private static UnitSpawn ParseObject(string line, int lineNo)
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
                return new UnitSpawn(parts[1], ParseInt(parts[2], lineNo),
                    new Cell(ParseInt(parts[3], lineNo), ParseInt(parts[4], lineNo)), count);
            default:
                throw Error(lineNo, $"unknown object '{parts[0]}'");
        }
    }

    private static int ParseInt(string s, int lineNo) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)
            ? v
            : throw Error(lineNo, $"'{s}' is not an integer");

    private static FormatException Error(int lineNo, string message) =>
        new($"Map line {lineNo}: {message}.");
}

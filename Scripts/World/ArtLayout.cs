using System.Collections.Generic;
using Godot;

namespace Lordsio.World;

/// <summary>Sheet layout shared by the art generator and the views that cut frames out of the sheets.</summary>
public static class ArtLayout
{
    public const int Tile = 16;
    public const int TerrainVariants = 4;
    public const int GrassRow = 0, ForestRow = 1, MountainRow = 3, RoadRow = 4, BridgeRow = 6;
    /// <summary>Frames of the animated terrain tiles (water, surf, woods), in the rows under each tile's first frame.</summary>
    public const int TileFrames = 4;
    /// <summary>Animated: water variants and shore overlays (by mask), each followed by its frames.</summary>
    public const int WaterRow = 43, ShoreRow = 47;
    /// <summary>
    /// Woods (animated: four positions modulo 2 in x and y, each <see cref="TileFrames"/> rows) and mountain ranges
    /// (sixteen positions modulo 4), by neighbour mask.
    /// </summary>
    public const int ForestRangeRow = 51, MountainRangeRow = 11;
    /// <summary>Tiles before the woods' and the ranges' patterns repeat (the rows per position).</summary>
    public const int ForestPeriod = 2, MountainPeriod = 4;
    /// <summary>First of 16 rows of a biome's ground blending into a neighbouring cell of another biome (8-bit neighbour mask).</summary>
    public const int BiomeBlendRow = 27;
    /// <summary>Decor: small things lying about on open ground, one kind a column over two rows.</summary>
    public const int DecorRow = 67, DecorKinds = 32;
    public const int TerrainRows = 69;

    /// <summary>The animated tiles: first row, how many columns, how long each frame shows, and whether neighbours
    /// start at different moments (water glitters at random; surf and woods move together, in one wind).</summary>
    public static IEnumerable<(int Row, int Columns, float[] Durations, bool RandomStart)> AnimatedTiles()
    {
        yield return (WaterRow, TerrainVariants, new[] { 0.5f, 0.5f, 0.5f, 0.5f }, true);
        yield return (ShoreRow, 16, new[] { 0.7f, 0.5f, 0.6f, 0.5f }, false);
        for (int parity = 0; parity < 4; parity++)
            yield return (ForestRangeRow + parity * TileFrames, 16, new[] { 1.4f, 0.45f, 1.1f, 0.45f }, false);
    }

    /// <summary>Which decor kinds each biome scatters (a kind listed twice turns up twice as often).</summary>
    public static readonly int[] MeadowDecor = { 0, 1, 2, 3, 31, 4, 4, 4, 5, 5, 6, 7, 8, 9, 10, 11, 12, 6, 4 };
    public static readonly int[] WinterDecor = { 13, 13, 13, 14, 15, 16, 17, 17, 18, 19, 20, 20, 6, 10 };
    public static readonly int[] DesertDecor = { 21, 21, 22, 22, 23, 24, 25, 25, 26, 26, 27, 28, 28, 29, 30, 6 };

    /// <summary>The eight neighbours of a cell as (dx, dy), in the bit order of the blend masks: N, NE, E, SE, S, SW, W, NW.</summary>
    public static readonly (int Dx, int Dy)[] BlendNeighbours =
        { (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1) };

    /// <summary>Frames side by side in each building sheet (flags, wheels, fires).</summary>
    public const int BuildingFrames = 4;

    public const int UnitCellWidth = 16;
    public const int UnitCellHeight = 20;
    public const int UnitFrames = 8;
    public const int UnitRows = 8;
    public const int FrameIdle = 0, FrameBreath = 1, FrameWalk1 = 2, FrameWalk2 = 3, FrameWalk3 = 4, FrameWalk4 = 5, FrameWindup = 6, FrameStrike = 7;
    /// <summary>Feet position inside a unit cell.</summary>
    public static readonly Vector2I UnitFeet = new(8, 19);

    public static int UnitRow(string unitTypeId) => unitTypeId switch
    {
        "archer" => 1,
        "heavy" => 2,
        "ram" => 3,
        "cavalry" => 4,
        "crossbowman" => 5,
        "monk" => 6,
        "trebuchet" => 7,
        _ => 0,
    };
}

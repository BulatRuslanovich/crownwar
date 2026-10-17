using System.Collections.Generic;
using Godot;

namespace Warcrown.World;

/// <summary>Sheet layout shared by the art generator and the views that cut frames out of the sheets.</summary>
public static class ArtLayout
{
    public const int Tile = 32;
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

    /// <summary>
    /// Columns of every unit sheet, by animation: standing and breathing (a hovering flyer's slow wing beats), a stride
    /// (a flyer's wing beats), an attack — two frames of wind-up, the blow, two of follow-through and one settling back —,
    /// a flinch when struck, and a death: a stagger, buckling knees, a fall, and two frames lying on the ground.
    /// </summary>
    public const int IdleFrames = 4, WalkFrames = 8, AttackFrames = 6, DeathFrames = 5;
    public const int FrameIdle = 0, FrameWalk = FrameIdle + IdleFrames, FrameAttack = FrameWalk + WalkFrames;
    public const int FrameHurt = FrameAttack + AttackFrames, FrameDeath = FrameHurt + 1;
    public const int UnitFrames = FrameDeath + DeathFrames;
    /// <summary>Frames of an attack: the blow lands on <see cref="AttackStrike"/>.</summary>
    public const int AttackWindup = 0, AttackStrike = 2, AttackRecover = 5;

    /// <summary>Cells of the soldiers' sheet (units.png): 32×40, feet at (16, 39).</summary>
    public const int UnitCellWidth = 32, UnitCellHeight = 40;
    public static readonly Vector2I UnitFeet = new(16, 39);
    /// <summary>Rows of the soldiers' sheet, in order.</summary>
    public static readonly string[] UnitKinds = { "infantry", "archer", "heavy", "ram", "cavalry", "crossbowman", "monk", "trebuchet", "griffin" };

    /// <summary>Cells of the sheet of big beasts (units_big.png): 64×64, feet at (32, 62).</summary>
    public const int BigCellSize = 64;
    public static readonly Vector2I BigFeet = new(32, 62);
    /// <summary>Rows of the big sheet, in order: the horde's warlord, who towers over everyone, and the dragon.</summary>
    public static readonly string[] BigKinds = { "warlord", "dragon" };

    /// <summary>Where a unit kind is drawn: which sheet (<see cref="Big"/>), its row, its cell and where the feet are in it.</summary>
    public readonly record struct UnitArt(bool Big, int Row)
    {
        public int CellWidth => Big ? BigCellSize : UnitCellWidth;
        public int CellHeight => Big ? BigCellSize : UnitCellHeight;
        public Vector2I Feet => Big ? BigFeet : UnitFeet;
        public int Rows => Big ? BigKinds.Length : UnitKinds.Length;
    }

    /// <summary>The picture of a unit kind (an unknown kind looks like infantry).</summary>
    public static UnitArt UnitArtOf(string unitTypeId)
    {
        int big = System.Array.IndexOf(BigKinds, unitTypeId);
        return big >= 0 ? new UnitArt(true, big) : new UnitArt(false, System.Math.Max(0, System.Array.IndexOf(UnitKinds, unitTypeId)));
    }

    /// <summary>How high above the ground (its shadow) a flyer is drawn, in world pixels; 0 for those on foot.</summary>
    public static float FlightAltitude(string? unitTypeId) => unitTypeId switch
    {
        "griffin" => 28f,
        "dragon" => 40f,
        _ => 0f,
    };
}

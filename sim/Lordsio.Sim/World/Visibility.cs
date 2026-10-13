using System.Numerics;
using Lordsio.Sim.Core;

namespace Lordsio.Sim.World;

/// <summary>
/// Fog of war for one team: which cells its units and buildings see right now, and which it has ever seen.
/// Allies share sight. This is presentation only: the rules and the bots do not use it, so it never
/// affects the simulation (and lockstep peers may each keep their own).
/// </summary>
public sealed class Visibility
{
    private readonly bool[] _visible;
    private readonly bool[] _explored;
    private readonly bool[] _previous;
    private readonly Dictionary<float, Cell[]> _discs = new();

    public Visibility(int width, int height)
    {
        Width = width;
        Height = height;
        _visible = new bool[width * height];
        _explored = new bool[width * height];
        _previous = new bool[width * height];
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>Everything is shown (spectating after defeat, or fog switched off).</summary>
    public bool RevealedAll { get; private set; }

    /// <summary>Bumped whenever the visible or explored cells change, so views redraw only then.</summary>
    public int Version { get; private set; }

    public bool IsVisible(Cell cell) => RevealedAll || (InBounds(cell) && _visible[cell.Y * Width + cell.X]);

    public bool IsVisible(Vector2 position) => IsVisible(Cell.FromPosition(position));

    public bool IsExplored(Cell cell) => RevealedAll || (InBounds(cell) && _explored[cell.Y * Width + cell.X]);

    /// <summary>Whether any cell of a square footprint is in sight.</summary>
    public bool IsAnyVisible(Cell anchor, int size)
    {
        for (int y = anchor.Y; y < anchor.Y + size; y++)
            for (int x = anchor.X; x < anchor.X + size; x++)
                if (IsVisible(new Cell(x, y)))
                    return true;
        return false;
    }

    public void RevealAll()
    {
        if (RevealedAll)
            return;
        RevealedAll = true;
        Version++;
    }

    /// <summary>Recomputes what <paramref name="team"/> sees from its living units and buildings.</summary>
    public void Update(Simulation sim, int team)
    {
        if (RevealedAll)
            return;
        bool changed = false;
        Array.Copy(_visible, _previous, _visible.Length);
        Array.Clear(_visible);

        foreach (var unit in sim.Units)
        {
            if (unit.IsAlive && TeamOf(sim, unit.FactionId) == team)
                Stamp(unit.Position, unit.Type.SightRange);
        }
        foreach (var building in sim.Buildings)
        {
            if (building.IsAlive && TeamOf(sim, building.FactionId) == team)
                Stamp(building.Position, building.SightRange);
        }

        for (int i = 0; i < _visible.Length; i++)
        {
            if (_visible[i] != _previous[i])
                changed = true;
            if (_visible[i] && !_explored[i])
            {
                _explored[i] = true;
                changed = true;
            }
        }
        if (changed)
            Version++;
    }

    private static int TeamOf(Simulation sim, int factionId) => sim.GetFaction(factionId)?.Team ?? -1;

    private bool InBounds(Cell cell) => cell.X >= 0 && cell.Y >= 0 && cell.X < Width && cell.Y < Height;

    /// <summary>Marks every cell whose center lies within <paramref name="radius"/> of <paramref name="center"/>.</summary>
    private void Stamp(Vector2 center, float radius)
    {
        var origin = Cell.FromPosition(center);
        foreach (var offset in Disc(radius))
        {
            int x = origin.X + offset.X, y = origin.Y + offset.Y;
            if (x < 0 || y < 0 || x >= Width || y >= Height)
                continue;
            var cellCenter = new Vector2(x + 0.5f, y + 0.5f);
            if (Vector2.DistanceSquared(cellCenter, center) <= radius * radius)
                _visible[y * Width + x] = true;
        }
    }

    /// <summary>Cell offsets that can fall within the radius (a square slightly larger than the circle), cached.</summary>
    private Cell[] Disc(float radius)
    {
        if (_discs.TryGetValue(radius, out var cells))
            return cells;
        int r = (int)MathF.Ceiling(radius) + 1;
        var list = new List<Cell>();
        for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
                if (dx * dx + dy * dy <= (r + 0.5f) * (r + 0.5f))
                    list.Add(new Cell(dx, dy));
        return _discs[radius] = list.ToArray();
    }
}

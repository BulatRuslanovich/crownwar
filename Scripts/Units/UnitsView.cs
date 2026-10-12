using System.Collections.Generic;
using Godot;
using Lordsio.Core;
using Lordsio.Sim.Units;

namespace Lordsio.Units;

/// <summary>
/// Keeps one <see cref="UnitView"/> per simulated unit, positioned with interpolation between ticks.
/// Also draws the remaining path of selected units for debugging.
/// </summary>
public partial class UnitsView : Node2D
{
    private static readonly Color PathColor = new(1f, 1f, 1f, 0.55f);

    private readonly Dictionary<int, UnitView> _views = new();
    private readonly List<Vector2> _pathPoints = new();
    private MatchController _match = null!;

    public void Initialize(MatchController match)
    {
        _match = match;
    }

    public override void _Process(double delta)
    {
        float alpha = _match.InterpolationAlpha;
        foreach (var unit in _match.Sim.Units)
        {
            if (!_views.TryGetValue(unit.Id, out var view))
            {
                view = new UnitView { Name = $"Unit{unit.Id}" };
                _views.Add(unit.Id, view);
                AddChild(view);
            }
            view.Position = InterpolatedPosition(unit, alpha);
            view.Selected = _match.Selection.Contains(unit.Id);
        }

        QueueRedraw(); // path overlay follows the units every frame
    }

    public override void _Draw()
    {
        float alpha = _match.InterpolationAlpha;
        foreach (int id in _match.Selection.Ids)
        {
            if (_match.Sim.GetUnit(id) is not { State: UnitState.Moving } unit)
                continue;

            _pathPoints.Clear();
            _pathPoints.Add(InterpolatedPosition(unit, alpha));
            for (int i = unit.PathIndex; i < unit.Path.Count; i++)
                _pathPoints.Add(SimConvert.ToWorld(unit.Path[i]));
            if (_pathPoints.Count < 2)
                continue;

            DrawPolyline(_pathPoints.ToArray(), PathColor, 1f);
            for (int i = 1; i < _pathPoints.Count; i++)
                DrawRect(new Rect2(_pathPoints[i] - Vector2.One, Vector2.One * 2), PathColor);
        }
    }

    private static Vector2 InterpolatedPosition(Unit unit, float alpha) =>
        SimConvert.ToWorld(System.Numerics.Vector2.Lerp(unit.PreviousPosition, unit.Position, alpha));
}

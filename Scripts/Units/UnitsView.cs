using System.Collections.Generic;
using Godot;
using Lordsio.Core;
using Lordsio.Sim.Units;

namespace Lordsio.Units;

/// <summary>
/// Keeps one <see cref="UnitView"/> per simulated unit, positioned with interpolation between ticks,
/// and drops views (and selection) of units that died. Also draws the planned route of selected units.
/// </summary>
public partial class UnitsView : Node2D
{
    private static readonly Color PathColor = new(1f, 1f, 1f, 0.45f);

    private readonly Dictionary<int, UnitView> _views = new();
    private readonly List<int> _gone = new();
    private readonly List<Vector2> _pathPoints = new();
    private MatchController _match = null!;

    public void Initialize(MatchController match)
    {
        _match = match;
    }

    /// <summary>The unit views (for their shadows).</summary>
    public IEnumerable<UnitView> Views => _views.Values;

    public override void _Process(double delta)
    {
        float alpha = _match.InterpolationAlpha;
        foreach (var unit in _match.Sim.Units)
        {
            if (!_views.TryGetValue(unit.Id, out var view))
            {
                view = new UnitView { Name = $"Unit{unit.Id}" };
                view.Initialize(unit);
                _views.Add(unit.Id, view);
                AddChild(view);
            }
            view.Visible = !_match.IsHidden(unit);
            view.Position = InterpolatedPosition(unit, alpha);
            view.Selected = _match.Selection.Contains(unit.Id);
        }

        _gone.Clear();
        foreach (var (id, view) in _views)
        {
            if (_match.Sim.GetUnit(id) != null)
                continue;
            view.QueueFree();
            _match.Selection.Remove(id);
            _gone.Add(id);
        }
        foreach (int id in _gone)
            _views.Remove(id);

        QueueRedraw(); // route overlay follows the units every frame
    }

    /// <summary>Strike and hit animations; the damage itself was already dealt by the simulation.</summary>
    public void OnSimEvent(Sim.Core.SimEvent e)
    {
        if (e is not Sim.Core.AttackLanded hit)
            return;
        if (!hit.Ranged && _views.TryGetValue(hit.AttackerId, out var attacker))
            attacker.Lunge(SimConvert.ToWorld(hit.Position) - attacker.Position);
        if (_views.TryGetValue(hit.TargetId, out var target))
        {
            // Staggered away from whoever dealt the blow (an archer far off, a tower, a soldier in its face).
            var from = _match.Sim.GetUnit(hit.AttackerId)?.Position ?? _match.Sim.GetBuilding(hit.AttackerId)?.Position ?? hit.Position;
            target.Flash(SimConvert.ToWorld(hit.Position) - SimConvert.ToWorld(from));
        }
    }

    public override void _Draw()
    {
        float alpha = _match.InterpolationAlpha;
        foreach (int id in _match.Selection.Ids)
        {
            // Only marches: chase routes change every tick and would just flicker.
            if (_match.Sim.GetUnit(id) is not { State: UnitState.Moving, Target: null } unit)
                continue;

            _pathPoints.Clear();
            _pathPoints.Add(InterpolatedPosition(unit, alpha));
            for (int i = unit.PathIndex; i < unit.Path.Count; i++)
                _pathPoints.Add(SimConvert.ToWorld(unit.Path[i]));
            if (_pathPoints.Count < 2)
                continue;

            DrawPolyline(_pathPoints.ToArray(), PathColor, 1f);
            DrawRect(new Rect2(_pathPoints[^1] - Vector2.One, Vector2.One * 2), PathColor);
        }
    }

    private static Vector2 InterpolatedPosition(Unit unit, float alpha) =>
        SimConvert.ToWorld(System.Numerics.Vector2.Lerp(unit.PreviousPosition, unit.Position, alpha));
}

using System.Collections.Generic;
using Godot;
using Lordsio.Core;

namespace Lordsio.Buildings;

/// <summary>
/// Keeps one <see cref="BuildingView"/> per building as they are placed and destroyed. Enemy buildings
/// appear once seen; out of sight they stay as frozen "ghosts" of what was last seen — even after being
/// destroyed, until the player looks again.
/// </summary>
public partial class BuildingsView : Node2D
{
    private readonly Dictionary<int, BuildingView> _views = new();
    private readonly List<int> _gone = new();
    private readonly HashSet<int> _seen = new();
    private MatchController _match = null!;

    public void Initialize(MatchController match)
    {
        _match = match;
    }

    public override void _Process(double delta)
    {
        foreach (var building in _match.Sim.Buildings)
        {
            if (!_views.TryGetValue(building.Id, out var view))
            {
                view = new BuildingView { Name = $"Building{building.Id}" };
                view.Initialize(building);
                _views.Add(building.Id, view);
                AddChild(view);
            }
            bool hidden = _match.IsHidden(building);
            if (!hidden)
                _seen.Add(building.Id);
            view.Visible = _seen.Contains(building.Id);
            view.Ghost = hidden;
            if (hidden && _match.Selection.BuildingId == building.Id)
                _match.Selection.Clear();
        }

        _gone.Clear();
        foreach (var (id, view) in _views)
        {
            if (_match.Sim.GetBuilding(id) == null)
            {
                // A ruin nobody saw fall keeps standing in memory until its place is seen again.
                if (view.Visible && view.Ghost && _match.IsHidden(view.Building))
                    continue;
                view.QueueFree();
                _gone.Add(id);
                continue;
            }
            view.Selected = _match.Selection.BuildingId == id;
        }
        foreach (int id in _gone)
        {
            _views.Remove(id);
            _seen.Remove(id);
            if (_match.Selection.BuildingId == id)
                _match.Selection.Clear();
        }
    }
}

using System.Collections.Generic;
using Godot;
using Lordsio.Core;

namespace Lordsio.Buildings;

/// <summary>Keeps one <see cref="BuildingView"/> per building as they are placed and destroyed.</summary>
public partial class BuildingsView : Node2D
{
    private readonly Dictionary<int, BuildingView> _views = new();
    private readonly List<int> _gone = new();
    private MatchController _match = null!;

    public void Initialize(MatchController match)
    {
        _match = match;
    }

    public override void _Process(double delta)
    {
        foreach (var building in _match.Sim.Buildings)
        {
            if (_views.ContainsKey(building.Id))
                continue;
            var view = new BuildingView { Name = $"Building{building.Id}" };
            view.Initialize(building);
            _views.Add(building.Id, view);
            AddChild(view);
        }

        _gone.Clear();
        foreach (var (id, view) in _views)
        {
            if (_match.Sim.GetBuilding(id) == null)
            {
                view.QueueFree();
                _gone.Add(id);
                continue;
            }
            view.Selected = _match.Selection.BuildingId == id;
        }
        foreach (int id in _gone)
        {
            _views.Remove(id);
            if (_match.Selection.BuildingId == id)
                _match.Selection.Clear();
        }
    }
}

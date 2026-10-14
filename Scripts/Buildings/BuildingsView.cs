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
    /// <summary>Night lights are added onto what lies beneath them.</summary>
    private readonly CanvasItemMaterial _additive = new() { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
    private GradientTexture2D _halo = null!;

    public void Initialize(MatchController match)
    {
        _match = match;
        // A round glow, bright in the middle and fading to nothing at the rim.
        var gradient = new Gradient();
        gradient.SetColor(0, Colors.White);
        gradient.SetColor(1, new Color(1f, 1f, 1f, 0f));
        gradient.AddPoint(0.45f, new Color(1f, 1f, 1f, 0.35f));
        _halo = new GradientTexture2D
        {
            Gradient = gradient, Width = 64, Height = 64, Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(1f, 0.5f),
        };
    }

    /// <summary>The building views (for their shadows).</summary>
    public IEnumerable<BuildingView> Views => _views.Values;

    public override void _Process(double delta)
    {
        foreach (var building in _match.Sim.Buildings)
        {
            if (!_views.TryGetValue(building.Id, out var view))
            {
                view = new BuildingView { Name = $"Building{building.Id}" };
                view.Initialize(building, _match, _additive, _halo);
                _views.Add(building.Id, view);
                AddChild(view);
            }
            bool hidden = _match.IsHidden(building);
            if (!hidden && _seen.Add(building.Id))
                _match.Map.ClearDecor(building.Anchor, building.Type.Size); // no flowers growing through its walls
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

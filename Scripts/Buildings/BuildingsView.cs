using System.Collections.Generic;
using Godot;
using Warcrown.Core;

namespace Warcrown.Buildings;

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
    /// <summary>Pops for buildings whose views are made later this frame (a site placed in the tick just run).</summary>
    private readonly Dictionary<int, Vector2> _pendingPops = new();

    /// <summary>A site thumped into the ground; a building finished; a level raised.</summary>
    private static readonly Vector2 PlacedSquash = new(1.25f, 0.7f), CompletedSquash = new(0.86f, 1.16f), UpgradedSquash = new(1.12f, 0.88f);

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

    /// <summary>Visual reactions of buildings to what happened in the tick (the damage itself is already done).</summary>
    public void OnSimEvent(Sim.Core.SimEvent e)
    {
        switch (e)
        {
            case Sim.Core.AttackLanded hit when _views.TryGetValue(hit.TargetId, out var struck):
                struck.Hit();
                break;
            case Sim.Core.BuildingPlaced placed:
                PopOrLater(placed.BuildingId, PlacedSquash);
                break;
            case Sim.Core.BuildingCompleted completed:
                PopOrLater(completed.BuildingId, CompletedSquash);
                break;
            case Sim.Core.BuildingUpgraded upgraded:
                PopOrLater(upgraded.BuildingId, UpgradedSquash);
                break;
        }
    }

    /// <summary>The red pulse on a building the player has just ordered an attack on.</summary>
    public void MarkTarget(int buildingId)
    {
        if (_views.TryGetValue(buildingId, out var view))
            view.MarkTarget();
    }

    private void PopOrLater(int buildingId, Vector2 squash)
    {
        if (_views.TryGetValue(buildingId, out var view))
            view.Pop(squash);
        else
            _pendingPops[buildingId] = squash;
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
                if (_pendingPops.Remove(building.Id, out var squash))
                    view.Pop(squash);
            }
            bool hidden = _match.IsHidden(building);
            if (!hidden && _seen.Add(building.Id))
            {
                // No flowers growing through its walls, and no trees: the site was cleared (shown only once it is seen,
                // so a clearing does not give away a hidden building).
                _match.Map.ClearDecor(building.Anchor, building.Type.Size);
                _match.Map.ClearWoods(building.Anchor, building.Type.Size);
            }
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

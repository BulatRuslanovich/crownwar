using System.Linq;
using Godot;
using Lordsio.Core;
using Lordsio.Sim.Core;
using Lordsio.Sim.Units;

namespace Lordsio.Interaction;

/// <summary>
/// Turns mouse input into selection changes and simulation commands:
/// left click selects a unit (or clears the selection), right click orders the selection to move.
/// </summary>
public partial class SelectionController : Node2D
{
    /// <summary>How close (in tiles) a click must be to a unit's feet to pick it.</summary>
    private const float PickRadius = 0.6f;
    private const float MarkerDuration = 0.45f;

    private static readonly Color MarkerColor = new("7cff6b");

    private MatchController _match = null!;
    private Vector2 _markerPosition;
    private float _markerTimeLeft;

    public void Initialize(MatchController match)
    {
        _match = match;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { Pressed: true } click)
            return;

        switch (click.ButtonIndex)
        {
            case MouseButton.Left:
                var unit = PickUnit(GetGlobalMousePosition());
                if (unit != null)
                    _match.Selection.Select(unit.Id);
                else
                    _match.Selection.Clear();
                GetViewport().SetInputAsHandled();
                break;

            case MouseButton.Right when _match.Selection.Count > 0:
                Vector2 world = GetGlobalMousePosition();
                _match.Sim.Issue(new MoveCommand(
                    MatchController.PlayerFactionId, _match.Selection.Ids.ToArray(), SimConvert.ToSim(world)));
                _markerPosition = world;
                _markerTimeLeft = MarkerDuration;
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    public override void _Process(double delta)
    {
        if (_markerTimeLeft <= 0f)
            return;
        _markerTimeLeft -= (float)delta;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_markerTimeLeft <= 0f)
            return;
        float t = _markerTimeLeft / MarkerDuration;
        DrawSetTransform(_markerPosition, 0f, new Vector2(1f, 0.6f));
        DrawArc(Vector2.Zero, 3f + 6f * t, 0f, Mathf.Tau, 20, MarkerColor with { A = t }, 1f);
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    private Unit? PickUnit(Vector2 worldPoint)
    {
        var point = SimConvert.ToSim(worldPoint);
        Unit? best = null;
        float bestDistSq = PickRadius * PickRadius;
        foreach (var unit in _match.Sim.Units)
        {
            if (unit.FactionId != MatchController.PlayerFactionId)
                continue;
            float distSq = System.Numerics.Vector2.DistanceSquared(unit.Position, point);
            if (distSq <= bestDistSq)
            {
                bestDistSq = distSq;
                best = unit;
            }
        }
        return best;
    }
}

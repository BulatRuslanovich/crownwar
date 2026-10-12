using Godot;
using Lordsio.Core;
using Lordsio.Sim.Buildings;
using Lordsio.Sim.Core;
using Lordsio.Sim.World;

namespace Lordsio.Interaction;

/// <summary>
/// Placement mode for a building: a ghost footprint follows the cursor, coloured by the simulation's own
/// <see cref="Simulation.CanBuild"/> check. LMB places (Shift keeps the mode for another one),
/// RMB or Esc cancels. Mines snap onto the deposit under the cursor.
/// Sits after <see cref="SelectionController"/> in the tree so it sees mouse input first.
/// </summary>
public partial class BuildPlacement : Node2D
{
    private static readonly Color Valid = new(0.45f, 1f, 0.4f, 0.35f);
    private static readonly Color Invalid = new(1f, 0.3f, 0.25f, 0.35f);
    private static readonly Color RadiusColor = new(1f, 1f, 1f, 0.12f);

    private MatchController _match = null!;
    private BuildingType? _type;
    private Cell _anchor;

    public void Initialize(MatchController match)
    {
        _match = match;
    }

    public bool IsActive => _type != null;
    public BuildingType? ActiveType => _type;

    /// <summary>Why the building cannot go where the cursor is, or None.</summary>
    public BuildError CurrentError { get; private set; }

    public void Begin(string buildingTypeId)
    {
        _type = _match.Sim.Rules.GetBuildingType(buildingTypeId);
        _match.Selection.Clear();
        QueueRedraw();
    }

    public void Cancel()
    {
        _type = null;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_type == null)
            return;
        _anchor = AnchorUnderCursor(_type);
        CurrentError = _match.Sim.CanBuild(MatchController.PlayerFactionId, _type.Id, _anchor);
        QueueRedraw();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_type == null)
            return;
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } left:
                // Always send the command: the simulation decides and explains a refusal.
                _match.Sim.Issue(new BuildCommand(MatchController.PlayerFactionId, _type.Id, _anchor));
                if (!left.ShiftPressed)
                    Cancel();
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true }:
            case InputEventKey { PhysicalKeycode: Key.Escape, Pressed: true }:
                Cancel();
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left }:
                GetViewport().SetInputAsHandled(); // swallow the release so it does not select anything
                break;
        }
    }

    public override void _Draw()
    {
        if (_type == null)
            return;

        int t = SimConvert.TileSize;
        var rect = new Rect2(_anchor.X * t, _anchor.Y * t, _type.Size * t, _type.Size * t);
        var color = CurrentError == BuildError.None ? Valid : Invalid;
        DrawRect(rect, color);
        DrawRect(rect, color with { A = 0.9f }, filled: false, width: 1f / _match.Camera.ZoomLevel);

        // Show how far from own units one may build, around each nearby own unit.
        float radius = _match.Sim.Rules.BuildRadius * t;
        var center = rect.GetCenter();
        foreach (var unit in _match.Sim.Units)
        {
            var p = SimConvert.ToWorld(unit.Position);
            if (unit.FactionId == MatchController.PlayerFactionId && p.DistanceTo(center) < radius * 3f)
                DrawArc(p, radius, 0f, Mathf.Tau, 48, RadiusColor, 1f / _match.Camera.ZoomLevel);
        }
    }

    private Cell AnchorUnderCursor(BuildingType type)
    {
        var cursor = Cell.FromPosition(SimConvert.ToSim(GetGlobalMousePosition()));
        if (type.IsMine && _match.Sim.DepositAt(cursor) is { } deposit)
            return deposit.Anchor;
        // Center the footprint on the cursor.
        int offset = (type.Size - 1) / 2;
        return new Cell(cursor.X - offset, cursor.Y - offset);
    }
}

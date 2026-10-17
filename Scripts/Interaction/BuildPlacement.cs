using Godot;
using Warcrown.Core;
using Warcrown.Sim.Buildings;
using Warcrown.Sim.Core;
using Warcrown.Sim.World;
using Warcrown.World;

namespace Warcrown.Interaction;

/// <summary>
/// Placement mode for a building: a ghost footprint follows the cursor, coloured by the simulation's own
/// <see cref="Simulation.CanBuild"/> check. LMB places (Shift keeps the mode for another one),
/// RMB or Esc cancels. Mines snap onto the deposit under the cursor. Where none of the player's troops stand, the ghost
/// turns gold and a line shows who will walk over to build it (the nearest free soldier, see
/// <see cref="Simulation.FindBuilder"/>). Buildings already ordered that way are drawn as faint pictures on their sites,
/// with a dashed line to their builder, until he arrives.
/// Sits after <see cref="SelectionController"/> in the tree so it sees mouse input first.
/// </summary>
public partial class BuildPlacement : Node2D
{
    private static readonly Color Valid = new(0.45f, 1f, 0.4f, 0.35f);
    private static readonly Color Invalid = new(1f, 0.3f, 0.25f, 0.35f);
    private static readonly Color RadiusColor = new(1f, 1f, 1f, 0.12f);
    private static readonly Color Planned = new(1f, 0.85f, 0.3f, 0.35f);
    private static readonly Color PlanLine = new(1f, 0.9f, 0.5f, 0.7f);

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

    /// <summary>Nobody stands near the cursor, but this soldier would walk over to build there.</summary>
    public Sim.Units.Unit? Builder { get; private set; }

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
        // Planned sites stay drawn while their builders walk.
        if (_type == null)
        {
            if (_match.Sim.BuildPlans.Count > 0 || _hadPlans)
                QueueRedraw();
            _hadPlans = _match.Sim.BuildPlans.Count > 0;
            return;
        }
        _anchor = AnchorUnderCursor(_type);
        CurrentError = _match.Sim.CanBuild(_match.LocalFactionId, _type.Id, _anchor);
        Builder = CurrentError == BuildError.NoUnitsNearby ? _match.Sim.FindBuilder(_match.LocalFactionId, _type.Id, _anchor) : null;
        QueueRedraw();
    }

    private bool _hadPlans;

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_type == null)
            return;
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } left:
                // Always send the command: the simulation decides and explains a refusal.
                _match.Issue(new BuildCommand(_match.LocalFactionId, _type.Id, _anchor));
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
        DrawPlans();
        if (_type == null)
            return;

        int t = SimConvert.TileSize;
        var rect = new Rect2(_anchor.X * t, _anchor.Y * t, _type.Size * t, _type.Size * t);
        var color = CurrentError == BuildError.None ? Valid : Builder != null ? Planned : Invalid;
        DrawRect(rect, color);
        DrawRect(rect, color with { A = 0.9f }, filled: false, width: 1f / _match.Camera.ZoomLevel);
        if (Builder != null)
            DrawDashedLine(SimConvert.ToWorld(Builder.Position), rect.GetCenter(), PlanLine, 1f / _match.Camera.ZoomLevel, 8f);

        // Show how far from own units one may build, around each nearby own unit.
        float radius = _match.Sim.Rules.BuildRadius * t;
        var center = rect.GetCenter();
        foreach (var unit in _match.Sim.Units)
        {
            var p = SimConvert.ToWorld(unit.Position);
            if (unit.FactionId == _match.LocalFactionId && p.DistanceTo(center) < radius * 3f)
                DrawArc(p, radius, 0f, Mathf.Tau, 48, RadiusColor, 1f / _match.Camera.ZoomLevel);
        }
    }

    /// <summary>The player's ordered buildings: a faint picture on the site and a dashed line from the builder.</summary>
    private void DrawPlans()
    {
        int t = SimConvert.TileSize;
        float width = 1f / _match.Camera.ZoomLevel;
        foreach (var plan in _match.Sim.BuildPlans)
        {
            if (plan.FactionId != _match.LocalFactionId)
                continue;
            var rect = new Rect2(plan.Anchor.X * t, plan.Anchor.Y * t, plan.Type.Size * t, plan.Type.Size * t);
            var picture = GD.Load<Texture2D>(Sprites.Path(plan.Type.Id));
            var size = new Vector2(picture.GetWidth() / (float)ArtLayout.BuildingFrames, picture.GetHeight());
            var bottom = new Vector2(rect.GetCenter().X, rect.End.Y);
            DrawTextureRectRegion(picture, new Rect2(bottom - new Vector2(size.X / 2f, size.Y), size), new Rect2(Vector2.Zero, size),
                new Color(1f, 1f, 1f, 0.4f));
            DrawRect(rect, Planned with { A = 0.8f }, filled: false, width: width);
            if (_match.Sim.GetUnit(plan.BuilderId) is { } builder)
                DrawDashedLine(SimConvert.ToWorld(builder.Position), rect.GetCenter(), PlanLine, width, 8f);
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

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Lordsio.Core;
using Lordsio.Sim.Core;
using Lordsio.Sim.Buildings;
using Lordsio.Sim.Units;

namespace Lordsio.Interaction;

/// <summary>
/// Turns mouse and keyboard input into selection changes and simulation commands.
/// LMB click / drag: select a unit / box-select (Shift adds, Shift+click toggles);
/// double click: all units of that type on screen; Ctrl+A: all own units;
/// RMB: advance / attack the clicked enemy; Ctrl+RMB: retreat ignoring enemies.
/// </summary>
public partial class SelectionController : Node2D
{
    /// <summary>How close (in tiles) a click must be to a unit's feet to pick it.</summary>
    private const float PickRadius = 0.6f;
    /// <summary>Screen pixels the mouse must travel before a click becomes a box selection.</summary>
    private const float DragThreshold = 5f;
    private const float MarkerDuration = 0.45f;

    private static readonly Color MarkerColor = new("7cff6b");
    private static readonly Color AttackMarkerColor = new("ff5a4a");
    private static readonly Color RetreatMarkerColor = new("8ec5ff");
    private static readonly Color BoxFill = new(0.48f, 1f, 0.42f, 0.12f);
    private static readonly Color BoxOutline = new(0.48f, 1f, 0.42f, 0.9f);

    private MatchController _match = null!;
    private Vector2 _markerPosition;
    private Color _markerColor = MarkerColor;
    private float _markerTimeLeft;

    private bool _leftDown;
    private bool _boxActive;
    private Vector2 _pressScreen;
    private Vector2 _pressWorld;

    public void Initialize(MatchController match)
    {
        _match = match;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } left:
                OnLeftButton(left);
                GetViewport().SetInputAsHandled();
                break;

            case InputEventMouseMotion motion when _leftDown:
                if (!_boxActive && motion.Position.DistanceTo(_pressScreen) > DragThreshold)
                    _boxActive = true;
                if (_boxActive)
                    QueueRedraw();
                break;

            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } right when _match.Selection.Count > 0:
                IssueOrder(GetGlobalMousePosition(), retreat: right.CtrlPressed);
                GetViewport().SetInputAsHandled();
                break;

            case InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.A, CtrlPressed: true }:
                _match.Selection.Select(OwnUnits().Select(u => u.Id), additive: false);
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    private void OnLeftButton(InputEventMouseButton e)
    {
        bool additive = e.ShiftPressed;
        if (e.Pressed)
        {
            if (e.DoubleClick)
            {
                SelectSameTypeOnScreen(additive);
                _leftDown = false;
                return;
            }
            _leftDown = true;
            _boxActive = false;
            _pressScreen = e.Position;
            _pressWorld = GetGlobalMousePosition();
            return;
        }

        if (!_leftDown)
            return; // release of a double click, already handled
        _leftDown = false;

        if (_boxActive)
        {
            _boxActive = false;
            QueueRedraw();
            var box = new Rect2(_pressWorld, Vector2.Zero).Expand(GetGlobalMousePosition());
            _match.Selection.Select(OwnUnits().Where(u => box.HasPoint(SimConvert.ToWorld(u.Position))).Select(u => u.Id), additive);
            return;
        }

        var unit = PickUnit(GetGlobalMousePosition());
        if (unit == null)
        {
            var building = PickBuilding(GetGlobalMousePosition());
            if (building != null)
                _match.Selection.SelectBuilding(building.Id);
            else if (!additive)
                _match.Selection.Clear();
        }
        else if (additive)
        {
            _match.Selection.Toggle(unit.Id);
        }
        else
        {
            _match.Selection.Select(unit.Id);
        }
    }

    private void SelectSameTypeOnScreen(bool additive)
    {
        var clicked = PickUnit(GetGlobalMousePosition());
        if (clicked == null)
            return;
        var visible = VisibleWorldRect();
        _match.Selection.Select(OwnUnits()
            .Where(u => u.Type == clicked.Type && visible.HasPoint(SimConvert.ToWorld(u.Position)))
            .Select(u => u.Id), additive);
    }

    /// <summary>
    /// Right click: on an enemy unit or building — attack it; on the ground — advance, fighting
    /// whatever is met; with Ctrl — retreat, ignoring enemies.
    /// </summary>
    private void IssueOrder(Vector2 world, bool retreat)
    {
        int player = MatchController.PlayerFactionId;
        int[] ids = _match.Selection.Ids.ToArray();
        Entity? enemy = null;
        if (!retreat)
        {
            enemy = PickUnit(world, EnemyUnits());
            if (enemy == null && PickBuilding(world) is { } building && building.FactionId != player)
                enemy = building;
        }
        if (enemy != null)
        {
            _match.Sim.Issue(new AttackCommand(player, ids, enemy.Id));
            _markerPosition = SimConvert.ToWorld(enemy.Position);
            _markerColor = AttackMarkerColor;
        }
        else
        {
            var mode = retreat ? MoveMode.Move : MoveMode.AttackMove;
            _match.Sim.Issue(new MoveCommand(player, ids, SimConvert.ToSim(world), mode));
            _markerPosition = world;
            _markerColor = retreat ? RetreatMarkerColor : MarkerColor;
        }
        _markerTimeLeft = MarkerDuration;
    }

    public override void _Process(double delta)
    {
        // The box is anchored in the world, so it must follow the camera even without mouse motion.
        if (_boxActive)
            QueueRedraw();
        if (_markerTimeLeft <= 0f)
            return;
        _markerTimeLeft -= (float)delta;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_boxActive)
        {
            var box = new Rect2(_pressWorld, Vector2.Zero).Expand(GetGlobalMousePosition());
            DrawRect(box, BoxFill);
            // Width 1 / zoom keeps the outline one screen pixel thick at every zoom level.
            DrawRect(box, BoxOutline, filled: false, width: 1f / _match.Camera.ZoomLevel);
        }

        if (_markerTimeLeft > 0f)
        {
            float t = _markerTimeLeft / MarkerDuration;
            DrawSetTransform(_markerPosition, 0f, new Vector2(1f, 0.6f));
            DrawArc(Vector2.Zero, 3f + 6f * t, 0f, Mathf.Tau, 20, _markerColor with { A = t }, 1f);
            DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        }
    }

    private IEnumerable<Unit> OwnUnits() =>
        _match.Sim.Units.Where(u => u.FactionId == MatchController.PlayerFactionId);

    private IEnumerable<Unit> EnemyUnits() =>
        _match.Sim.Units.Where(u => u.FactionId != MatchController.PlayerFactionId);

    private Rect2 VisibleWorldRect()
    {
        var inverse = GetCanvasTransform().AffineInverse();
        return new Rect2(inverse * Vector2.Zero, Vector2.Zero).Expand(inverse * GetViewportRect().Size);
    }

    /// <summary>
    /// Building under the point: its footprint, plus the part of the drawing that rises above it
    /// (about one footprint higher, since buildings are drawn tall).
    /// </summary>
    private Building? PickBuilding(Vector2 worldPoint)
    {
        var p = SimConvert.ToSim(worldPoint);
        foreach (var building in _match.Sim.Buildings)
        {
            float half = building.Type.Size / 2f;
            var c = building.Position;
            if (MathF.Abs(p.X - c.X) <= half && p.Y <= c.Y + half && p.Y >= c.Y - half - building.Type.Size * 0.8f)
                return building;
        }
        return null;
    }

    private Unit? PickUnit(Vector2 worldPoint) => PickUnit(worldPoint, OwnUnits());

    private static Unit? PickUnit(Vector2 worldPoint, IEnumerable<Unit> candidates)
    {
        var point = SimConvert.ToSim(worldPoint);
        Unit? best = null;
        float bestDistSq = PickRadius * PickRadius;
        foreach (var unit in candidates)
        {
            // Units are drawn upwards from their feet, so aim slightly above the position too.
            float distSq = MathF.Min(
                System.Numerics.Vector2.DistanceSquared(unit.Position, point),
                System.Numerics.Vector2.DistanceSquared(unit.Position - new System.Numerics.Vector2(0f, 0.4f), point));
            if (distSq <= bestDistSq)
            {
                bestDistSq = distSq;
                best = unit;
            }
        }
        return best;
    }
}

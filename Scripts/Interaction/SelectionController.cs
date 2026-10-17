using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Warcrown.Core;
using Warcrown.Sim.Buildings;
using Warcrown.Sim.Core;
using Warcrown.Sim.Units;
using Warcrown.UI;
using Warcrown.World;

namespace Warcrown.Interaction;

/// <summary>
/// Turns mouse and keyboard input into selection changes and simulation commands.
/// LMB click / drag: select a unit / box-select (Shift adds, Shift+click toggles);
/// double click: all units of that type on screen; Ctrl+A: all own units;
/// RMB: go there no matter what (the order beats any fight) / attack the clicked enemy;
/// Ctrl+RMB: advance, fighting whatever is met on the way.
/// </summary>
public partial class SelectionController : Node2D
{
    /// <summary>How close (in tiles) a click must be to a unit's feet to pick it.</summary>
    private const float PickRadius = 0.6f;
    /// <summary>Screen pixels the mouse must travel before a click becomes a box selection.</summary>
    private const float DragThreshold = 5f;
    private const float MarkerDuration = 0.45f;

    private static readonly Color MarkerColor = UiPalette.Friendly;
    private static readonly Color AttackMarkerColor = UiPalette.Hostile;
    private static readonly Color RallyColor = new("ffd84a");
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

    private bool _attackCursor;

    public void Initialize(MatchController match)
    {
        _match = match;
    }

    public override void _ExitTree() => Input.SetDefaultCursorShape(Input.CursorShape.Arrow);

    /// <summary>The sword cursor: troops are selected and a right click here would attack someone (or advance with Ctrl).</summary>
    private void UpdateCursor()
    {
        bool attack = false;
        if (_match.Selection.Count > 0)
        {
            var world = GetGlobalMousePosition();
            attack = Input.IsKeyPressed(Key.Ctrl)
                     || PickUnit(world, EnemyUnits()) != null
                     || PickBuilding(world) is { } building && _match.Sim.AreEnemies(_match.LocalFactionId, building.FactionId) && !_match.IsHidden(building);
        }
        if (attack == _attackCursor)
            return;
        _attackCursor = attack;
        Input.SetDefaultCursorShape(attack ? UI.Cursors.Attack : Input.CursorShape.Arrow);
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

            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } when _match.SelectedRallyBuilding() is { } producer:
                SetRally(producer, GetGlobalMousePosition());
                GetViewport().SetInputAsHandled();
                break;

            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } right when _match.Selection.Count > 0:
                IssueOrder(GetGlobalMousePosition(), advance: right.CtrlPressed);
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
            _match.Selection.Select(OwnUnits().Where(u => box.HasPoint(SimConvert.ToWorld(u.Position))
                || box.HasPoint(SimConvert.ToWorld(u.Position) - new Vector2(0, ArtLayout.FlightAltitude(u.Type.Id)))).Select(u => u.Id), additive);
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
        var visible = _match.Camera.VisibleRect;
        _match.Selection.Select(OwnUnits()
            .Where(u => u.Type.Id == clicked.Type.Id && visible.HasPoint(SimConvert.ToWorld(u.Position)))
            .Select(u => u.Id), additive);
    }

    /// <summary>
    /// Right click: on an enemy unit or building — attack it; on the ground — go there, dropping any fight
    /// (the player's order comes first: this is how troops are pulled back); with Ctrl — advance, fighting
    /// whatever is met.
    /// </summary>
    private void IssueOrder(Vector2 world, bool advance)
    {
        int player = _match.LocalFactionId;
        int[] ids = _match.Selection.Ids.ToArray();
        Entity? enemy = null;
        if (!advance)
        {
            enemy = PickUnit(world, EnemyUnits());
            if (enemy == null && PickBuilding(world) is { } building && _match.Sim.AreEnemies(player, building.FactionId)
                && !_match.IsHidden(building))
                enemy = building;
        }
        if (enemy != null)
        {
            _match.Issue(new AttackCommand(player, ids, enemy.Id));
            _match.AcknowledgeAttack(enemy.Id);
            _markerPosition = SimConvert.ToWorld(enemy.Position);
            _markerColor = AttackMarkerColor;
        }
        else
        {
            var mode = advance ? MoveMode.AttackMove : MoveMode.Move;
            _match.Issue(new MoveCommand(player, ids, SimConvert.ToSim(world), mode));
            _match.AcknowledgeMove();
            _markerPosition = world;
            _markerColor = advance ? AttackMarkerColor : MarkerColor;
        }
        _markerTimeLeft = MarkerDuration;
    }

    /// <summary>Right click with a castle or barracks selected: new recruits walk there; on the building itself, they stay put.</summary>
    private void SetRally(Building producer, Vector2 world)
    {
        bool onItself = PickBuilding(world)?.Id == producer.Id;
        _match.Issue(new SetRallyCommand(_match.LocalFactionId, producer.Id, onItself ? null : SimConvert.ToSim(world)));
        _markerPosition = onItself ? SimConvert.ToWorld(producer.Position) : world;
        _markerColor = RallyColor;
        _markerTimeLeft = MarkerDuration;
    }

    public override void _Process(double delta)
    {
        UpdateCursor();
        if (_match.SelectedRallyBuilding() != null)
            QueueRedraw(); // the rally flag follows the selection
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
        // The selected castle's or barracks' rally point: a dashed line from the door and a small flag.
        if (_match.SelectedRallyBuilding() is { RallyPoint: { } rally } producer)
        {
            var from = SimConvert.ToWorld(producer.Position);
            var to = SimConvert.ToWorld(rally);
            DrawDashedLine(from, to, RallyColor with { A = 0.7f }, 1f / _match.Camera.ZoomLevel, 8f);
            DrawLine(to, to + new Vector2(0, -24), new Color("4a3a2a"), 2f);
            DrawColoredPolygon(new[] { to + new Vector2(1, -24), to + new Vector2(15, -19), to + new Vector2(1, -14) }, RallyColor);
            DrawColoredPolygon(new[] { to + new Vector2(1, -24), to + new Vector2(15, -19), to + new Vector2(1, -21) }, RallyColor.Lightened(0.3f));
        }

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
            DrawArc(Vector2.Zero, 6f + 12f * t, 0f, Mathf.Tau, 28, _markerColor with { A = t }, 2f);
            DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        }
    }

    private IEnumerable<Unit> OwnUnits() =>
        _match.Sim.Units.Where(u => u.FactionId == _match.LocalFactionId);

    private IEnumerable<Unit> EnemyUnits() =>
        _match.Sim.Units.Where(u => _match.Sim.AreEnemies(_match.LocalFactionId, u.FactionId) && !_match.IsHidden(u));

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
            // Units are drawn upwards from their feet, so aim slightly above the position too (a flyer: up where it flies).
            var body = unit.Position - new System.Numerics.Vector2(0f, ArtLayout.FlightAltitude(unit.Type.Id) / SimConvert.TileSize);
            float distSq = MathF.Min(
                System.Numerics.Vector2.DistanceSquared(body, point),
                System.Numerics.Vector2.DistanceSquared(body - new System.Numerics.Vector2(0f, 0.4f), point));
            if (distSq <= bestDistSq)
            {
                bestDistSq = distSq;
                best = unit;
            }
        }
        return best;
    }
}

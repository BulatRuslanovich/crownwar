using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Godot;
using Lordsio.Core;
using Lordsio.Sim.Units;
using Lordsio.Sim.World;

namespace Lordsio.UI;

/// <summary>Developer overlay: frame/tick info, the tile under the cursor and the selected unit's state.</summary>
public partial class DebugHud : CanvasLayer
{
    private readonly StringBuilder _text = new();
    private MatchController _match = null!;
    private Label _label = null!;

    public void Initialize(MatchController match)
    {
        _match = match;
        _label = GetNode<Label>("Panel/Label");
    }

    public override void _Process(double delta)
    {
        var sim = _match.Sim;
        var inv = CultureInfo.InvariantCulture;
        _text.Clear();
        _text.Append(inv, $"FPS {Engine.GetFramesPerSecond():0}   tick {sim.Tick} ({_match.StepMilliseconds:0.00} ms)   ");
        _text.Append(inv, $"units {sim.Units.Count}   zoom {_match.Camera.ZoomLevel:0.#}x\n");

        var cell = Cell.FromPosition(SimConvert.ToSim(_match.GetGlobalMousePosition()));
        if (sim.Map.InBounds(cell))
        {
            int region = sim.Map.ComponentOf(cell);
            string walk = region >= 0 ? $"walkable, region {region}" : "blocked";
            _text.Append(inv, $"Cursor {cell}: {sim.Map.GetTerrain(cell)}, {walk}\n");
        }
        else
        {
            _text.Append("Cursor: outside the map\n");
        }

        if (_match.Selection.Count == 1)
            AppendUnitDetails(sim.GetUnit(_match.Selection.Ids.First()));
        else if (_match.Selection.Count > 1)
            AppendGroupSummary();

        _text.Append("\nLMB select / drag box  ·  Shift add  ·  double click same type  ·  Ctrl+A all\n");
        _text.Append("RMB advance (attacks on the way) / attack enemy  ·  Ctrl+RMB retreat\n");
        _text.Append("WASD / arrows / screen edge pan  ·  MMB drag  ·  wheel zoom");
        _label.Text = _text.ToString();
    }

    private void AppendUnitDetails(Unit? unit)
    {
        if (unit == null)
            return;
        var inv = CultureInfo.InvariantCulture;
        int waypoints = unit.Path.Count - unit.PathIndex;
        _text.Append(inv, $"\nUnit #{unit.Id} {unit.Type.Name}  faction {unit.FactionId}\n");
        _text.Append(inv, $"  health {unit.Health}/{unit.Type.MaxHealth}, state {unit.State}, order {unit.Order} ({unit.LastOrder})\n");
        if (unit.Target is { } target)
        {
            string name = target switch { Unit u => u.Type.Name, Sim.Buildings.Building b => b.Type.Name, _ => "?" };
            _text.Append(inv, $"  target #{target.Id} {name} of faction {target.FactionId}\n");
        }
        _text.Append(inv, $"  cell {Cell.FromPosition(unit.Position)}  pos ({unit.Position.X:0.00}, {unit.Position.Y:0.00})\n");
        _text.Append(inv, $"  path: {waypoints} waypoints, {unit.RemainingPathLength:0.0} tiles left\n");
    }

    private void AppendGroupSummary()
    {
        var units = _match.Selection.Ids.Select(_match.Sim.GetUnit).OfType<Unit>().ToList();
        _text.Append(CultureInfo.InvariantCulture, $"\nSelected {units.Count} units\n");
        _text.Append("  state: ").Append(CountBy(units, u => u.State.ToString())).Append('\n');
        _text.Append("  last order: ").Append(CountBy(units, u => u.LastOrder.ToString())).Append('\n');
        _text.Append("  type: ").Append(CountBy(units, u => u.Type.Name)).Append('\n');
    }

    private static string CountBy(List<Unit> units, Func<Unit, string> key) =>
        string.Join(", ", units.GroupBy(key).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count()}"));
}

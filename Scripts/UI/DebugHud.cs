using System.Globalization;
using System.Text;
using Godot;
using Lordsio.Core;
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
        _text.Append(inv, $"FPS {Engine.GetFramesPerSecond():0}   tick {sim.Tick}   zoom {_match.Camera.ZoomLevel:0.#}x\n");

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

        foreach (int id in _match.Selection.Ids)
        {
            if (sim.GetUnit(id) is not { } unit)
                continue;
            int waypoints = unit.Path.Count - unit.PathIndex;
            _text.Append(inv, $"\nUnit #{unit.Id} {unit.Type.Name}  faction {unit.FactionId}\n");
            _text.Append(inv, $"  state {unit.State}, last order {unit.LastOrder}\n");
            _text.Append(inv, $"  cell {Cell.FromPosition(unit.Position)}  pos ({unit.Position.X:0.00}, {unit.Position.Y:0.00})\n");
            _text.Append(inv, $"  path: {waypoints} waypoints, {unit.RemainingPathLength:0.0} tiles left\n");
        }

        _text.Append("\nLMB select  ·  RMB move  ·  WASD / arrows / screen edge pan  ·  MMB drag  ·  wheel zoom");
        _label.Text = _text.ToString();
    }
}

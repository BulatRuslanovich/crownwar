using System;
using System.Diagnostics;
using Godot;
using Lordsio.Interaction;
using Lordsio.Sim.Core;
using Lordsio.Sim.Units;
using Lordsio.Sim.World;
using Lordsio.UI;
using Lordsio.Units;
using Lordsio.World;

namespace Lordsio.Core;

/// <summary>
/// Owns the simulation for one match, advances it in fixed ticks and wires up the view nodes.
/// </summary>
public partial class MatchController : Node2D
{
    public const int PlayerFactionId = 0;

    /// <summary>Upper bound on catch-up ticks per frame so a long hitch does not snowball.</summary>
    private const int MaxStepsPerFrame = 5;

    [Export(PropertyHint.File, "*.txt")] public string MapPath { get; set; } = "res://Maps/default.txt";
    [Export(PropertyHint.File, "*.json")] public string UnitsPath { get; set; } = "res://Data/units.json";

    private double _accumulator;

    public Simulation Sim { get; private set; } = null!;
    public Selection Selection { get; } = new();
    public CameraController Camera { get; private set; } = null!;

    /// <summary>Smoothed wall-clock cost of one simulation tick, for the debug overlay.</summary>
    public double StepMilliseconds { get; private set; }

    /// <summary>Fraction of the way from the previous tick to the next one, for smooth rendering.</summary>
    public float InterpolationAlpha => (float)(_accumulator / Simulation.TickDuration);

    public override void _Ready()
    {
        var map = MapLoader.Parse(ReadText(MapPath));
        var catalog = UnitCatalog.Parse(ReadText(UnitsPath));
        Sim = new Simulation(map, catalog);

        Camera = GetNode<CameraController>("Camera");
        var worldSize = new Vector2(Sim.Map.Width, Sim.Map.Height) * SimConvert.TileSize;
        Camera.SetBounds(new Rect2(Vector2.Zero, worldSize).Grow(SimConvert.TileSize * 2));
        var firstUnit = Sim.Units.Count > 0 ? Sim.Units[0] : null;
        Camera.CenterOn(firstUnit != null ? SimConvert.ToWorld(firstUnit.Position) : worldSize / 2);

        GetNode<MapView>("MapView").Build(Sim.Map);
        GetNode<UnitsView>("UnitsView").Initialize(this);
        GetNode<SelectionController>("SelectionController").Initialize(this);
        GetNode<DebugHud>("DebugHud").Initialize(this);
    }

    public override void _Process(double delta)
    {
        _accumulator += delta;
        int steps = 0;
        while (_accumulator >= Simulation.TickDuration && steps < MaxStepsPerFrame)
        {
            long start = Stopwatch.GetTimestamp();
            Sim.Step();
            double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            StepMilliseconds = StepMilliseconds * 0.9 + ms * 0.1;
            _accumulator -= Simulation.TickDuration;
            steps++;
        }
        if (_accumulator >= Simulation.TickDuration)
            _accumulator = 0; // drop the backlog after a hitch instead of fast-forwarding
    }

    private static string ReadText(string path)
    {
        string text = FileAccess.GetFileAsString(path);
        if (string.IsNullOrEmpty(text))
            throw new InvalidOperationException($"Cannot read '{path}': {FileAccess.GetOpenError()}");
        return text;
    }
}

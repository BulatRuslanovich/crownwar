using System;
using System.Diagnostics;
using Godot;
using Lordsio.Interaction;
using Lordsio.Sim.Core;
using Lordsio.Sim.World;
using Lordsio.Buildings;
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
    [Export(PropertyHint.File, "*.json")] public string RulesPath { get; set; } = "res://Data/rules.json";

    /// <summary>Player plus opponents; each gets a capital on a random start position.</summary>
    [Export(PropertyHint.Range, "1,8")] public int FactionCount { get; set; } = 2;

    /// <summary>Seed for start positions; negative picks a new one every match (overridable with --seed=N).</summary>
    [Export] public int Seed { get; set; } = -1;

    private double _accumulator;
    private Hud _hud = null!;

    public Simulation Sim { get; private set; } = null!;
    public int ActualSeed { get; private set; }
    public Selection Selection { get; } = new();
    public CameraController Camera { get; private set; } = null!;

    /// <summary>Smoothed wall-clock cost of one simulation tick, for the debug overlay.</summary>
    public double StepMilliseconds { get; private set; }

    /// <summary>Fraction of the way from the previous tick to the next one, for smooth rendering.</summary>
    public float InterpolationAlpha => (float)(_accumulator / Simulation.TickDuration);

    public override void _Ready()
    {
        var map = MapLoader.Parse(ReadText(MapPath));
        var rules = GameRules.Parse(ReadText(RulesPath));
        ActualSeed = SeedFromCommandLine() ?? (Seed >= 0 ? Seed : (int)(GD.Randi() & int.MaxValue));
        Sim = new Simulation(map, rules, new MatchSetup(FactionCount, ActualSeed));

        Camera = GetNode<CameraController>("Camera");
        var worldSize = new Vector2(Sim.Map.Width, Sim.Map.Height) * SimConvert.TileSize;
        Camera.SetBounds(new Rect2(Vector2.Zero, worldSize).Grow(SimConvert.TileSize * 2));
        Camera.CenterOn(StartingFocus() ?? worldSize / 2);

        GetNode<MapView>("MapView").Build(Sim.Map);
        GetNode<DepositsView>("DepositsView").Initialize(this);
        GetNode<BuildingsView>("Actors/BuildingsView").Initialize(this);
        GetNode<UnitsView>("Actors/UnitsView").Initialize(this);
        GetNode<ProjectilesView>("ProjectilesView").Initialize(this);
        GetNode<SelectionController>("SelectionController").Initialize(this);
        var placement = GetNode<BuildPlacement>("BuildPlacement");
        placement.Initialize(this);
        GetNode<DebugHud>("DebugHud").Initialize(this);
        _hud = GetNode<Hud>("Hud");
        _hud.Initialize(this, placement);
    }

    /// <summary>The player's capital, else their first unit.</summary>
    private Vector2? StartingFocus()
    {
        if (Sim.GetFaction(PlayerFactionId) is { } player && Sim.GetBuilding(player.CapitalId) is { } capital)
            return SimConvert.ToWorld(capital.Position);
        foreach (var unit in Sim.Units)
            if (unit.FactionId == PlayerFactionId)
                return SimConvert.ToWorld(unit.Position);
        return null;
    }

    private static int? SeedFromCommandLine()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--seed=", StringComparison.Ordinal)
                && int.TryParse(arg["--seed=".Length..], System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out int seed))
                return seed;
        }
        return null;
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

        foreach (var e in Sim.TakeEvents())
            _hud.OnSimEvent(e);
    }

    private static string ReadText(string path)
    {
        string text = FileAccess.GetFileAsString(path);
        if (string.IsNullOrEmpty(text))
            throw new InvalidOperationException($"Cannot read '{path}': {FileAccess.GetOpenError()}");
        return text;
    }
}

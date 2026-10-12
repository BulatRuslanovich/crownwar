using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;
using Lordsio.Audio;
using Lordsio.Interaction;
using Lordsio.Sim.AI;
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

    private double _accumulator;
    private Hud _hud = null!;
    private EndScreen _endScreen = null!;
    private PauseMenu _pauseMenu = null!;
    private UnitsView _unitsView = null!;
    private EffectsView _groundEffects = null!;
    private EffectsView _airEffects = null!;
    private MatchSounds _sounds = null!;
    private readonly AttackAlerts _alerts = new(PlayerFactionId);

    private readonly List<BotBrain> _bots = new();

    /// <summary>Set by <see cref="Main"/> before the match enters the tree; defaults when the scene runs alone.</summary>
    public MatchConfig Config { get; set; } = MatchConfig.Default;

    /// <summary>Raised when the player asks for a new match with the same settings.</summary>
    public event Action? RestartRequested;

    /// <summary>Raised when the player leaves to the main menu.</summary>
    public event Action? MenuRequested;

    public Simulation Sim { get; private set; } = null!;
    public MatchStats Stats { get; } = new(PlayerFactionId);
    public int ActualSeed { get; private set; }
    public IReadOnlyList<BotBrain> Bots => _bots;
    public Selection Selection { get; } = new();
    public CameraController Camera { get; private set; } = null!;

    /// <summary>Smoothed wall-clock cost of one simulation tick plus bot decisions, for the debug overlay.</summary>
    public double StepMilliseconds { get; private set; }

    /// <summary>Fraction of the way from the previous tick to the next one, for smooth rendering.</summary>
    public float InterpolationAlpha => (float)(_accumulator / Simulation.TickDuration);

    public override void _Ready()
    {
        var map = MapLoader.Parse(ReadText(MapPath));
        var rules = GameRules.Parse(ReadText(RulesPath));
        ActualSeed = Config.Seed ?? (int)(GD.Randi() & int.MaxValue);
        Sim = new Simulation(map, rules, new MatchSetup(1 + Config.BotCount, ActualSeed));
        foreach (var faction in Sim.Factions)
        {
            if (faction.Id != PlayerFactionId)
                _bots.Add(new BotBrain(Sim, faction.Id, Config.Difficulty, ActualSeed + faction.Id));
        }

        Camera = GetNode<CameraController>("Camera");
        var worldSize = new Vector2(Sim.Map.Width, Sim.Map.Height) * SimConvert.TileSize;
        Camera.SetBounds(new Rect2(Vector2.Zero, worldSize).Grow(SimConvert.TileSize * 2));
        Camera.CenterOn(StartingFocus() ?? worldSize / 2);

        GetNode<MapView>("MapView").Build(Sim.Map);
        GetNode<DepositsView>("DepositsView").Initialize(this);
        GetNode<BuildingsView>("Actors/BuildingsView").Initialize(this);
        _unitsView = GetNode<UnitsView>("Actors/UnitsView");
        _unitsView.Initialize(this);
        GetNode<ProjectilesView>("ProjectilesView").Initialize(this);
        _groundEffects = GetNode<EffectsView>("GroundEffects");
        _groundEffects.Initialize(this);
        _airEffects = GetNode<EffectsView>("AirEffects");
        _airEffects.Initialize(this);
        _sounds = GetNode<MatchSounds>("Sounds");
        _sounds.Initialize(this);
        GetNode<SelectionController>("SelectionController").Initialize(this);
        var placement = GetNode<BuildPlacement>("BuildPlacement");
        placement.Initialize(this);
        GetNode<DebugHud>("DebugHud").Initialize(this);
        _hud = GetNode<Hud>("Hud");
        _hud.Initialize(this, placement);
        _endScreen = GetNode<EndScreen>("EndScreen");
        _endScreen.Initialize(this);
        _pauseMenu = GetNode<PauseMenu>("PauseMenu");
        _pauseMenu.Initialize(this, placement);

        _alerts.Alerted += position =>
        {
            _hud.ShowAlert(position);
            _airEffects.Ping(position);
            _sounds.PlayAlert();
        };
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // Space jumps the camera to the latest attack on the player.
        if (@event is InputEventKey { PhysicalKeycode: Key.Space, Pressed: true, Echo: false }
            && _alerts.LastPosition is { } position)
        {
            Camera.CenterOn(SimConvert.ToWorld(position));
            GetViewport().SetInputAsHandled();
        }
    }

    public bool IsPaused => GetTree().Paused;

    /// <summary>Freezes the match (simulation, camera, input) while menus stay responsive.</summary>
    public void SetPaused(bool paused)
    {
        GetTree().Paused = paused;
        _pauseMenu.Visible = paused;
    }

    /// <summary>Dev-only: shows the end screen as if the match had ended, without touching the simulation.</summary>
    public void PreviewEndScreen(bool victory) =>
        _endScreen.OnSimEvent(new MatchEnded(victory ? PlayerFactionId : PlayerFactionId + 1));

    public void RequestRestart() => RestartRequested?.Invoke();

    public void RequestMenu() => MenuRequested?.Invoke();

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

    public override void _Process(double delta)
    {
        _accumulator += delta;
        int steps = 0;
        while (_accumulator >= Simulation.TickDuration && steps < MaxStepsPerFrame)
        {
            long start = Stopwatch.GetTimestamp();
            Sim.Step();
            // Bots decide right after a tick, like a player reacting to what they see.
            foreach (var bot in _bots)
                bot.Update();
            double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            StepMilliseconds = StepMilliseconds * 0.9 + ms * 0.1;
            _accumulator -= Simulation.TickDuration;
            steps++;
        }
        if (_accumulator >= Simulation.TickDuration)
            _accumulator = 0; // drop the backlog after a hitch instead of fast-forwarding

        foreach (var e in Sim.TakeEvents())
        {
            Stats.OnSimEvent(e);
            _hud.OnSimEvent(e);
            _unitsView.OnSimEvent(e);
            _groundEffects.OnSimEvent(e);
            _airEffects.OnSimEvent(e);
            _sounds.OnSimEvent(e);
            _alerts.OnSimEvent(e, Sim.Tick);
            _endScreen.OnSimEvent(e);
        }
    }

    private static string ReadText(string path)
    {
        string text = FileAccess.GetFileAsString(path);
        if (string.IsNullOrEmpty(text))
            throw new InvalidOperationException($"Cannot read '{path}': {FileAccess.GetOpenError()}");
        return text;
    }
}

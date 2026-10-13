using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using Lordsio.Audio;
using Lordsio.Interaction;
using Lordsio.Net;
using Lordsio.Sim.AI;
using Lordsio.Sim.Core;
using Lordsio.Sim.Net;
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
    /// <summary>The faction this machine controls (always 0 in single player).</summary>
    public int LocalFactionId { get; private set; }

    /// <summary>The local player's team: allies share it.</summary>
    public int LocalTeam => Sim.GetFaction(LocalFactionId)?.Team ?? LocalFactionId;

    /// <summary>
    /// Sends a player command. All UI goes through here instead of calling the simulation directly,
    /// so a networked match can route commands through the lockstep session.
    /// </summary>
    public void Issue(Command command)
    {
        if (Net != null)
            Net.QueueLocal(command);
        else
            Sim.Issue(command);
    }

    /// <summary>Set by <see cref="Main"/> for network games before the match enters the tree; null in single player.</summary>
    public NetMatchDriver? Net { get; set; }

    /// <summary>A networked match is stalled until another player's commands arrive.</summary>
    public bool WaitingForPlayers { get; private set; }

    /// <summary>
    /// Frames buffered further ahead than the input delay mean we fell behind the others: run ticks without
    /// waiting for real time. (Up to the input delay is normal: our own inputs are sent that far ahead.)
    /// </summary>
    private const int LockstepCatchUpThreshold = LockstepHost.InputDelay + 2;

    /// <summary>Hands a faction to the computer (bot slots at start, or a player who left).</summary>
    public BotBrain AddBot(int factionId, BotDifficulty difficulty)
    {
        var bot = new BotBrain(Sim, factionId, difficulty, ActualSeed + factionId);
        _bots.Add(bot);
        return bot;
    }

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
    private AttackAlerts _alerts = null!;

    private readonly List<BotBrain> _bots = new();

    /// <summary>Set by <see cref="Main"/> before the match enters the tree; defaults when the scene runs alone.</summary>
    public MatchConfig Config { get; set; } = MatchConfig.Default;

    /// <summary>Raised when the player asks for a new match with the same settings.</summary>
    public event Action? RestartRequested;

    /// <summary>Raised when the player leaves to the main menu.</summary>
    public event Action? MenuRequested;

    public Simulation Sim { get; private set; } = null!;

    /// <summary>What the local team sees; enemies outside it are not drawn, heard or clickable.</summary>
    public Visibility Fog { get; private set; } = null!;
    private int _fogTick = -1;
    public MatchStats Stats { get; private set; } = null!;
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
        LocalFactionId = Config.LocalFactionId;
        Sim = new Simulation(map, rules, new MatchSetup(Config.Slots.Count, ActualSeed, Teams: Config.Teams));
        Stats = new MatchStats(LocalFactionId);
        Fog = new Visibility(Sim.Map.Width, Sim.Map.Height);
        UpdateFog();
        _alerts = new AttackAlerts(LocalFactionId);
        for (int i = 0; i < Config.Slots.Count; i++)
        {
            if (Config.Slots[i].Kind == SlotKind.Bot)
                AddBot(i, Config.Slots[i].Difficulty);
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
        GetNode<FogView>("FogView").Initialize(this);
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
        if (Net != null)
            Net.DesyncDetected += _ => ShowNetworkProblem("NET_DESYNC");

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

    /// <summary>The pause menu is open. In a network game the world keeps running behind it.</summary>
    public bool IsPaused => Net != null ? _pauseMenu.Visible : GetTree().Paused;

    /// <summary>Freezes the match (simulation, camera, input) while menus stay responsive.</summary>
    public void SetPaused(bool paused)
    {
        if (Net == null)
            GetTree().Paused = paused;
        _pauseMenu.Visible = paused;
    }

    /// <summary>A network game cannot go on (desync, host gone): show why and offer the way out.</summary>
    public void ShowNetworkProblem(string reasonKey) => _endScreen.ShowProblem(reasonKey);

    /// <summary>Dev-only: shows the end screen as if the match had ended, without touching the simulation.</summary>
    public void PreviewEndScreen(bool victory) =>
        _endScreen.OnSimEvent(new MatchEnded(victory ? LocalTeam : -1));

    public void RequestRestart() => RestartRequested?.Invoke();

    public void RequestMenu() => MenuRequested?.Invoke();

    /// <summary>The player's capital, else their first unit.</summary>
    private Vector2? StartingFocus()
    {
        if (Sim.GetFaction(LocalFactionId) is { } player && Sim.GetBuilding(player.CapitalId) is { } capital)
            return SimConvert.ToWorld(capital.Position);
        foreach (var unit in Sim.Units)
            if (unit.FactionId == LocalFactionId)
                return SimConvert.ToWorld(unit.Position);
        return null;
    }

    public override void _Process(double delta)
    {
        if (Net != null)
            ProcessNetworked(delta);
        else
            ProcessLocal(delta);

        UpdateFog();

        foreach (var e in Sim.TakeEvents())
        {
            Stats.OnSimEvent(e);
            _hud.OnSimEvent(e);
            _alerts.OnSimEvent(e, Sim.Tick);
            _endScreen.OnSimEvent(e);
            if (e is FactionEliminated { FactionId: var fallen } && fallen == LocalFactionId || e is MatchEnded)
                Fog.RevealAll(); // spectating: the rest of the match in full view
            if (!CanSee(e))
                continue; // a fight in the fog makes no sparks or sounds
            _unitsView.OnSimEvent(e);
            _groundEffects.OnSimEvent(e);
            _airEffects.OnSimEvent(e);
            _sounds.OnSimEvent(e);
        }
    }

    private void UpdateFog()
    {
        if (Sim.Tick == _fogTick)
            return;
        _fogTick = Sim.Tick;
        Fog.Update(Sim, LocalTeam);
    }

    private bool IsFriendly(int factionId) => factionId >= 0 && Sim.GetFaction(factionId)?.Team == LocalTeam;

    /// <summary>An enemy (or neutral) unit out of the local team's sight.</summary>
    public bool IsHidden(Sim.Units.Unit unit) => !IsFriendly(unit.FactionId) && !Fog.IsVisible(unit.Position);

    /// <summary>An enemy building none of whose tiles the local team sees.</summary>
    public bool IsHidden(Sim.Buildings.Building building) =>
        !IsFriendly(building.FactionId) && !Fog.IsAnyVisible(building.Anchor, building.Type.Size);

    /// <summary>Whether the player may see (and hear) where an event happened.</summary>
    private bool CanSee(SimEvent e) => e switch
    {
        AttackLanded hit => Fog.IsVisible(hit.Position),
        ProjectileFired shot => Fog.IsVisible(shot.Position),
        UnitDied died => IsFriendly(died.FactionId) || Fog.IsVisible(died.Position),
        BuildingDestroyed destroyed => IsFriendly(destroyed.FactionId) || Fog.IsVisible(destroyed.Position),
        BuildingCompleted completed => IsFriendly(completed.FactionId),
        _ => true,
    };

    private void ProcessLocal(double delta)
    {
        _accumulator += delta;
        int steps = 0;
        while (_accumulator >= Simulation.TickDuration && steps < MaxStepsPerFrame)
        {
            StepOnce();
            _accumulator -= Simulation.TickDuration;
            steps++;
        }
        if (_accumulator >= Simulation.TickDuration)
            _accumulator = 0; // drop the backlog after a hitch instead of fast-forwarding
    }

    /// <summary>
    /// Lockstep: a tick runs only once its frame (everyone's commands) has arrived. When frames pile up
    /// (we fell behind) we catch up a few ticks per frame; when none is there we wait.
    /// </summary>
    private void ProcessNetworked(double delta)
    {
        var net = Net!;
        net.Pump();
        if (Sim.IsOver || net.IsDesynced)
            return;

        _accumulator += delta;
        int steps = 0;
        while (steps < MaxStepsPerFrame && net.HasFrame(Sim.Tick + 1)
               && (_accumulator >= Simulation.TickDuration || net.HasFrame(Sim.Tick + 1 + LockstepCatchUpThreshold)))
        {
            var frame = net.TakeFrame(Sim.Tick + 1);
            foreach (var command in frame.Commands)
                Sim.Issue(command);
            foreach (int faction in frame.Takeovers)
            {
                if (_bots.All(b => b.FactionId != faction))
                {
                    AddBot(faction, BotDifficulty.Normal);
                    _hud.ShowNotice(string.Format(Tr("NET_PLAYER_LEFT"), Names.Faction(faction)));
                }
            }
            StepOnce();
            net.AfterStep(Sim.Tick, Sim);
            _accumulator = Math.Max(0, _accumulator - Simulation.TickDuration);
            steps++;
        }
        // While waiting for others, do not bank time to burst through later.
        _accumulator = Math.Min(_accumulator, Simulation.TickDuration * 4);
        WaitingForPlayers = !net.HasFrame(Sim.Tick + 1) && _accumulator >= Simulation.TickDuration * 4;
    }

    private void StepOnce()
    {
        long start = Stopwatch.GetTimestamp();
        Sim.Step();
        // Bots decide right after a tick, like a player reacting to what they see.
        foreach (var bot in _bots)
            bot.Update();
        double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        StepMilliseconds = StepMilliseconds * 0.9 + ms * 0.1;
    }

    private static string ReadText(string path)
    {
        string text = FileAccess.GetFileAsString(path);
        if (string.IsNullOrEmpty(text))
            throw new InvalidOperationException($"Cannot read '{path}': {FileAccess.GetOpenError()}");
        return text;
    }
}

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

    /// <summary>Hands a faction to the computer (bot slots at start, or a player who left).</summary>
    public BotBrain AddBot(int factionId, BotDifficulty difficulty)
    {
        var bot = new BotBrain(Sim, factionId, difficulty, ActualSeed + factionId);
        _bots.Add(bot);
        return bot;
    }

    [Export(PropertyHint.File, "*.json")] public string RulesPath { get; set; } = "res://Data/rules.json";

    private Hud _hud = null!;
    private EndScreen _endScreen = null!;
    private PauseMenu _pauseMenu = null!;
    private UnitsView _unitsView = null!;
    private BuildingsView _buildingsView = null!;
    private EffectsView _groundEffects = null!;
    private EffectsView _airEffects = null!;
    private MatchSounds _sounds = null!;
    private AttackAlerts _alerts = null!;

    /// <summary>The player's troops or buildings came under attack here (the minimap blinks it).</summary>
    public event Action<System.Numerics.Vector2>? AttackAlerted;

    private readonly List<BotBrain> _bots = new();

    /// <summary>Set by <see cref="Main"/> before the match enters the tree; defaults when the scene runs alone.</summary>
    public MatchConfig Config { get; set; } = MatchConfig.Default;

    /// <summary>Raised when the player asks for a new match with the same settings.</summary>
    public event Action? RestartRequested;

    /// <summary>Raised when the player leaves to the main menu.</summary>
    public event Action? MenuRequested;

    public Simulation Sim { get; private set; } = null!;

    /// <summary>The unit sheets in each faction's colour, shared by the unit, shadow and effect views.</summary>
    public Units.UnitSheets UnitSheets { get; } = new();

    /// <summary>What the local team sees; enemies outside it are not drawn, heard or clickable.</summary>
    public Visibility Fog { get; private set; } = null!;
    private int _fogTick = -1;
    public MatchStats Stats { get; private set; } = null!;
    public int ActualSeed { get; private set; }

    /// <summary>The biome of every cell (meadow, snow, sand), row by row: from the map's seed, the same for every player.</summary>
    public System.Collections.Generic.IReadOnlyList<Biome>? MapBiomes { get; private set; }

    public Biome BiomeAt(Cell cell) => MapBiomes?[cell.Y * Sim.Map.Width + cell.X] ?? Biome.Meadow;
    public IReadOnlyList<BotBrain> Bots => _bots;
    public Selection Selection { get; } = new();
    public CameraController Camera { get; private set; } = null!;

    /// <summary>The terrain view (its decor is cleared under buildings).</summary>
    public MapView Map { get; private set; } = null!;

    public override void _Ready()
    {
        _night = new CanvasModulate { Name = "Night", Color = Colors.White };
        AddChild(_night);
        var rules = GameRules.Parse(ReadText(RulesPath));
        ActualSeed = Config.Seed ?? (int)(GD.Randi() & int.MaxValue);
        // Every match is on a new map, sized and laid out for the sides at the start; the seed (the "map number" in the
        // menu) and their number reproduce it exactly, which is also how network players all get the same map.
        var map = MapGenerator.Generate(ActualSeed, Config.Slots.Count, Config.Mode);
        MapBiomes = map.Biomes;
        LocalFactionId = Config.LocalFactionId;
        Sim = new Simulation(map, rules, new MatchSetup(Config.Slots.Count, ActualSeed, Teams: Config.Teams, Mode: Config.Mode));
        FactionColors.UseDefense(Sim.Defense?.GarrisonFactionId ?? -1, Sim.Defense?.HordeFactionId ?? -1);
        Stats = new MatchStats();
        Fog = new Visibility(Sim.Map.Width, Sim.Map.Height);
        UpdateFog();
        _alerts = new AttackAlerts(LocalFactionId, Sim.Defense?.GarrisonFactionId);
        for (int i = 0; i < Config.Slots.Count; i++)
        {
            if (Config.Slots[i].Kind == SlotKind.Bot)
                AddBot(i, Config.Slots[i].Difficulty);
        }

        Camera = GetNode<CameraController>("Camera");
        var worldSize = new Vector2(Sim.Map.Width, Sim.Map.Height) * SimConvert.TileSize;
        Camera.SetBounds(new Rect2(Vector2.Zero, worldSize).Grow(SimConvert.TileSize * 2));
        Camera.CenterOn(StartingFocus() ?? worldSize / 2);

        Map = GetNode<MapView>("MapView");
        Map.Build(Sim.Map, MapBiomes);
        foreach (var deposit in Sim.Deposits)
            Map.ClearDecor(deposit.Anchor, GoldDeposit.Size);
        GetNode<DepositsView>("DepositsView").Initialize(this);
        _buildingsView = GetNode<BuildingsView>("Actors/BuildingsView");
        _buildingsView.Initialize(this);
        _unitsView = GetNode<UnitsView>("Actors/UnitsView");
        _unitsView.Initialize(this);
        GetNode<ShadowsView>("Shadows").Initialize(this, _unitsView, _buildingsView);
        GetNode<CloudsView>("Clouds").Initialize(this);
        GetNode<WeatherView>("Weather").Initialize(this);
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
        {
            Net.DesyncDetected += _ => ShowNetworkProblem("NET_DESYNC");
            Net.PauseChanged += OnNetPauseChanged;
        }
        if (IsShowcase)
            BecomeShowcase();

        _alerts.Alerted += position =>
        {
            AttackAlerted?.Invoke(position);
            _hud.ShowAlert(position);
            _airEffects.Ping(position);
            _sounds.PlayAlert();
        };
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (IsShowcase)
            return;
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
        else
            Net.RequestPause(paused); // everyone stops (or goes on) together
        _pauseMenu.Visible = paused;
    }

    /// <summary>A network pause began or ended (perhaps by someone else): show or hide the pause menu to match.</summary>
    private void OnNetPauseChanged()
    {
        _pauseMenu.Visible = Net!.IsPausedByPlayer;
        _pauseMenu.ShowPausedBy(Net.IsPausedByPlayer ? Net.PausedBy : null);
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

    /// <summary>Leaving the match (menu, restart, quit): the host's save keeps everything played so far.</summary>
    public override void _ExitTree()
    {
        EndHitStop();
        Net?.Dispose();
        UnitSheets.Dispose();
    }

    public override void _Process(double delta)
    {
        if (Net != null)
            ProcessNetworked(delta);
        else
            ProcessLocal(delta);

        UpdateFog();
        Stats.Update(Sim);
        UpdateAmbience((float)delta);
        DispatchEvents(delta);
    }

    private void UpdateFog()
    {
        if (Sim.Tick == _fogTick)
            return;
        _fogTick = Sim.Tick;
        Fog.Update(Sim, LocalTeam);
    }

    /// <summary>
    /// Feedback for a player's attack order: the target pulses red and a soft acknowledgement sounds, so the click
    /// clearly took (the order itself goes through <see cref="Issue"/>).
    /// </summary>
    public void AcknowledgeAttack(int targetId)
    {
        _unitsView.MarkTarget(targetId);
        _buildingsView.MarkTarget(targetId);
        _sounds.PlayOrder();
    }

    /// <summary>A soft acknowledgement of a move order.</summary>
    public void AcknowledgeMove() => _sounds.PlayOrder();

    /// <summary>The selected building, if it is ours and trains troops (so a right click sets its rally point).</summary>
    public Sim.Buildings.Building? SelectedRallyBuilding() =>
        Selection.BuildingId is { } id && Sim.GetBuilding(id) is { } building
        && building.FactionId == LocalFactionId && building.Type.Recruits.Count > 0
            ? building
            : null;

    /// <summary>A side played by the computer on every machine (a bot slot, or a player who left): it does no diplomacy.</summary>
    public bool IsBotControlled(int factionId) => _bots.Any(b => b.FactionId == factionId && b.CommandSink == null);

    public bool IsFriendly(int factionId) => factionId >= 0 && Sim.GetFaction(factionId)?.Team == LocalTeam;

    /// <summary>An enemy (or neutral) unit out of the local team's sight.</summary>
    public bool IsHidden(Sim.Units.Unit unit) => !IsFriendly(unit.FactionId) && !Fog.IsVisible(unit.Position);

    /// <summary>An enemy building none of whose tiles the local team sees.</summary>
    public bool IsHidden(Sim.Buildings.Building building) =>
        !IsFriendly(building.FactionId) && !Fog.IsAnyVisible(building.Anchor, building.Type.Size);

    private static string ReadText(string path)
    {
        string text = FileAccess.GetFileAsString(path);
        if (string.IsNullOrEmpty(text))
            throw new InvalidOperationException($"Cannot read '{path}': {FileAccess.GetOpenError()}");
        return text;
    }
}

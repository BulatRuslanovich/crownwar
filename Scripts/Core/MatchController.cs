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
    /// Frames buffered this far ahead mean the match is being replayed (this player rejoined after a drop):
    /// ticks run as fast as the frame budget allows, with nothing drawn or heard on the way.
    /// </summary>
    private const int ReplayThreshold = 40;
    private const double ReplayBudgetMilliseconds = 25;

    /// <summary>Replaying the match after rejoining it.</summary>
    public bool CatchingUp { get; private set; }

    /// <summary>How far the replay has come, 0..1.</summary>
    public float CatchUpProgress => Net is { LatestFrameTick: > 0 } net ? Math.Min(1f, (float)Sim.Tick / net.LatestFrameTick) : 0f;

    private bool _replayedThisFrame;

    /// <summary>Factions a returning player takes back from their bot, by the tick it happens.</summary>
    private readonly Dictionary<int, int> _returnAt = new();

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

    /// <summary>The player's troops or buildings came under attack here (the minimap blinks it).</summary>
    public event Action<System.Numerics.Vector2>? AttackAlerted;

    private readonly List<BotBrain> _bots = new();

    /// <summary>Set by <see cref="Main"/> before the match enters the tree; defaults when the scene runs alone.</summary>
    public MatchConfig Config { get; set; } = MatchConfig.Default;

    /// <summary>Raised when the player asks for a new match with the same settings.</summary>
    public event Action? RestartRequested;

    /// <summary>Raised when the player leaves to the main menu.</summary>
    public event Action? MenuRequested;

    /// <summary>The menu backdrop: bots play, nobody watches the interface (see <see cref="MatchConfig.Showcase"/>).</summary>
    public bool IsShowcase => Config.Showcase;

    /// <summary>Showcase only: the match is over and a new one may take its place.</summary>
    public event Action? ShowcaseFinished;

    private Vector2 _showcaseFocus;
    private double _showcaseRetarget;
    private double _showcaseOverFor;
    private System.Numerics.Vector2? _lastFight;

    public Simulation Sim { get; private set; } = null!;

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

    /// <summary>
    /// The local team is in a real fight: enough blows landed on or by its units and buildings lately.
    /// With hysteresis, so the battle music neither starts on a stray arrow nor flickers off mid-fight.
    /// </summary>
    public bool InBattle { get; private set; }

    /// <summary>Blows involving the local team, decaying over a few seconds.</summary>
    private float _battleHeat;
    public float BattleHeat => _battleHeat;
    private const float BattleHeatHalfLife = 4f;
    private const float BattleStarts = 6f;
    private const float BattleEnds = 1f;
    /// <summary>Caps the heat, so peace returns within some 15 seconds even after a huge battle.</summary>
    private const float BattleHeatMax = 15f;

    /// <summary>Smoothed wall-clock cost of one simulation tick plus bot decisions, for the debug overlay.</summary>
    public double StepMilliseconds { get; private set; }

    /// <summary>Fraction of the way from the previous tick to the next one, for smooth rendering.</summary>
    public float InterpolationAlpha => (float)(_accumulator / Simulation.TickDuration);

    /// <summary>The world dims to this at night (the interface, on its own layers, stays as it is).</summary>
    private static readonly Color NightTint = new(0.55f, 0.6f, 0.85f);
    /// <summary>How quickly dusk and dawn shade the world (per second).</summary>
    private const float DuskSpeed = 1.5f;
    private CanvasModulate _night = null!;

    public override void _Ready()
    {
        _night = new CanvasModulate { Name = "Night", Color = Colors.White };
        AddChild(_night);
        var rules = GameRules.Parse(ReadText(RulesPath));
        ActualSeed = Config.Seed ?? (int)(GD.Randi() & int.MaxValue);
        // Every match is on a new map; the seed (the "map number" in the menu) reproduces it exactly,
        // which is also how network players all get the same map.
        var map = MapGenerator.Generate(ActualSeed);
        MapBiomes = map.Biomes;
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

        GetNode<MapView>("MapView").Build(Sim.Map, MapBiomes);
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

    /// <summary>Strips the match down to the world itself: no interface, sounds, fog or player control.</summary>
    private void BecomeShowcase()
    {
        Fog.RevealAll();
        Camera.UserControl = false;
        Camera.SetZoomIndex(2);
        // Exactly the map (no margin beyond its edges), starting from the middle.
        var worldSize = new Vector2(Sim.Map.Width, Sim.Map.Height) * SimConvert.TileSize;
        Camera.SetBounds(new Rect2(Vector2.Zero, worldSize));
        Camera.CenterOn(worldSize / 2);
        _showcaseFocus = worldSize / 2;
        _showcaseRetarget = 6.0;
        foreach (string path in new[] { "Hud", "EndScreen", "PauseMenu", "DebugHud", "SelectionController", "BuildPlacement", "Sounds" })
        {
            var node = GetNode(path);
            node.ProcessMode = ProcessModeEnum.Disabled;
            if (node is CanvasLayer layer)
                layer.Visible = false;
            else if (node is CanvasItem item)
                item.Visible = false;
        }
    }

    /// <summary>
    /// The backdrop camera drifts between points of interest: the latest fight, else a random castle.
    /// When the match ends, a new one is asked for after a short look at the outcome.
    /// </summary>
    private void UpdateShowcase(double delta)
    {
        _showcaseRetarget -= delta;
        if (_showcaseRetarget <= 0)
        {
            _showcaseRetarget = 9.0;
            if (_lastFight is { } fight)
                _showcaseFocus = SimConvert.ToWorld(fight);
            else if (Sim.Buildings.Where(b => b.Type.IsCapital).ToList() is { Count: > 0 } castles)
                _showcaseFocus = SimConvert.ToWorld(castles[(int)(GD.Randi() % castles.Count)].Position);
            _lastFight = null;
        }
        Camera.GlideTowards(_showcaseFocus, 3f, delta);

        if (!Sim.IsOver)
            return;
        _showcaseOverFor += delta;
        if (_showcaseOverFor > 6.0)
        {
            _showcaseOverFor = double.NegativeInfinity; // ask once
            ShowcaseFinished?.Invoke();
        }
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

    /// <summary>Heavy blows on screen shake it: a stone landing a little, a building collapsing more, a castle most.</summary>
    private void ShakeFor(SimEvent e)
    {
        var (at, amount) = e switch
        {
            SplashLanded s => (s.Position, 0.18f),
            BuildingDestroyed d => (d.Position, Sim.Rules.GetBuildingType(d.BuildingTypeId).IsCapital ? 0.9f : 0.4f),
            _ => (default(System.Numerics.Vector2), 0f),
        };
        if (amount <= 0f)
            return;
        // Fades with the distance from the middle of the screen: a far-off collapse is only felt a little.
        var screen = GetViewportRect().Size / Camera.ZoomLevel;
        float distance = SimConvert.ToWorld(at).DistanceTo(Camera.GetScreenCenterPosition());
        float nearness = Mathf.Clamp(1f - distance / screen.Length(), 0f, 1f);
        Camera.Shake(amount * nearness);
    }

    /// <summary>Leaving the match (menu, restart, quit): the host's save keeps everything played so far.</summary>
    public override void _ExitTree() => Net?.CloseSave();

    public override void _Process(double delta)
    {
        if (Net != null)
            ProcessNetworked(delta);
        else
            ProcessLocal(delta);

        UpdateFog();
        _night.Color = _night.Color.Lerp(Sim.IsNight ? NightTint : Colors.White, 1f - MathF.Exp(-DuskSpeed * (float)delta));
        _battleHeat *= MathF.Pow(0.5f, (float)delta / BattleHeatHalfLife);

        if (_replayedThisFrame)
        {
            // Replaying the match after a rejoin: count it, but no effects, sounds or messages for the past.
            foreach (var e in Sim.TakeEvents())
                Stats.OnSimEvent(e);
            return;
        }

        if (IsShowcase)
        {
            // Only the world reacts; no interface, statistics or sounds behind the menu.
            foreach (var e in Sim.TakeEvents())
            {
                if (e is AttackLanded hit)
                    _lastFight = hit.Position;
                _unitsView.OnSimEvent(e);
                _groundEffects.OnSimEvent(e);
                _airEffects.OnSimEvent(e);
            }
            UpdateShowcase(delta);
            return;
        }

        foreach (var e in Sim.TakeEvents())
        {
            if (e is AttackLanded hit && (IsFriendly(hit.FactionId) || IsFriendly(hit.TargetFactionId)))
                _battleHeat = MathF.Min(BattleHeatMax, _battleHeat + 1f);
            Stats.OnSimEvent(e);
            _hud.OnSimEvent(e);
            _alerts.OnSimEvent(e, Sim.Tick);
            _endScreen.OnSimEvent(e);
            if (e is FactionEliminated { FactionId: var fallen } && fallen == LocalFactionId || e is MatchEnded)
                Fog.RevealAll(); // spectating: the rest of the match in full view
            if (e is MatchEnded)
                Net?.DiscardSave(); // a finished match is not one to bring back
            if (!CanSee(e))
                continue; // a fight in the fog makes no sparks or sounds
            ShakeFor(e);
            _unitsView.OnSimEvent(e);
            _groundEffects.OnSimEvent(e);
            _airEffects.OnSimEvent(e);
            _sounds.OnSimEvent(e);
        }
        UpdateBattleMood();
    }

    private void UpdateBattleMood()
    {
        if (Sim.IsOver)
            InBattle = false;
        else if (_battleHeat >= BattleStarts)
            InBattle = true;
        else if (_battleHeat <= BattleEnds)
            InBattle = false;
    }

    private void UpdateFog()
    {
        if (Sim.Tick == _fogTick)
            return;
        _fogTick = Sim.Tick;
        Fog.Update(Sim, LocalTeam);
    }

    public bool IsFriendly(int factionId) => factionId >= 0 && Sim.GetFaction(factionId)?.Team == LocalTeam;

    /// <summary>An enemy (or neutral) unit out of the local team's sight.</summary>
    public bool IsHidden(Sim.Units.Unit unit) => !IsFriendly(unit.FactionId) && !Fog.IsVisible(unit.Position);

    /// <summary>An enemy building none of whose tiles the local team sees.</summary>
    public bool IsHidden(Sim.Buildings.Building building) =>
        !IsFriendly(building.FactionId) && !Fog.IsAnyVisible(building.Anchor, building.Type.Size);

    /// <summary>Whether the player may see (and hear) where an event happened.</summary>
    private bool CanSee(SimEvent e) => e switch
    {
        AttackLanded hit => Fog.IsVisible(hit.Position),
        SplashLanded splash => Fog.IsVisible(splash.Position),
        ProjectileFired shot => Fog.IsVisible(shot.Position),
        UnitDied died => IsFriendly(died.FactionId) || Fog.IsVisible(died.Position),
        BuildingDestroyed destroyed => IsFriendly(destroyed.FactionId) || Fog.IsVisible(destroyed.Position),
        BuildingDemolished demolished => IsFriendly(demolished.FactionId) || Fog.IsVisible(demolished.Position),
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
    /// <summary>Times the match had to stop and wait for another player's commands (shown on the F3 overlay).</summary>
    public int NetStalls { get; private set; }
    private bool _starving;

    private void ProcessNetworked(double delta)
    {
        var net = Net!;
        net.Pump(delta);
        StepNetworked(net, delta);
        net.Flush();
    }

    private void StepNetworked(NetMatchDriver net, double delta)
    {
        _replayedThisFrame = false;
        if (net.IsPausedByPlayer && !CatchingUp)
        {
            // Everyone stands still; do not bank the time, nor take the silence for a slow link.
            _accumulator = 0;
            WaitingForPlayers = false;
            _starving = false;
            return;
        }
        if (Sim.IsOver || net.IsDesynced)
            return;

        if (net.LatestFrameTick - Sim.Tick > ReplayThreshold && Sim.Tick < net.SilentReplayLimit)
        {
            CatchingUp = true;
            _replayedThisFrame = true;
            WaitingForPlayers = false;
            _accumulator = 0;
            long start = Stopwatch.GetTimestamp();
            // Only the decided past is replayed silently: a host resuming its save must send input for its first new
            // ticks, or it would wait for itself forever.
            while (net.HasFrame(Sim.Tick + 1) && Sim.Tick < net.SilentReplayLimit
                   && Stopwatch.GetElapsedTime(start).TotalMilliseconds < ReplayBudgetMilliseconds)
                ApplyFrame(net, net.TakeFrame(Sim.Tick + 1), replaying: true);
            return;
        }
        CatchingUp = false;
        // A returning player who has caught up asks for their faction back (once).
        if (net.IsRejoin && Sim.Tick > 0 && net.LatestFrameTick - Sim.Tick <= 2)
            net.ReportCaughtUp(Sim.Tick);

        _accumulator += delta;
        int steps = 0;
        while (steps < MaxStepsPerFrame && net.HasFrame(Sim.Tick + 1)
               && (_accumulator >= Simulation.TickDuration || net.HasFrame(Sim.Tick + 1 + LockstepCatchUpThreshold)))
        {
            ApplyFrame(net, net.TakeFrame(Sim.Tick + 1), replaying: false);
            _accumulator = Math.Max(0, _accumulator - Simulation.TickDuration);
            steps++;
        }
        // A stall: a whole tick overdue and still no frame (a few ms late is only clocks drifting apart).
        bool starving = !net.HasFrame(Sim.Tick + 1) && _accumulator >= Simulation.TickDuration * 2;
        if (starving && !_starving)
        {
            NetStalls++;
            net.ReportStall();
        }
        _starving = starving;
        // While waiting for others, do not bank time to burst through later.
        _accumulator = Math.Min(_accumulator, Simulation.TickDuration * 4);
        WaitingForPlayers = !net.HasFrame(Sim.Tick + 1) && _accumulator >= Simulation.TickDuration * 4;
    }

    /// <summary>
    /// What every machine does with a frame: returning players take their faction back from the bot, the
    /// commands go in, dropped players are handed to bots, then one tick runs.
    /// </summary>
    private void ApplyFrame(NetMatchDriver net, FrameMessage frame, bool replaying)
    {
        foreach (var ret in frame.Returns)
            _returnAt[ret.Faction] = ret.FromTick;
        foreach (var (faction, _) in _returnAt.Where(r => r.Value == frame.Tick).ToList())
        {
            // Only the stand-in that plays the faction on every machine; a bot that sends its commands through
            // the network (dev autoplay) is a player, not part of the simulation.
            _bots.RemoveAll(b => b.FactionId == faction && b.CommandSink == null);
            _returnAt.Remove(faction);
            if (net.LogChecksums)
                GD.Print($"[net] faction {faction} back under player control at tick {frame.Tick}");
            if (!replaying)
                _hud.ShowNotice(faction == LocalFactionId
                    ? Tr("NET_YOU_RETURNED")
                    : string.Format(Tr("NET_PLAYER_RETURNED"), Names.Faction(faction)));
        }
        foreach (var command in frame.Commands)
            Sim.Issue(command);
        foreach (int faction in frame.Takeovers)
        {
            if (_bots.Any(b => b.FactionId == faction && b.CommandSink == null))
                continue;
            AddBot(faction, BotDifficulty.Normal);
            if (!replaying)
                _hud.ShowNotice(string.Format(Tr("NET_PLAYER_LEFT"), Names.Faction(faction)));
        }
        StepOnce();
        net.AfterStep(Sim.Tick, Sim, sendInput: !replaying);
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

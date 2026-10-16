using System.Globalization;
using System.Linq;
using Godot;
using Warcrown.Dev;
using Warcrown.Net;
using Warcrown.Sim.AI;
using Warcrown.Sim.Net;
using Warcrown.UI;

namespace Warcrown.Core;

/// <summary>
/// Application root and screen switcher: main menu ↔ match, and for network games
/// network menu → lobby → match. Restarting or leaving a match frees the whole match scene and builds
/// a fresh one, so no state leaks between matches. A network game lives in one <see cref="NetSession"/>
/// from the network menu until the player goes back to the main menu.
/// </summary>
public partial class Main : Node
{
    [Export] public PackedScene MenuScene { get; set; } = null!;
    [Export] public PackedScene MatchScene { get; set; } = null!;
    [Export] public PackedScene NetMenuScene { get; set; } = null!;
    [Export] public PackedScene LobbyScene { get; set; } = null!;

    private Node? _screen;
    /// <summary>Menus are drawn on their own layer, above the backdrop match and its camera.</summary>
    private CanvasLayer _ui = null!;
    /// <summary>A bot match playing behind the menus.</summary>
    private MatchController? _backdrop;
    private MatchConfig _lastConfig = MatchConfig.Default;
    private NetSession? _session;
    private Audio.MusicPlayer _music = null!;
    private Audio.UiSounds? _uiSounds;

    public override void _EnterTree()
    {
        InputActions.Register();
        // Hooked before any screen exists, so every button gets its click.
        _uiSounds = new Audio.UiSounds { Name = "UiSounds" };
        AddChild(_uiSounds);
        UI.Cursors.Install();
    }

    public override void _Ready()
    {
        _ui = new CanvasLayer { Name = "Ui", Layer = 10 };
        AddChild(_ui);
        // Developer options only in debug builds; a released game ignores its command line.
        var args = OS.IsDebugBuild() ? OS.GetCmdlineUserArgs() : System.Array.Empty<string>();
        if (RunDevExport(args))
            return;
        _music = new Audio.MusicPlayer { Name = "Music" };
        AddChild(_music);
        GetTree().AutoAcceptQuit = false; // closing the window goes through QuitGame too
        if (!StartFromDevOptions(args))
            ShowMenu();
        ScheduleDevRuns(args);
    }

    public override void _Process(double delta)
    {
        // Menus get the menu theme; a match plays the day's or the night's piece, as tense as the fighting.
        if (_music != null)
        {
            var match = _screen as MatchController;
            _music.Track = match == null ? Audio.MusicTrack.Menu : match.Sim.IsNight ? Audio.MusicTrack.Night : Audio.MusicTrack.Day;
            _music.Intensity = match?.MusicLevel ?? 0;
        }
    }

    public void ShowMenu()
    {
        EndSession();
        GetTree().Paused = false;
        EnsureBackdrop();
        var menu = MenuScene.Instantiate<MainMenu>();
        menu.Initialize(_lastConfig);
        menu.StartRequested += config => StartMatch(config);
        menu.NetworkRequested += () => ShowNetMenu(null);
        menu.QuitRequested += QuitGame;
        Replace(menu);
    }

    public MatchController StartMatch(MatchConfig config)
    {
        _lastConfig = config;
        var match = CreateMatch(config, null);
        match.RestartRequested += () => StartMatch(_lastConfig);
        match.MenuRequested += ShowMenu;
        return match;
    }

    private MatchController CreateMatch(MatchConfig config, NetMatchDriver? net)
    {
        GetTree().Paused = false;
        FreeBackdrop();
        var match = MatchScene.Instantiate<MatchController>();
        match.Config = config;
        match.Net = net;
        Replace(match);
        return match;
    }

    /// <summary>Black over everything, faded out whenever a new screen comes up (see <see cref="Replace"/>).</summary>
    private ColorRect? _curtain;

    private void Replace(Node screen)
    {
        FadeFromBlack();
        if (_screen != null)
        {
            _screen.GetParent().RemoveChild(_screen);
            _screen.QueueFree();
        }
        _screen = screen;
        if (screen is Control)
            _ui.AddChild(screen);
        else
            AddChild(screen);
    }

    /// <summary>A new screen does not cut in: it comes out of the dark in a moment (not the very first one, after the splash).</summary>
    private void FadeFromBlack()
    {
        if (_screen == null)
            return;
        if (_curtain == null)
        {
            var layer = new CanvasLayer { Name = "Curtain", Layer = 100, ProcessMode = ProcessModeEnum.Always };
            AddChild(layer);
            _curtain = new ColorRect { Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Ignore };
            _curtain.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            layer.AddChild(_curtain);
        }
        _curtain.Modulate = Colors.White;
        _curtain.CreateTween().TweenProperty(_curtain, "modulate:a", 0f, 0.35).SetEase(Tween.EaseType.Out);
    }

    /// <summary>Starts the living backdrop behind the menus, unless one is already playing.</summary>
    private void EnsureBackdrop()
    {
        if (_backdrop != null)
            return;
        _backdrop = MatchScene.Instantiate<MatchController>();
        _backdrop.Name = "Backdrop";
        _backdrop.Config = MatchConfig.ShowcaseMatch((int)(GD.Randi() & int.MaxValue));
        _backdrop.ShowcaseFinished += () => Callable.From(() =>
        {
            FreeBackdrop();
            if (_screen is not MatchController)
                EnsureBackdrop();
        }).CallDeferred();
        AddChild(_backdrop);
        MoveChild(_backdrop, 0); // behind everything else
    }

    private void FreeBackdrop()
    {
        if (_backdrop == null)
            return;
        RemoveChild(_backdrop);
        _backdrop.QueueFree();
        _backdrop = null;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
            QuitGame();
    }

    /// <summary>
    /// Quits once the music has stopped. The audio thread lets go of a stopped stream a moment later;
    /// quitting in the same frame would leave it referenced at exit (reported as leaked instances).
    /// </summary>
    public async void QuitGame()
    {
        _music?.StopAll();
        _uiSounds?.StopAll();
        UI.Cursors.Uninstall();
        await ToSignal(GetTree().CreateTimer(0.15, processAlways: true), SceneTreeTimer.SignalName.Timeout);
        GetTree().Quit();
    }
}

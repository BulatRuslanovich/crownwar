using System.Globalization;
using System.Linq;
using Godot;
using Lordsio.Net;
using Lordsio.Sim.AI;
using Lordsio.Sim.Net;
using Lordsio.UI;

namespace Lordsio.Core;

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
    private MatchConfig _lastConfig = MatchConfig.Default;
    private NetSession? _session;
    private Audio.MusicPlayer _music = null!;

    // Dev options for testing network games with two copies on one PC (see StartDevNetwork).
    private int _devAutoStartPlayers;
    private bool _devAutoplay;
    private bool _devNetLog;
    private int[]? _devTeams;

    public override void _EnterTree()
    {
        InputActions.Register();
    }

    public override void _Ready()
    {
        // Developer options only in debug builds; a released game ignores its command line.
        var args = OS.IsDebugBuild() ? OS.GetCmdlineUserArgs() : System.Array.Empty<string>();
        if (DevTools.Arg(args, "--export-art") is { } previewDirectory)
        {
            Dev.ArtGenerator.ExportAll(ProjectSettings.GlobalizePath("res://"), previewDirectory);
            GD.Print("Art exported");
            GetTree().Quit();
            return;
        }
        if (DevTools.Arg(args, "--export-sounds") is { } soundDirectory)
        {
            Audio.SoundBank.ExportAll(soundDirectory);
            Audio.MusicPlayer.ExportAll(soundDirectory);
            GetTree().Quit();
            return;
        }
        _music = new Audio.MusicPlayer { Name = "Music" };
        AddChild(_music);
        GetTree().AutoAcceptQuit = false; // closing the window goes through QuitGame too
        if (DevTools.WantsDirectStart(args))
            DevTools.Apply(StartMatch(MatchConfig.Default with { Seed = DevTools.Seed(args) }), args);
        else if (DevTools.WantsNetwork(args))
            StartDevNetwork(args);
        else
            ShowMenu();

        if (System.Array.IndexOf(args, "--smoke-flow") >= 0)
            _ = RunSmokeFlow();

        if (DevTools.Arg(args, "--screenshot") is { } screenshot)
        {
            double delay = double.Parse(DevTools.Arg(args, "--screenshot-delay") ?? "2", CultureInfo.InvariantCulture);
            _ = TakeScreenshotAndQuit(screenshot, delay, System.Array.IndexOf(args, "--demo-select-mine") >= 0);
        }
    }

    public override void _Process(double delta)
    {
        // Menus get the menu theme; a match plays its calm or battle music.
        if (_music != null)
            _music.Track = _screen is MatchController match
                ? match.InBattle ? Audio.MusicTrack.Battle : Audio.MusicTrack.Peace
                : Audio.MusicTrack.Menu;
    }

    public void ShowMenu()
    {
        EndSession();
        GetTree().Paused = false;
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
        var match = MatchScene.Instantiate<MatchController>();
        match.Config = config;
        match.Net = net;
        Replace(match);
        return match;
    }

    // ---------------------------------------------------------------- network games

    private NetSession Session()
    {
        if (_session != null)
            return _session;
        _session = new NetSession { Name = "NetSession" };
        AddChild(_session);
        // Session events can fire inside a button handler of the very screen they replace,
        // so screen switches are deferred to the end of the frame.
        _session.LobbyUpdated += () => Callable.From(OnLobbyUpdated).CallDeferred();
        _session.MatchStarting += start => Callable.From(() => StartNetworkMatch(start)).CallDeferred();
        _session.Failed += reason => Callable.From(() => OnSessionFailed(reason)).CallDeferred();
        return _session;
    }

    private void EndSession()
    {
        if (_session == null)
            return;
        _session.Close();
        _session.QueueFree();
        _session = null;
    }

    private void ShowNetMenu(string? problemKey)
    {
        GetTree().Paused = false;
        var menu = NetMenuScene.Instantiate<NetMenu>();
        menu.Initialize(Session(), problemKey);
        menu.BackRequested += ShowMenu;
        Replace(menu);
    }

    private void OnLobbyUpdated()
    {
        if (_session == null || _session.InMatch)
            return;
        if (_screen is NetMenu)
        {
            var lobby = LobbyScene.Instantiate<Lobby>();
            lobby.Initialize(_session);
            lobby.LeaveRequested += () =>
            {
                _session?.Close();
                ShowNetMenu(null);
            };
            Replace(lobby);
        }
        if (_devAutoStartPlayers > 0 && _session.IsHost)
            DevAutoStart(_session);
    }

    private void StartNetworkMatch(StartMessage start)
    {
        if (_session == null)
            return;
        var slots = start.Slots.Select(s => new SlotConfig(
            s.Kind == NetSlotKind.Bot ? SlotKind.Bot : SlotKind.Human, s.Name, (BotDifficulty)s.Difficulty, s.Team)).ToList();
        var driver = new NetMatchDriver(_session, start) { LogChecksums = _devNetLog };
        var match = CreateMatch(new MatchConfig(slots, start.Seed, start.YourFaction), driver);
        match.MenuRequested += ShowMenu;
        if (_devAutoplay)
            match.AddBot(match.LocalFactionId, BotDifficulty.Hard).CommandSink = match.Issue;
    }

    /// <summary>Lost connection, rejected, host gone: in a match the end screen explains; elsewhere back to the network menu.</summary>
    private void OnSessionFailed(string reasonKey)
    {
        if (_screen is MatchController match)
            match.ShowNetworkProblem(reasonKey);
        else
            ShowNetMenu(reasonKey);
    }

    /// <summary>
    /// Dev-only, for two copies on one PC: --net-browse opens the network menu; --net-host or --net-join=ADDRESS
    /// skip the menus; --net-players=N
    /// makes the host start once N players are in (open slots become Hard bots), --net-teams=0,0,1,1 sets the
    /// slots' teams; --net-autoplay lets a bot
    /// play the local faction through the network; --net-log prints checksums to compare the copies' logs.
    /// </summary>
    private void StartDevNetwork(string[] args)
    {
        _devAutoplay = System.Array.IndexOf(args, "--net-autoplay") >= 0;
        _devNetLog = System.Array.IndexOf(args, "--net-log") >= 0;
        string name = DevTools.Arg(args, "--net-name") ?? GameSettings.Instance?.PlayerName ?? "Player";
        ShowNetMenu(null);
        if (System.Array.IndexOf(args, "--net-browse") >= 0)
            return;
        if (DevTools.Arg(args, "--net-join") is { } address)
        {
            Session().Join(address, name);
            return;
        }
        _devAutoStartPlayers = int.Parse(DevTools.Arg(args, "--net-players") ?? "2", CultureInfo.InvariantCulture);
        _devTeams = DevTools.Arg(args, "--net-teams")?.Split(',').Select(t => int.Parse(t, CultureInfo.InvariantCulture)).ToArray();
        var error = Session().Host(name);
        if (error != Error.Ok)
            GD.PushError($"Could not host: {error}");
    }

    private void DevAutoStart(NetSession session)
    {
        if (session.Slots.Count(s => s.Kind == NetSlotKind.Human) < _devAutoStartPlayers)
            return;
        _devAutoStartPlayers = 0;
        for (int i = 0; i < session.Slots.Count; i++)
        {
            if (session.Slots[i].Kind == NetSlotKind.Empty)
                session.SetSlot(i, NetSlotKind.Bot, BotDifficulty.Hard);
            if (_devTeams != null && i < _devTeams.Length)
                session.SetTeam(i, _devTeams[i]);
        }
        session.StartMatch();
    }

    private void Replace(Node screen)
    {
        if (_screen != null)
        {
            RemoveChild(_screen);
            _screen.QueueFree();
        }
        _screen = screen;
        AddChild(screen);
    }

    /// <summary>
    /// Dev-only (--smoke-flow): menu → match → pause → restart → main menu → match → quit,
    /// to catch errors in screen switching without clicking through it by hand.
    /// </summary>
    private async System.Threading.Tasks.Task RunSmokeFlow()
    {
        async System.Threading.Tasks.Task Wait(double seconds) =>
            await ToSignal(GetTree().CreateTimer(seconds, processAlways: true), SceneTreeTimer.SignalName.Timeout);

        await Wait(0.5);
        var match = StartMatch(MatchConfig.Single(2, Sim.AI.BotDifficulty.Hard, 11));
        await Wait(1.0);
        match.SetPaused(true);
        await Wait(0.5);
        match.RequestRestart();
        await Wait(1.0);
        ((MatchController)_screen!).RequestMenu();
        await Wait(0.5);
        StartMatch(MatchConfig.Default);
        await Wait(1.0);
        GD.Print("Smoke flow finished");
        QuitGame();
    }

    private async System.Threading.Tasks.Task TakeScreenshotAndQuit(string path, double delay, bool selectMine)
    {
        // Process even while the game is paused, so paused screens can be captured too.
        await ToSignal(GetTree().CreateTimer(delay, processAlways: true), SceneTreeTimer.SignalName.Timeout);
        if (selectMine && _screen is MatchController match)
        {
            DevTools.SelectFirstMine(match);
            await ToSignal(GetTree().CreateTimer(0.2, processAlways: true), SceneTreeTimer.SignalName.Timeout);
        }
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var error = GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"Screenshot saved to {path}: {error}");
        QuitGame();
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
        await ToSignal(GetTree().CreateTimer(0.15, processAlways: true), SceneTreeTimer.SignalName.Timeout);
        GetTree().Quit();
    }
}

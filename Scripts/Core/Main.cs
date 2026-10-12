using System.Globalization;
using Godot;
using Lordsio.UI;

namespace Lordsio.Core;

/// <summary>
/// Application root and screen switcher: main menu ↔ match. Restarting or leaving a match frees the
/// whole match scene and builds a fresh one, so no state leaks between matches.
/// </summary>
public partial class Main : Node
{
    [Export] public PackedScene MenuScene { get; set; } = null!;
    [Export] public PackedScene MatchScene { get; set; } = null!;

    private Node? _screen;
    private MatchConfig _lastConfig = MatchConfig.Default;

    public override void _EnterTree()
    {
        InputActions.Register();
    }

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        if (DevTools.WantsDirectStart(args))
            DevTools.Apply(StartMatch(MatchConfig.Default with { Seed = DevTools.Seed(args) }), args);
        else
            ShowMenu();

        if (System.Array.IndexOf(args, "--smoke-flow") >= 0)
            _ = RunSmokeFlow();

        if (DevTools.Arg(args, "--screenshot") is { } screenshot)
        {
            double delay = double.Parse(DevTools.Arg(args, "--screenshot-delay") ?? "2", CultureInfo.InvariantCulture);
            _ = TakeScreenshotAndQuit(screenshot, delay);
        }
    }

    public void ShowMenu()
    {
        GetTree().Paused = false;
        var menu = MenuScene.Instantiate<MainMenu>();
        menu.Initialize(_lastConfig);
        menu.StartRequested += config => StartMatch(config);
        Replace(menu);
    }

    public MatchController StartMatch(MatchConfig config)
    {
        GetTree().Paused = false;
        _lastConfig = config;
        var match = MatchScene.Instantiate<MatchController>();
        match.Config = config;
        match.RestartRequested += () => StartMatch(_lastConfig);
        match.MenuRequested += ShowMenu;
        Replace(match);
        return match;
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
        var match = StartMatch(new MatchConfig(2, Sim.AI.BotDifficulty.Hard, 11));
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
        GetTree().Quit();
    }

    private async System.Threading.Tasks.Task TakeScreenshotAndQuit(string path, double delay)
    {
        // Process even while the game is paused, so paused screens can be captured too.
        await ToSignal(GetTree().CreateTimer(delay, processAlways: true), SceneTreeTimer.SignalName.Timeout);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var error = GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"Screenshot saved to {path}: {error}");
        GetTree().Quit();
    }
}

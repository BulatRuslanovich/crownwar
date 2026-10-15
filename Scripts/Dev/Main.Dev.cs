using System.Globalization;
using System.Linq;
using Godot;
using Lordsio.Dev;
using Lordsio.Net;
using Lordsio.Sim.AI;
using Lordsio.Sim.Net;
using Lordsio.UI;

namespace Lordsio.Core;

/// <summary>Developer options of the application root (debug builds only): asset export, scripted network tests, smoke runs, screenshots.</summary>
public partial class Main
{
    // Dev options for testing network games with two copies on one PC (see StartDevNetwork).
    private int _devAutoStartPlayers;
    private bool _devAutoplay;
    private bool _devNetLog;
    private bool _devPauseTest;
    private bool _devDiplomacyTest;
    private int[]? _devTeams;

    /// <summary>--export-art / --export-sounds: writes the generated assets and quits; true if it did.</summary>
    private bool RunDevExport(string[] args)
    {
        if (DevTools.Arg(args, "--export-art") is { } previewDirectory)
        {
            ArtGenerator.ExportAll(ProjectSettings.GlobalizePath("res://"), previewDirectory);
            GD.Print("Art exported");
            GetTree().Quit();
            return true;
        }
        if (DevTools.Arg(args, "--export-sounds") is { } soundDirectory)
        {
            Audio.SoundBank.ExportAll(soundDirectory);
            Audio.MusicPlayer.ExportAll(soundDirectory);
            GetTree().Quit();
            return true;
        }
        return false;
    }

    /// <summary>Demo or network options start a match (or a network game) without the menu; true if they did.</summary>
    private bool StartFromDevOptions(string[] args)
    {
        if (DevTools.WantsDirectStart(args))
            DevTools.Apply(StartMatch(MatchConfig.Default with { Seed = DevTools.Seed(args) }), args);
        else if (DevTools.WantsNetwork(args))
            StartDevNetwork(args);
        else
            return false;
        return true;
    }

    /// <summary>Options that run on their own once the first screen is up: the help screen, the smoke run, a screenshot.</summary>
    private void ScheduleDevRuns(string[] args)
    {
        if (DevTools.Has(args, "--show-help") && _screen is MainMenu shown)
            Callable.From(shown.ShowHelp).CallDeferred();
        if (DevTools.Has(args, "--smoke-flow"))
            _ = RunSmokeFlow();
        if (DevTools.Arg(args, "--screenshot") is { } screenshot)
        {
            double delay = double.Parse(DevTools.Arg(args, "--screenshot-delay") ?? "2", CultureInfo.InvariantCulture);
            _ = TakeScreenshotAndQuit(screenshot, delay, DevTools.Arg(args, "--demo-select"));
        }
    }

    /// <summary>
    /// Dev (--net-diplo-test, on both copies): the host offers the other player peace at 6 s, the guest accepts at
    /// 12 s (the offer pop-up shows meanwhile), the host opens the diplomacy panel at 22 s and declares war at 24 s.
    /// </summary>
    private void DevDiplomacyTest(MatchController match)
    {
        int me = match.LocalFactionId;
        int other = match.Config.Slots.Select((s, i) => (s, i)).First(p => p.s.Kind == SlotKind.Human && p.i != me).i;
        void At(double seconds, System.Action action) =>
            GetTree().CreateTimer(seconds).Timeout += () => { if (IsInstanceValid(match)) action(); };
        if (_session!.IsHost)
        {
            At(6, () => match.Issue(new Sim.Core.ProposePeaceCommand(me, other)));
            At(22, () => match.GetNode<UI.Hud>("Hud").Diplomacy.Visible = true);
            At(24, () => match.Issue(new Sim.Core.DeclareWarCommand(me, other)));
        }
        else
        {
            At(12, () => match.Issue(new Sim.Core.AnswerPeaceCommand(me, other, Accept: true)));
        }
    }

    /// <summary>
    /// Dev-only, for two copies on one PC: --net-browse opens the network menu; --net-host or --net-join=ADDRESS
    /// skip the menus; --net-players=N
    /// makes the host start once N players are in (open slots become Hard bots), --net-teams=0,0,1,1 sets the
    /// slots' teams; --net-diplo-test plays a scripted round of diplomacy (see DevDiplomacyTest); --net-autoplay lets a bot
    /// play the local faction through the network; --net-log prints checksums to compare the copies' logs;
    /// --net-lag=MS[,JITTER] holds this copy's outgoing packets back (a poor link on one PC; ENet's ping does not see it);
    /// --net-resume hosts the saved match again (as after a crash), if there is one; --net-pause-test pauses the
    /// match from this copy at 20 s and resumes it at 26 s.
    /// </summary>
    private void StartDevNetwork(string[] args)
    {
        _devAutoplay = DevTools.Has(args, "--net-autoplay");
        _devNetLog = DevTools.Has(args, "--net-log");
        _devPauseTest = DevTools.Has(args, "--net-pause-test");
        _devDiplomacyTest = DevTools.Has(args, "--net-diplo-test");
        string name = DevTools.Arg(args, "--net-name") ?? GameSettings.Instance?.PlayerName ?? PlayerNames.Fallback;
        ShowNetMenu(null);
        if (DevTools.Arg(args, "--net-lag") is { } lag)
        {
            var parts = lag.Split(',').Select(p => int.Parse(p, CultureInfo.InvariantCulture)).ToArray();
            Session().DevLagMs = parts[0];
            Session().DevJitterMs = parts.Length > 1 ? parts[1] : 0;
        }
        if (DevTools.Has(args, "--net-browse"))
            return;
        if (DevTools.Has(args, "--net-resume") && Net.MatchSaveStore.TryLoad() is { } save)
        {
            var resumed = Session().HostResume(name, save);
            if (resumed != Error.Ok)
                GD.PushError($"Could not resume: {resumed}");
            return;
        }
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

    private async System.Threading.Tasks.Task TakeScreenshotAndQuit(string path, double delay, string? selectType)
    {
        // Process even while the game is paused, so paused screens can be captured too.
        await ToSignal(GetTree().CreateTimer(delay, processAlways: true), SceneTreeTimer.SignalName.Timeout);
        if (selectType != null && _screen is MatchController match)
        {
            DevTools.SelectFirst(match, selectType);
            await ToSignal(GetTree().CreateTimer(0.2, processAlways: true), SceneTreeTimer.SignalName.Timeout);
        }
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var error = GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"Screenshot saved to {path}: {error}");
        QuitGame();
    }
}

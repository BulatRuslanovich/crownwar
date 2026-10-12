using System;
using System.Globalization;
using System.Linq;
using Godot;
using Lordsio.Sim.Core;
using SimVector2 = System.Numerics.Vector2;

namespace Lordsio.Core;

/// <summary>
/// Application root. Also handles developer command-line options (passed after "--"):
///   --screenshot=&lt;file.png&gt;   save a screenshot after --screenshot-delay seconds (default 2) and quit
///   --demo-move=&lt;x&gt;,&lt;y&gt;      select all player units and order them to advance (attack-move) to tile (x, y)
///   --zoom-index=&lt;n&gt;         start at camera zoom step n (0 = farthest)
///   --demo-recruit=&lt;n&gt;       select the player's castle and order n recruits there
/// </summary>
public partial class Main : Node
{
    public override void _EnterTree()
    {
        InputActions.Register();
    }

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        string? zoomIndex = Arg(args, "--zoom-index");
        if (zoomIndex != null)
            GetNode<MatchController>("Match").Camera.SetZoomIndex(int.Parse(zoomIndex, CultureInfo.InvariantCulture));

        string? demoMove = Arg(args, "--demo-move");
        if (demoMove != null)
            RunDemoMove(demoMove);

        string? demoRecruit = Arg(args, "--demo-recruit");
        if (demoRecruit != null)
            RunDemoRecruit(int.Parse(demoRecruit, CultureInfo.InvariantCulture));

        string? screenshot = Arg(args, "--screenshot");
        if (screenshot != null)
        {
            double delay = double.Parse(Arg(args, "--screenshot-delay") ?? "2", CultureInfo.InvariantCulture);
            _ = TakeScreenshotAndQuit(screenshot, delay);
        }
    }

    private void RunDemoRecruit(int count)
    {
        var match = GetNode<MatchController>("Match");
        var castle = match.Sim.Settlements.First(s => s.OwnerId == MatchController.PlayerFactionId && s.Type.Recruits.Count > 0);
        match.Selection.SelectSettlement(castle.Id);
        for (int i = 0; i < count; i++)
            match.Sim.Issue(new RecruitCommand(MatchController.PlayerFactionId, castle.Id, castle.Type.Recruits[0]));
    }

    private void RunDemoMove(string value)
    {
        var match = GetNode<MatchController>("Match");
        var parts = value.Split(',');
        var target = new SimVector2(
            float.Parse(parts[0], CultureInfo.InvariantCulture) + 0.5f,
            float.Parse(parts[1], CultureInfo.InvariantCulture) + 0.5f);
        var ids = match.Sim.Units.Where(u => u.FactionId == MatchController.PlayerFactionId).Select(u => u.Id).ToArray();
        match.Selection.Select(ids, additive: false);
        match.Sim.Issue(new MoveCommand(MatchController.PlayerFactionId, ids, target, MoveMode.AttackMove));
    }

    private async System.Threading.Tasks.Task TakeScreenshotAndQuit(string path, double delay)
    {
        await ToSignal(GetTree().CreateTimer(delay), SceneTreeTimer.SignalName.Timeout);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var error = GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"Screenshot saved to {path}: {error}");
        GetTree().Quit();
    }

    private static string? Arg(string[] args, string name)
    {
        string prefix = name + "=";
        return args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];
    }
}

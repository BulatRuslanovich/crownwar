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
///   --demo-gold=&lt;n&gt;          give the player n extra gold (for visual checks only)
///   --demo-build             order a barracks and a tower next to the player's castle
///   --seed=&lt;n&gt;               fixed seed for start positions (read by MatchController)
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

        string? demoGold = Arg(args, "--demo-gold");
        if (demoGold != null)
            GetNode<MatchController>("Match").Sim.GetFaction(MatchController.PlayerFactionId)!
                .Treasury.Add(int.Parse(demoGold, CultureInfo.InvariantCulture));

        if (args.Contains("--demo-build"))
            RunDemoBuild();

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
        var capital = match.Sim.GetBuilding(match.Sim.GetFaction(MatchController.PlayerFactionId)!.CapitalId)!;
        match.Selection.SelectBuilding(capital.Id);
        for (int i = 0; i < count; i++)
            match.Sim.Issue(new RecruitCommand(MatchController.PlayerFactionId, capital.Id, capital.Type.Recruits[0]));
    }

    /// <summary>Orders a barracks and a tower on the first free spots around the player's capital.</summary>
    private void RunDemoBuild()
    {
        var match = GetNode<MatchController>("Match");
        var sim = match.Sim;
        var capital = sim.GetBuilding(sim.GetFaction(MatchController.PlayerFactionId)!.CapitalId)!;
        var taken = new System.Collections.Generic.HashSet<Sim.World.Cell>();
        foreach (string type in new[] { "barracks", "tower" })
        {
            int size = sim.Rules.GetBuildingType(type).Size;
            for (int r = 3; r < 6; r++)
            {
                var anchor = Enumerable.Range(-r, 2 * r + 1)
                    .SelectMany(dy => Enumerable.Range(-r, 2 * r + 1).Select(dx => new Sim.World.Cell(capital.Anchor.X + dx, capital.Anchor.Y + dy)))
                    .FirstOrDefault(c => sim.CanBuild(MatchController.PlayerFactionId, type, c) == BuildError.None
                                         && !Sim.Buildings.Building.FootprintOf(c, size).Any(taken.Contains));
                if (anchor == default)
                    continue;
                sim.Issue(new BuildCommand(MatchController.PlayerFactionId, type, anchor));
                // Keep a one-tile gap so the second building does not clip the first one's footprint.
                for (int y = -1; y <= size; y++)
                    for (int x = -1; x <= size; x++)
                        taken.Add(new Sim.World.Cell(anchor.X + x, anchor.Y + y));
                break;
            }
        }
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

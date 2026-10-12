using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Lordsio.Sim.Buildings;
using Lordsio.Sim.Core;
using Lordsio.Sim.World;
using SimVector2 = System.Numerics.Vector2;

namespace Lordsio.Core;

/// <summary>
/// Developer command-line options (passed after "--"), used for automated screenshots and smoke runs.
/// Any of them except the screenshot options skips the main menu and starts a match right away.
///   --skip-menu              start a default match without the menu
///   --screenshot=&lt;file.png&gt;   save a screenshot after --screenshot-delay seconds (default 2) and quit
///   --zoom-index=&lt;n&gt;         start at camera zoom step n (0 = farthest)
///   --seed=&lt;n&gt;               fixed seed for start positions
///   --demo-gold=&lt;n&gt;          give the player n extra gold (for visual checks only)
///   --demo-build             order a barracks and a tower next to the player's castle
///   --demo-recruit=&lt;n&gt;       select the player's castle and order n recruits there
///   --demo-move=&lt;x&gt;,&lt;y&gt;      select all player units and order them to advance to tile (x, y)
///   --pause                  open the pause menu right away
///   --debug-hud              show the developer overlay (F3 toggles it in game)
///   --demo-end=victory|defeat show the end screen (view only, the match goes on)
/// </summary>
public static class DevTools
{
    public static bool WantsDirectStart(string[] args) =>
        args.Any(a => a == "--skip-menu" || a.StartsWith("--demo-", StringComparison.Ordinal)
                      || a.StartsWith("--seed=", StringComparison.Ordinal)
                      || a.StartsWith("--zoom-index=", StringComparison.Ordinal) || a == "--pause" || a == "--debug-hud");

    public static int? Seed(string[] args) => Arg(args, "--seed") is { } s ? int.Parse(s, CultureInfo.InvariantCulture) : null;

    /// <summary>Applies the demo options to a freshly started match.</summary>
    public static void Apply(MatchController match, string[] args)
    {
        if (Arg(args, "--zoom-index") is { } zoom)
            match.Camera.SetZoomIndex(int.Parse(zoom, CultureInfo.InvariantCulture));
        if (Arg(args, "--demo-gold") is { } gold)
            match.Sim.GetFaction(MatchController.PlayerFactionId)!.Treasury.Add(int.Parse(gold, CultureInfo.InvariantCulture));
        if (args.Contains("--demo-build"))
            DemoBuild(match);
        if (Arg(args, "--demo-recruit") is { } recruit)
            DemoRecruit(match, int.Parse(recruit, CultureInfo.InvariantCulture));
        if (Arg(args, "--demo-move") is { } move)
            DemoMove(match, move);
        if (args.Contains("--pause"))
            match.SetPaused(true);
        if (Arg(args, "--demo-end") is { } end)
            match.PreviewEndScreen(victory: end == "victory");
        if (args.Contains("--debug-hud"))
            match.GetNode<Godot.CanvasLayer>("DebugHud").Visible = true;
    }

    public static string? Arg(string[] args, string name)
    {
        string prefix = name + "=";
        return args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];
    }

    private static Building PlayerCapital(Simulation sim) =>
        sim.GetBuilding(sim.GetFaction(MatchController.PlayerFactionId)!.CapitalId)!;

    private static void DemoRecruit(MatchController match, int count)
    {
        var capital = PlayerCapital(match.Sim);
        match.Selection.SelectBuilding(capital.Id);
        for (int i = 0; i < count; i++)
            match.Sim.Issue(new RecruitCommand(MatchController.PlayerFactionId, capital.Id, capital.Type.Recruits[0]));
    }

    /// <summary>Orders a barracks and a tower on the first free spots around the player's capital.</summary>
    private static void DemoBuild(MatchController match)
    {
        var sim = match.Sim;
        var capital = PlayerCapital(sim);
        var taken = new HashSet<Cell>();
        foreach (string type in new[] { "barracks", "tower" })
        {
            int size = sim.Rules.GetBuildingType(type).Size;
            for (int r = 3; r < 6; r++)
            {
                var anchor = Enumerable.Range(-r, 2 * r + 1)
                    .SelectMany(dy => Enumerable.Range(-r, 2 * r + 1).Select(dx => new Cell(capital.Anchor.X + dx, capital.Anchor.Y + dy)))
                    .FirstOrDefault(c => sim.CanBuild(MatchController.PlayerFactionId, type, c) == BuildError.None
                                         && !Building.FootprintOf(c, size).Any(taken.Contains));
                if (anchor == default)
                    continue;
                sim.Issue(new BuildCommand(MatchController.PlayerFactionId, type, anchor));
                // Keep a one-tile gap so the second building does not clip the first one's footprint.
                for (int y = -1; y <= size; y++)
                    for (int x = -1; x <= size; x++)
                        taken.Add(new Cell(anchor.X + x, anchor.Y + y));
                break;
            }
        }
    }

    private static void DemoMove(MatchController match, string value)
    {
        var parts = value.Split(',');
        var target = new SimVector2(
            float.Parse(parts[0], CultureInfo.InvariantCulture) + 0.5f,
            float.Parse(parts[1], CultureInfo.InvariantCulture) + 0.5f);
        var ids = match.Sim.Units.Where(u => u.FactionId == MatchController.PlayerFactionId).Select(u => u.Id).ToArray();
        match.Selection.Select(ids, additive: false);
        match.Sim.Issue(new MoveCommand(MatchController.PlayerFactionId, ids, target, MoveMode.AttackMove));
    }
}

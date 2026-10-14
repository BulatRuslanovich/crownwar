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
///   --demo-build             order a barracks, a tower, a watchtower and a catapult next to the player's castle
///   --demo-recruit=&lt;n&gt;       select the player's castle and order n recruits there
///   --demo-move=&lt;x&gt;,&lt;y&gt;      select all player units and order them to advance to tile (x, y)
///   --demo-camera=&lt;x&gt;,&lt;y&gt;    center the camera on tile (x, y)
///   --demo-army=&lt;n&gt;          stress test: n player units and n enemy units (every kind) clash on screen
///   --pause                  open the pause menu right away
///   --debug-hud              show the developer overlay (F3 toggles it in game)
///   --demo-end=victory|defeat show the end screen (view only, the match goes on)
///   --demo-end-delay=&lt;s&gt;     show it only after s seconds of game time
///   --demo-autoplay          a Hard bot plays the player's faction (to watch a match unfold)
///   --demo-time-scale=&lt;x&gt;    run the game x times faster (timers, including --screenshot-delay, scale too)
///   --demo-select=&lt;type&gt;     right before the screenshot, select the player's first building of that type (e.g. mine)
///   --demo-upgrade           keep upgrading the player's finished towers and mines to their highest level
///   --demo-no-fog            show the whole map (no fog of war)
///   --demo-scoreboard        keep the players table (Tab) open
///   --demo-diplomacy         open the diplomacy panel (P)
///   --demo-far-build         order a barracks far from the player's troops (a soldier walks over to build it)
/// </summary>
public static class DevTools
{
    public static bool WantsDirectStart(string[] args) =>
        args.Any(a => a == "--skip-menu" || a.StartsWith("--demo-", StringComparison.Ordinal)
                      || a.StartsWith("--seed=", StringComparison.Ordinal)
                      || a.StartsWith("--zoom-index=", StringComparison.Ordinal) || a == "--pause" || a == "--debug-hud");

    public static bool WantsNetwork(string[] args) =>
        args.Any(a => a == "--net-host" || a == "--net-resume" || a == "--net-browse" || a.StartsWith("--net-join=", StringComparison.Ordinal));

    public static int? Seed(string[] args) => Arg(args, "--seed") is { } s ? int.Parse(s, CultureInfo.InvariantCulture) : null;

    /// <summary>Applies the demo options to a freshly started match.</summary>
    public static void Apply(MatchController match, string[] args)
    {
        if (Arg(args, "--zoom-index") is { } zoom)
            match.Camera.SetZoomIndex(int.Parse(zoom, CultureInfo.InvariantCulture));
        if (args.Contains("--demo-autoplay"))
            match.AddBot(match.LocalFactionId, Sim.AI.BotDifficulty.Hard);
        if (Arg(args, "--demo-time-scale") is { } scale)
            Godot.Engine.TimeScale = double.Parse(scale, CultureInfo.InvariantCulture);
        if (args.Contains("--demo-diplomacy"))
            match.GetNode<UI.Hud>("Hud").Diplomacy.Visible = true;
        if (args.Contains("--demo-scoreboard"))
            match.GetNode<UI.Scoreboard>("Hud/Scoreboard").Pinned = true;
        if (args.Contains("--demo-no-fog"))
            match.Fog.RevealAll();
        if (args.Contains("--demo-upgrade"))
            DemoUpgrade(match);
        if (Arg(args, "--demo-gold") is { } gold)
            match.Sim.GetFaction(match.LocalFactionId)!.Treasury.Add(int.Parse(gold, CultureInfo.InvariantCulture));
        if (args.Contains("--demo-build"))
            DemoBuild(match);
        if (args.Contains("--demo-far-build"))
            DemoFarBuild(match);
        if (Arg(args, "--demo-recruit") is { } recruit)
            DemoRecruit(match, int.Parse(recruit, CultureInfo.InvariantCulture));
        if (Arg(args, "--demo-camera") is { } camera)
        {
            var xy = camera.Split(',');
            match.Camera.CenterOn(SimConvert.ToWorld(new SimVector2(float.Parse(xy[0], CultureInfo.InvariantCulture), float.Parse(xy[1], CultureInfo.InvariantCulture))));
        }
        if (Arg(args, "--demo-move") is { } move)
            DemoMove(match, move);
        if (Arg(args, "--demo-army") is { } army)
            DemoArmy(match, int.Parse(army, CultureInfo.InvariantCulture));
        if (args.Contains("--pause"))
            match.SetPaused(true);
        if (Arg(args, "--demo-end") is { } end)
        {
            // After a while of play (game time), so the end screen has a match to show.
            double delay = Arg(args, "--demo-end-delay") is { } d ? double.Parse(d, CultureInfo.InvariantCulture) : 0;
            if (delay <= 0)
                match.PreviewEndScreen(victory: end == "victory");
            else
                match.GetTree().CreateTimer(delay).Timeout += () =>
                {
                    if (Godot.GodotObject.IsInstanceValid(match))
                        match.PreviewEndScreen(victory: end == "victory");
                };
        }
        if (args.Contains("--debug-hud"))
            match.GetNode<Godot.CanvasLayer>("DebugHud").Visible = true;
    }

    /// <summary>--demo-select=TYPE: shows a building's panel in a screenshot.</summary>
    public static void SelectFirst(MatchController match, string typeId)
    {
        if (match.Sim.Buildings.FirstOrDefault(b => b.FactionId == match.LocalFactionId && b.Type.Id == typeId) is { } building)
        {
            match.Selection.SelectBuilding(building.Id);
            match.Camera.CenterOn(SimConvert.ToWorld(building.Position));
        }
    }

    public static string? Arg(string[] args, string name)
    {
        string prefix = name + "=";
        return args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];
    }

    private static Building PlayerCapital(MatchController match) =>
        match.Sim.GetBuilding(match.Sim.GetFaction(match.LocalFactionId)!.CapitalId)!;

    private static void DemoRecruit(MatchController match, int count)
    {
        var capital = PlayerCapital(match);
        match.Selection.SelectBuilding(capital.Id);
        for (int i = 0; i < count; i++)
            match.Issue(new RecruitCommand(match.LocalFactionId, capital.Id, capital.Type.Recruits[0]));
    }

    /// <summary>Orders a barracks, a tower, a watchtower and a catapult on the first free spots around the player's capital.</summary>
    private static void DemoBuild(MatchController match)
    {
        var sim = match.Sim;
        var capital = PlayerCapital(match);
        var taken = new HashSet<Cell>();
        foreach (string type in new[] { "barracks", "tower", "watchtower", "catapult" })
        {
            int size = sim.Rules.GetBuildingType(type).Size;
            for (int r = 3; r < 8; r++)
            {
                var anchor = Enumerable.Range(-r, 2 * r + 1)
                    .SelectMany(dy => Enumerable.Range(-r, 2 * r + 1).Select(dx => new Cell(capital.Anchor.X + dx, capital.Anchor.Y + dy)))
                    .FirstOrDefault(c => sim.CanBuild(match.LocalFactionId, type, c) == BuildError.None
                                         && !Building.FootprintOf(c, size).Any(taken.Contains));
                if (anchor == default)
                    continue;
                sim.Issue(new BuildCommand(match.LocalFactionId, type, anchor));
                // Keep a one-tile gap so the second building does not clip the first one's footprint.
                for (int y = -1; y <= size; y++)
                    for (int x = -1; x <= size; x++)
                        taken.Add(new Cell(anchor.X + x, anchor.Y + y));
                break;
            }
        }
    }

    /// <summary>A barracks some 12 tiles from the capital, where nobody stands: the nearest soldier is sent to build it.</summary>
    private static void DemoFarBuild(MatchController match)
    {
        var sim = match.Sim;
        var capital = PlayerCapital(match);
        for (int r = 11; r < 16; r++)
        {
            var anchor = Enumerable.Range(-r, 2 * r + 1)
                .SelectMany(dy => Enumerable.Range(-r, 2 * r + 1).Select(dx => new Cell(capital.Anchor.X + dx, capital.Anchor.Y + dy)))
                .Where(c => Math.Max(Math.Abs(c.X - capital.Anchor.X), Math.Abs(c.Y - capital.Anchor.Y)) == r)
                .FirstOrDefault(c => sim.CanBuild(match.LocalFactionId, "barracks", c) == BuildError.NoUnitsNearby
                                     && sim.FindBuilder(match.LocalFactionId, "barracks", c) != null);
            if (anchor == default)
                continue;
            match.Issue(new BuildCommand(match.LocalFactionId, "barracks", anchor));
            match.Camera.CenterOn(SimConvert.ToWorld(new SimVector2((anchor.X + capital.Anchor.X) / 2f, (anchor.Y + capital.Anchor.Y) / 2f)));
            return;
        }
    }

    /// <summary>Every half second, orders the next level for each finished building of the player that has one.</summary>
    private static void DemoUpgrade(MatchController match)
    {
        if (!Godot.GodotObject.IsInstanceValid(match) || !match.IsInsideTree())
            return;
        foreach (var building in match.Sim.Buildings)
        {
            if (match.Sim.CanUpgrade(match.LocalFactionId, building.Id) == UpgradeError.None)
                match.Issue(new UpgradeCommand(match.LocalFactionId, building.Id));
        }
        match.GetTree().CreateTimer(0.5).Timeout += () => DemoUpgrade(match);
    }

    /// <summary>Stress test: two armies of <paramref name="perSide"/> units clash in the middle of the camera view.</summary>
    private static void DemoArmy(MatchController match, int perSide)
    {
        var sim = match.Sim;
        var center = SimConvert.ToSim(match.Camera.Position); // where the camera is going, even before it has moved
        // A mixed army: twice as many spearmen as anything else, and one of every other kind in turn.
        var types = sim.Rules.Units.All.SelectMany(t => t.Id == "infantry" ? new[] { t.Id, t.Id } : new[] { t.Id }).ToArray();
        var west = sim.Map.FindNearestInComponent(Cell.FromPosition(center + new SimVector2(-8f, 0f)), TileGrid.AnyComponent, 12)!.Value.Center;
        var east = sim.Map.FindNearestInComponent(Cell.FromPosition(center + new SimVector2(8f, 0f)), TileGrid.AnyComponent, 12)!.Value.Center;
        var a = Sim.Units.Formation.ComputeSlots(sim.Map, west, perSide, 0.36f)
            .Select((p, i) => sim.SpawnUnit(types[i % types.Length], match.LocalFactionId, p).Id).ToList();
        int enemy = sim.Factions.First(f => f.Id != match.LocalFactionId).Id;
        var b = Sim.Units.Formation.ComputeSlots(sim.Map, east, perSide, 0.36f)
            .Select((p, i) => sim.SpawnUnit(types[i % types.Length], enemy, p).Id).ToList();
        sim.Issue(new MoveCommand(match.LocalFactionId, a, east, MoveMode.AttackMove));
        sim.Issue(new MoveCommand(enemy, b, west, MoveMode.AttackMove));
    }

    private static void DemoMove(MatchController match, string value)
    {
        var parts = value.Split(',');
        var target = new SimVector2(
            float.Parse(parts[0], CultureInfo.InvariantCulture) + 0.5f,
            float.Parse(parts[1], CultureInfo.InvariantCulture) + 0.5f);
        var ids = match.Sim.Units.Where(u => u.FactionId == match.LocalFactionId).Select(u => u.Id).ToArray();
        match.Selection.Select(ids, additive: false);
        match.Issue(new MoveCommand(match.LocalFactionId, ids, target, MoveMode.AttackMove));
    }
}

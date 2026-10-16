using System;
using System.Linq;
using Godot;

namespace Warcrown.Core;

/// <summary>The menu backdrop: bots play on their own while the camera drifts between points of interest.</summary>
public partial class MatchController
{
    /// <summary>The menu backdrop: bots play, nobody watches the interface (see <see cref="MatchConfig.Showcase"/>).</summary>
    public bool IsShowcase => Config.Showcase;

    /// <summary>Showcase only: the match is over and a new one may take its place.</summary>
    public event Action? ShowcaseFinished;

    private Vector2 _showcaseFocus;
    private double _showcaseRetarget;
    private double _showcaseOverFor;
    private System.Numerics.Vector2? _lastFight;

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
        // The menu covers the left of the screen: keep the action in the open part on the right.
        var shift = new Vector2(GetViewportRect().Size.X * 0.2f / Camera.ZoomLevel, 0f);
        Camera.GlideTowards(_showcaseFocus - shift, 3f, delta);

        if (!Sim.IsOver)
            return;
        _showcaseOverFor += delta;
        if (_showcaseOverFor > 6.0)
        {
            _showcaseOverFor = double.NegativeInfinity; // ask once
            ShowcaseFinished?.Invoke();
        }
    }
}

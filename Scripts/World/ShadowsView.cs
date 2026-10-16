using Godot;
using Warcrown.Buildings;
using Warcrown.Core;
using Warcrown.Units;

namespace Warcrown.World;

/// <summary>
/// Shadows of buildings and units cast by the sun: each picture's silhouette laid on the ground behind it and sheared
/// to the side the sun shines from. The sun crosses the sky with the day, so shadows swing from the west in the morning
/// to the east in the evening and are longest at dawn and dusk; at night only faint moon shadows stay. Drawn as one
/// group under the actors, so overlapping shadows do not darken each other and nobody's shadow falls on a roof.
/// </summary>
public partial class ShadowsView : CanvasGroup
{
    private const float DayAlpha = 0.3f;
    private const float NightAlpha = 0.12f;
    /// <summary>How quickly a shadow follows the sun (smoothing over the day's steps and the dusk).</summary>
    private const float Follow = 0.8f;

    private MatchController _match = null!;
    private UnitsView _units = null!;
    private BuildingsView _buildings = null!;
    private Node2D _drawer = null!;
    private float _lean = -0.6f;
    private float _length = 0.55f;
    private float _alpha = DayAlpha;

    public void Initialize(MatchController match, UnitsView units, BuildingsView buildings)
    {
        _match = match;
        _units = units;
        _buildings = buildings;
        _drawer = new Node2D { Name = "Silhouettes" };
        _drawer.Draw += DrawShadows;
        AddChild(_drawer);
        FollowSun(1f / Follow);
    }

    public override void _Process(double delta)
    {
        FollowSun((float)delta);
        SelfModulate = new Color(0f, 0f, 0.05f, _alpha);
        _drawer.QueueRedraw();
    }

    private void FollowSun(float delta)
    {
        var sim = _match.Sim;
        float day = sim.DayProgress;
        // Morning: the sun in the east throws shadows west (left); evening the other way. Long when the sun is low.
        float lean = (day - 0.5f) * 1.8f;
        float length = 0.35f + 0.4f * Mathf.Abs(2f * day - 1f);
        float alpha = sim.IsNight ? NightAlpha : DayAlpha;
        float k = Smoothing.Factor(Follow, delta);
        if (!sim.IsNight)
        {
            _lean = Mathf.Lerp(_lean, lean, k);
            _length = Mathf.Lerp(_length, length, k);
        }
        _alpha = Mathf.Lerp(_alpha, alpha, k);
    }

    private void DrawShadows()
    {
        var shear = new Vector2(-_lean, _length);
        foreach (var view in _buildings.Views)
        {
            if (!view.Visible)
                continue;
            var (body, team, frame, size) = view.Picture;
            var region = new Rect2(frame * size.X, 0, size.X, size.Y);
            var rect = new Rect2(-size.X / 2f, -size.Y, size.X, size.Y);
            // A footing of shade where the walls meet the ground, then the cast shadow.
            float half = view.Building.Type.Size * SimConvert.TileSize / 2f;
            _drawer.DrawSetTransform(view.Position + new Vector2(0, -2), 0f, new Vector2(1f, 0.3f));
            _drawer.DrawCircle(Vector2.Zero, half + 2f, Colors.Black);
            _drawer.DrawSetTransformMatrix(new Transform2D(Vector2.Right, shear, view.Position));
            _drawer.DrawTextureRectRegion(body, rect, region, Colors.Black);
            _drawer.DrawTextureRectRegion(team, rect, region, Colors.Black);
        }
        // All the footings, then all the silhouettes (from one sheet): black on black in one group, the order does not
        // show, and the renderer batches each pass instead of switching for every soldier.
        foreach (var view in _units.Views)
        {
            if (!view.Visible)
                continue;
            _drawer.DrawSetTransform(view.Position + view.SpriteOffset + new Vector2(0, 0.5f), 0f, new Vector2(1f, 0.45f));
            _drawer.DrawCircle(Vector2.Zero, 4f, Colors.Black);
        }
        int w = ArtLayout.UnitCellWidth, h = ArtLayout.UnitCellHeight;
        var silhouettes = _match.UnitSheets.For(Colors.White);
        foreach (var view in _units.Views)
        {
            if (!view.Visible)
                continue;
            var feet = view.Position + view.SpriteOffset;
            var region = new Rect2(view.Frame * w, view.Row * h, w, h);
            var rect = new Rect2(-ArtLayout.UnitFeet.X, -ArtLayout.UnitFeet.Y, w, h);
            _drawer.DrawSetTransformMatrix(new Transform2D(view.FacingLeft ? Vector2.Left : Vector2.Right, shear, feet)
                .ScaledLocal(Vector2.One * view.BodyScale));
            _drawer.DrawTextureRectRegion(silhouettes, rect, region, Colors.Black);
        }
        _drawer.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }
}

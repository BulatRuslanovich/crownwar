using System;
using System.Collections.Generic;
using Godot;
using Lordsio.Core;
using Lordsio.Sim.Core;

namespace Lordsio.World;

/// <summary>
/// Short-lived pixel effects driven by simulation events: hit sparks and what flies off a struck target (blood,
/// sparks off plate, wood chips, stone splinters), dust, death puffs, collapsing buildings, a catapult stone's flash,
/// flying rocks and crater, construction sparkles and alert pings. Two instances run in a match: a ground layer
/// below the actors (rubble, craters, corpses) and an air layer above them (everything else).
/// </summary>
public partial class EffectsView : Node2D
{
    private enum Kind
    {
        Spark,
        Dust,
        Puff,
        Collapse,
        Rubble,
        Sparkle,
        Ping,
        Impact,
        /// <summary>A fallen soldier lying on the ground for a while, then fading (ground layer).</summary>
        Corpse,
        /// <summary>Bits knocked off a target, thrown up, falling and lying a moment (Size: how hard they fly).</summary>
        Debris,
        /// <summary>Rocks of a shattered catapult stone flying out of the impact.</summary>
        Chunks,
        /// <summary>The bright burst of a stone striking home (bright even at night).</summary>
        Flash,
        /// <summary>A scorched pit left by a stone, fading after a while (ground layer).</summary>
        Crater,
    }

    private record struct Effect(Kind Kind, Vector2 Position, float Age, float Duration, float Size, int Seed, Color Color);

    private static readonly Color SparkColor = new("fff2b0");
    private static readonly Color DustColor = new("b8a789");
    private static readonly Color RubbleColor = new("5f5a54");
    private static readonly Color RubbleLight = new("858079");
    private static readonly Color SparkleColor = new("ffe066");
    private static readonly Color PingColor = new("ff5a4a");
    private static readonly Color Blood = new("9c1f1f");
    private static readonly Color PlateSpark = new("fff6c8");
    private static readonly Color WoodChip = new("a87a4a");
    private static readonly Color StoneChip = new("9d968e");
    private static readonly Color RockChunk = new("6e6862");
    private static readonly Color FlashColor = new("fff1b0");
    private static readonly Color CraterColor = new("3a3128");
    private static readonly Color CraterRim = new("8a7a5e");
    private const float CraterSeconds = 14f;

    /// <summary>True for the layer drawn under units and buildings.</summary>
    [Export] public bool Ground { get; set; }

    private readonly List<Effect> _effects = new();
    private MatchController _match = null!;
    private int _seed;

    public void Initialize(MatchController match)
    {
        _match = match;
    }

    public void OnSimEvent(SimEvent e)
    {
        switch (e)
        {
            case SplashLanded splash when !Ground:
                var landed = SimConvert.ToWorld(splash.Position);
                Add(Kind.Impact, landed, 0.5f, splash.Radius * SimConvert.TileSize, DustColor);
                Add(Kind.Flash, landed + new Vector2(0, -3), 0.16f, 9f, FlashColor);
                Add(Kind.Chunks, landed, 1.1f, 1f, RockChunk);
                break;
            case SplashLanded splash when Ground:
                Add(Kind.Crater, SimConvert.ToWorld(splash.Position), CraterSeconds, splash.Radius * SimConvert.TileSize * 0.55f, CraterColor);
                break;
            case AttackLanded hit when !Ground && !hit.Splash: // a stone's victims share its one impact cloud
                bool building = _match.Sim.GetBuilding(hit.TargetId) != null;
                var at = SimConvert.ToWorld(hit.Position) + new Vector2(0, building ? -8 : -6);
                Add(building || hit.Ranged ? Kind.Dust : Kind.Spark, at, building ? 0.35f : 0.18f, building ? 6f : 3f, DustColor);
                Add(Kind.Debris, at, 0.75f, building ? 1.2f : 1f, DebrisColor(hit.TargetId, building));
                break;
            case UnitDied died when !Ground:
                Add(Kind.Puff, SimConvert.ToWorld(died.Position) + new Vector2(0, -5), 0.6f, 6f, FactionColors.Get(died.FactionId));
                break;
            case UnitDied died when Ground && died.UnitTypeId.Length > 0:
                Add(Kind.Corpse, SimConvert.ToWorld(died.Position), CorpseSeconds, Dev.ArtLayout.UnitRow(died.UnitTypeId),
                    FactionColors.Get(died.FactionId));
                break;
            case BuildingDestroyed or BuildingDemolished:
                // A demolished building comes down the same way, only on its owner's orders.
                var (simPosition, footprint) = e switch
                {
                    BuildingDestroyed d => (d.Position, d.Size),
                    BuildingDemolished m => (m.Position, m.Size),
                    _ => default,
                };
                float size = footprint * SimConvert.TileSize / 2f;
                var center = SimConvert.ToWorld(simPosition);
                if (Ground)
                    Add(Kind.Rubble, center, 30f, size, RubbleColor);
                else
                    Add(Kind.Collapse, center, 1.4f, size, DustColor);
                break;
            case BuildingCompleted completed when !Ground && _match.Sim.GetBuilding(completed.BuildingId) is { } b:
                Add(Kind.Sparkle, SimConvert.ToWorld(b.Position), 0.8f, b.Type.Size * SimConvert.TileSize / 2f, SparkleColor);
                break;
        }
    }

    /// <summary>What flies off a struck target: splinters of stone, chips of a ram's timber, sparks off plate, blood.</summary>
    private Color DebrisColor(int targetId, bool building)
    {
        if (building)
            return StoneChip;
        return _match.Sim.GetUnit(targetId)?.Type switch
        {
            { IsSiege: true } => WoodChip,
            { RangedArmor: >= 0.3f } => PlateSpark,
            _ => Blood,
        };
    }

    /// <summary>Expanding ring marking where the player is being attacked.</summary>
    public void Ping(System.Numerics.Vector2 simPosition) =>
        Add(Kind.Ping, SimConvert.ToWorld(simPosition), 2f, 24f, PingColor);

    public override void _Process(double delta)
    {
        if (_effects.Count == 0)
            return;
        for (int i = _effects.Count - 1; i >= 0; i--)
        {
            var effect = _effects[i];
            effect.Age += (float)delta;
            if (effect.Age >= effect.Duration)
                _effects.RemoveAt(i);
            else
                _effects[i] = effect;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        foreach (var e in _effects)
        {
            float t = e.Age / e.Duration;
            var rng = new Random(e.Seed);
            switch (e.Kind)
            {
                case Kind.Spark:
                    // A star of short rays that shrinks.
                    float ray = e.Size * (1f - t) + 1f;
                    for (int r = 0; r < 4; r++)
                    {
                        var dir = Vector2.Right.Rotated(Mathf.Pi / 4f * r + rng.NextSingle());
                        DrawLine(e.Position - dir * ray, e.Position + dir * ray, SparkColor with { A = 1f - t }, 1f);
                    }
                    break;
                case Kind.Dust:
                case Kind.Puff:
                    int specks = e.Kind == Kind.Puff ? 9 : 5;
                    for (int s = 0; s < specks; s++)
                    {
                        var dir = Vector2.Right.Rotated(rng.NextSingle() * Mathf.Tau);
                        var p = e.Position + dir * e.Size * t * (0.5f + rng.NextSingle()) + new Vector2(0, -4f * t);
                        var color = (s % 3 == 0 && e.Kind == Kind.Puff ? e.Color : DustColor) with { A = 0.8f * (1f - t) };
                        DrawRect(new Rect2(p, Vector2.One * 2), color);
                    }
                    break;
                case Kind.Corpse:
                    DrawCorpse(e, t);
                    break;
                case Kind.Debris:
                case Kind.Chunks:
                    DrawDebris(e, t, rng);
                    break;
                case Kind.Flash:
                    // Bright against the night too: the world's darkening is undone for it.
                    var boost = _match.LightBoost;
                    var flash = new Color(e.Color.R * boost.R, e.Color.G * boost.G, e.Color.B * boost.B, 1f - t);
                    DrawCircle(e.Position, e.Size * (0.5f + 0.5f * t), flash);
                    DrawCircle(e.Position, e.Size * 0.4f * (1f - t), new Color(boost.R, boost.G, boost.B));
                    break;
                case Kind.Crater:
                    DrawCrater(e, rng);
                    break;
                case Kind.Impact:
                    // A flattened ring of dust spreading over the splash area, with specks thrown out.
                    DrawSetTransform(e.Position, 0f, new Vector2(1f, 0.5f));
                    DrawArc(Vector2.Zero, e.Size * (0.3f + 0.7f * t), 0f, Mathf.Tau, 24, DustColor with { A = 0.7f * (1f - t) }, 2f);
                    DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
                    for (int s = 0; s < 10; s++)
                    {
                        var dir = Vector2.Right.Rotated(rng.NextSingle() * Mathf.Tau);
                        var p = e.Position + new Vector2(dir.X, dir.Y * 0.5f) * e.Size * t * (0.4f + rng.NextSingle()) + new Vector2(0, -8f * t);
                        DrawRect(new Rect2(p, Vector2.One * 2), DustColor with { A = 0.8f * (1f - t) });
                    }
                    break;
                case Kind.Collapse:
                    // A dust cloud billowing out of the footprint, with debris thrown up and falling.
                    for (int s = 0; s < 40; s++)
                    {
                        var dir = Vector2.Right.Rotated(rng.NextSingle() * Mathf.Tau);
                        float dist = e.Size * (0.3f + 1.2f * t) * rng.NextSingle();
                        var p = e.Position + dir * dist + new Vector2(0, -6f * t);
                        DrawRect(new Rect2(p, Vector2.One * (2 + rng.Next(3))), DustColor with { A = 0.7f * (1f - t) });
                    }
                    for (int s = 0; s < 10; s++)
                    {
                        float vx = (rng.NextSingle() - 0.5f) * 60f, vy = -40f - rng.NextSingle() * 40f;
                        float time = e.Age;
                        var p = e.Position + new Vector2(vx * time, vy * time + 90f * time * time);
                        DrawRect(new Rect2(p, Vector2.One * 2), RubbleColor with { A = 1f - t });
                    }
                    break;
                case Kind.Rubble:
                    // Ruins fade away over the last few seconds.
                    float alpha = Mathf.Clamp((e.Duration - e.Age) / 5f, 0f, 1f);
                    for (int s = 0; s < (int)(e.Size * 1.5f); s++)
                    {
                        var p = e.Position + new Vector2((rng.NextSingle() - 0.5f) * e.Size * 2f, (rng.NextSingle() - 0.5f) * e.Size * 1.6f);
                        var color = (s % 4 == 0 ? RubbleLight : RubbleColor) with { A = alpha };
                        DrawRect(new Rect2(p, new Vector2(2 + rng.Next(4), 2 + rng.Next(2))), color);
                    }
                    break;
                case Kind.Sparkle:
                    for (int s = 0; s < 12; s++)
                    {
                        float angle = s / 12f * Mathf.Tau;
                        var p = e.Position + Vector2.Right.Rotated(angle) * e.Size * (0.6f + 0.8f * t) + new Vector2(0, -e.Size * 0.6f);
                        DrawRect(new Rect2(p, Vector2.One * 2), e.Color with { A = 1f - t });
                    }
                    break;
                case Kind.Ping:
                    for (int ring = 0; ring < 2; ring++)
                    {
                        float k = (t * 2f + ring * 0.5f) % 1f;
                        DrawArc(e.Position, 4f + e.Size * k, 0f, Mathf.Tau, 32, e.Color with { A = 1f - k }, 2f / _match.Camera.ZoomLevel);
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Bits thrown up from the hit, falling under gravity onto the ground a few pixels below (the target's feet) and
    /// lying there a moment before fading. Chunks are a stone's rocks: more, bigger, thrown farther.
    /// </summary>
    private void DrawDebris(Effect e, float t, Random rng)
    {
        bool chunks = e.Kind == Kind.Chunks;
        int count = chunks ? 12 : 5;
        float ground = e.Position.Y + (chunks ? 2f : 6f);
        float alpha = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;
        for (int s = 0; s < count; s++)
        {
            float vx = (rng.NextSingle() - 0.5f) * (chunks ? 110f : 50f) * e.Size;
            float vy = -(chunks ? 50f : 25f) - rng.NextSingle() * (chunks ? 60f : 35f) * e.Size;
            float time = e.Age;
            var p = e.Position + new Vector2(vx * time, vy * time + 0.5f * 260f * time * time);
            if (p.Y > ground)
            {
                // Landed: it stops where it hit the ground.
                float landing = (-vy + Mathf.Sqrt(vy * vy + 2f * 260f * (ground - e.Position.Y))) / 260f;
                p = new Vector2(e.Position.X + vx * landing, ground + (s % 3) - 1);
            }
            float size = chunks ? 2 + s % 2 : 1 + (s % 3 == 0 ? 1 : 0);
            var color = s % 3 == 0 ? e.Color.Lightened(0.25f) : e.Color;
            DrawRect(new Rect2(p.Round(), new Vector2(size, size)), color with { A = alpha });
        }
    }

    /// <summary>A flattened scorched pit with a rim of thrown-up earth and clods around it.</summary>
    private void DrawCrater(Effect e, Random rng)
    {
        float alpha = Mathf.Clamp((e.Duration - e.Age) / 3f, 0f, 1f) * 0.75f;
        DrawSetTransform(e.Position, 0f, new Vector2(1f, 0.5f));
        DrawCircle(Vector2.Zero, e.Size + 1.5f, CraterRim with { A = alpha * 0.8f });
        DrawCircle(Vector2.Zero, e.Size, CraterColor with { A = alpha });
        DrawCircle(new Vector2(-e.Size * 0.25f, -e.Size * 0.25f), e.Size * 0.45f, CraterColor.Darkened(0.3f) with { A = alpha });
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        for (int s = 0; s < 10; s++)
        {
            var dir = Vector2.Right.Rotated(rng.NextSingle() * Mathf.Tau);
            var p = e.Position + new Vector2(dir.X, dir.Y * 0.5f) * e.Size * (1.2f + rng.NextSingle() * 0.8f);
            DrawRect(new Rect2(p.Round(), new Vector2(2, 1)), CraterRim with { A = alpha });
        }
    }

    private const float CorpseSeconds = 8f;
    private Texture2D? _units;
    private Texture2D? _unitsTeam;

    /// <summary>
    /// The unit's standing picture tipped onto its side (left or right at random), darkened; it lies still, then
    /// fades over its last quarter. Size carries the unit's row in the atlas.
    /// </summary>
    private void DrawCorpse(Effect e, float t)
    {
        _units ??= GD.Load<Texture2D>("res://Assets/Sprites/units.png");
        _unitsTeam ??= GD.Load<Texture2D>("res://Assets/Sprites/units_team.png");
        int w = Dev.ArtLayout.UnitCellWidth, h = Dev.ArtLayout.UnitCellHeight;
        var region = new Rect2(Dev.ArtLayout.FrameIdle * w, (int)e.Size * h, w, h);
        float alpha = t < 0.75f ? 1f : 1f - (t - 0.75f) / 0.25f;
        float side = e.Seed % 2 == 0 ? 1f : -1f;
        DrawSetTransform(e.Position + new Vector2(0, -3), side * Mathf.Pi / 2f, Vector2.One);
        var rect = new Rect2(-w / 2f, -h + 2f, w, h);
        var shade = new Color(0.62f, 0.62f, 0.62f, alpha);
        DrawTextureRectRegion(_units, rect, region, shade);
        DrawTextureRectRegion(_unitsTeam, rect, region, e.Color * shade);
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    private void Add(Kind kind, Vector2 position, float duration, float size, Color color) =>
        _effects.Add(new Effect(kind, position, 0f, duration, size, ++_seed, color));
}

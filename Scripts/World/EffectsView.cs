using System;
using System.Collections.Generic;
using Godot;
using Lordsio.Core;
using Lordsio.Sim.Core;

namespace Lordsio.World;

/// <summary>
/// Short-lived pixel effects driven by simulation events: hit sparks, dust, death puffs, collapsing
/// buildings, construction sparkles and alert pings. Two instances run in a match: a ground layer
/// below the actors (rubble) and an air layer above them (everything else).
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
    }

    private record struct Effect(Kind Kind, Vector2 Position, float Age, float Duration, float Size, int Seed, Color Color);

    private static readonly Color SparkColor = new("fff2b0");
    private static readonly Color DustColor = new("b8a789");
    private static readonly Color RubbleColor = new("5f5a54");
    private static readonly Color RubbleLight = new("858079");
    private static readonly Color SparkleColor = new("ffe066");
    private static readonly Color PingColor = new("ff5a4a");

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
                Add(Kind.Impact, SimConvert.ToWorld(splash.Position), 0.5f, splash.Radius * SimConvert.TileSize, DustColor);
                break;
            case AttackLanded hit when !Ground && !hit.Splash: // a stone's victims share its one impact cloud
                bool building = _match.Sim.GetBuilding(hit.TargetId) != null;
                var at = SimConvert.ToWorld(hit.Position) + new Vector2(0, building ? -8 : -6);
                Add(building || hit.Ranged ? Kind.Dust : Kind.Spark, at, building ? 0.35f : 0.18f, building ? 6f : 3f, DustColor);
                break;
            case UnitDied died when !Ground:
                Add(Kind.Puff, SimConvert.ToWorld(died.Position) + new Vector2(0, -5), 0.6f, 6f, FactionColors.Get(died.FactionId));
                break;
            case BuildingDestroyed destroyed:
                float size = destroyed.Size * SimConvert.TileSize / 2f;
                var center = SimConvert.ToWorld(destroyed.Position);
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

    private void Add(Kind kind, Vector2 position, float duration, float size, Color color) =>
        _effects.Add(new Effect(kind, position, 0f, duration, size, ++_seed, color));
}

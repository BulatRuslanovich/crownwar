using System;
using System.Collections.Generic;
using Godot;
using Lordsio.Core;
using Lordsio.Sim.World;

namespace Lordsio.World;

/// <summary>
/// Weather over what the camera sees, by the biome under each particle: snow falls and drifts on the wind over the
/// snowfields, sand streams low over the desert, leaves and seeds float over the meadows and, at night, fireflies
/// blink there. Each biome's weather swells and calms on its own slow cycle. Purely cosmetic: local randomness, never
/// the simulation's.
/// </summary>
public partial class WeatherView : Node2D
{
    private enum Kind { Snow, Sand, Leaf, Firefly }

    private struct Particle
    {
        public Kind Kind;
        public Vector2 Position;
        public Vector2 Velocity;
        public float Age;
        public float Life;
        public float Phase;
        public Color Color;
        public int Size;
    }

    /// <summary>Particles a second per screen of 1280×720 at full strength, by biome.</summary>
    private const float SnowRate = 70f, SandRate = 45f, LeafRate = 5f, FireflyRate = 6f;
    private const int MaxParticles = 600;

    private static readonly Color[] LeafColors = { new("6db04a"), new("c9b44a"), new("e0d07a"), new("9ccf5f") };
    private static readonly Color SnowColor = new("f4f8fb");
    private static readonly Color SandColor = new("ead2a0");
    private static readonly Color FireflyColor = new("d8ff7a");

    private readonly List<Particle> _particles = new();
    private readonly Random _random = new();
    private MatchController _match = null!;
    private float _time;
    private float _spawnDebt;

    public void Initialize(MatchController match) => _match = match;

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _time += dt;
        var view = VisibleRect();
        Spawn(view, dt);
        var wind = _match.Wind;
        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.Age += dt;
            if (p.Age >= p.Life || !view.Grow(64f).HasPoint(p.Position))
            {
                _particles.RemoveAt(i);
                continue;
            }
            switch (p.Kind)
            {
                case Kind.Snow:
                    p.Position += (p.Velocity + new Vector2(wind.X * 0.8f + Mathf.Sin(p.Age * 2.1f + p.Phase) * 6f, 0f)) * dt;
                    break;
                case Kind.Sand:
                    p.Position += (p.Velocity + wind * 2.5f) * dt;
                    break;
                case Kind.Leaf:
                    p.Position += (p.Velocity + wind * 1.2f + new Vector2(0f, Mathf.Sin(p.Age * 3f + p.Phase) * 8f)) * dt;
                    break;
                case Kind.Firefly:
                    p.Position += new Vector2(Mathf.Sin(p.Age * 0.9f + p.Phase), Mathf.Cos(p.Age * 0.7f + p.Phase * 2f)) * 7f * dt;
                    break;
            }
            _particles[i] = p;
        }
        QueueRedraw();
    }

    /// <summary>The part of the world on screen (with the camera's zoom).</summary>
    private Rect2 VisibleRect()
    {
        var size = GetViewportRect().Size / _match.Camera.ZoomLevel;
        return new Rect2(_match.Camera.GetScreenCenterPosition() - size / 2f, size);
    }

    /// <summary>How strong a biome's weather is now (0..1): each swells and calms on its own slow cycle.</summary>
    private float Strength(Biome biome) => biome switch
    {
        Biome.Winter => 0.35f + 0.65f * Wave(53f, 0f),
        Biome.Desert => Mathf.Max(0f, Wave(41f, 2f) * 1.4f - 0.3f),
        _ => 0.6f + 0.4f * Wave(37f, 4f),
    };

    private float Wave(float period, float offset) => 0.5f + 0.5f * Mathf.Sin(_time * Mathf.Tau / period + offset);

    private void Spawn(Rect2 view, float dt)
    {
        // Attempts in proportion to the area on screen; each lands on some cell and takes that biome's chance.
        float screens = view.Area / (1280f * 720f);
        _spawnDebt += dt * screens * SnowRate;
        bool night = _match.Sim.IsNight;
        var map = _match.Sim.Map;
        while (_spawnDebt >= 1f && _particles.Count < MaxParticles)
        {
            _spawnDebt -= 1f;
            var at = view.Position + new Vector2(_random.NextSingle() * view.Size.X, _random.NextSingle() * view.Size.Y);
            var cell = new Cell(Mathf.FloorToInt(at.X / SimConvert.TileSize), Mathf.FloorToInt(at.Y / SimConvert.TileSize));
            if (!map.InBounds(cell.X, cell.Y))
                continue;
            var biome = _match.BiomeAt(cell);
            float rate = biome switch
            {
                Biome.Winter => SnowRate,
                Biome.Desert => SandRate,
                _ => night ? FireflyRate : LeafRate,
            };
            if (_random.NextSingle() > rate / SnowRate * Strength(biome))
                continue;
            if (!night && biome == Biome.Meadow && map.GetTerrain(cell) is Terrain.Water)
                continue;
            _particles.Add(Make(biome, night, at));
        }
        _spawnDebt = Mathf.Min(_spawnDebt, 50f);
    }

    private Particle Make(Biome biome, bool night, Vector2 at)
    {
        float r = _random.NextSingle();
        return biome switch
        {
            Biome.Winter => new Particle
            {
                Kind = Kind.Snow, Position = at, Velocity = new Vector2(0f, 14f + r * 12f), Life = 3f + r * 3f,
                Phase = r * 10f, Color = SnowColor, Size = r < 0.25f ? 2 : 1,
            },
            Biome.Desert => new Particle
            {
                Kind = Kind.Sand, Position = at, Velocity = new Vector2(20f + r * 30f, 2f), Life = 0.8f + r * 1.2f,
                Color = SandColor, Size = 2 + (int)(r * 3),
            },
            _ when night => new Particle
            {
                Kind = Kind.Firefly, Position = at, Life = 4f + r * 4f, Phase = r * 20f, Color = FireflyColor, Size = 1,
            },
            _ => new Particle
            {
                Kind = Kind.Leaf, Position = at, Velocity = new Vector2(4f, 5f + r * 6f), Life = 4f + r * 3f, Phase = r * 10f,
                Color = LeafColors[_random.Next(LeafColors.Length)], Size = 2,
            },
        };
    }

    public override void _Draw()
    {
        var boost = _match.LightBoost;
        foreach (var p in _particles)
        {
            // Fade in and out at the ends of a particle's life.
            float t = p.Age / p.Life;
            float alpha = Mathf.Min(1f, Mathf.Min(t * 5f, (1f - t) * 4f));
            var at = p.Position.Round();
            switch (p.Kind)
            {
                case Kind.Snow:
                    DrawRect(new Rect2(at, new Vector2(p.Size, p.Size)), p.Color with { A = 0.9f * alpha });
                    break;
                case Kind.Sand:
                    DrawRect(new Rect2(at, new Vector2(p.Size, 1)), p.Color with { A = 0.55f * alpha });
                    break;
                case Kind.Leaf:
                    // A leaf turning over: wide, then edge-on.
                    bool flat = Mathf.Sin(p.Age * 5f + p.Phase) > 0f;
                    DrawRect(new Rect2(at, flat ? new Vector2(2, 1) : new Vector2(1, 2)), p.Color with { A = alpha });
                    break;
                case Kind.Firefly:
                    // Blinks on and off, glowing through the dark.
                    float blink = Mathf.Max(0f, Mathf.Sin(p.Age * 2.4f + p.Phase));
                    var glow = new Color(p.Color.R * boost.R, p.Color.G * boost.G, p.Color.B * boost.B, blink * alpha);
                    DrawRect(new Rect2(at - Vector2.One, new Vector2(3, 3)), glow with { A = glow.A * 0.25f });
                    DrawRect(new Rect2(at, Vector2.One), glow);
                    break;
            }
        }
    }
}

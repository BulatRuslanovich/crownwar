using System;
using System.Collections.Generic;
using Godot;
using Lordsio.Core;

namespace Lordsio.World;

/// <summary>
/// Shadows of clouds drifting over the map with the wind: soft-edged blobs of shade, a few hundred pixels across,
/// sliding over fields, armies and castles alike and wrapping round at the map's edges. They fade away at night, when
/// there is no sun to cast them. Purely cosmetic.
/// </summary>
public partial class CloudsView : Node2D
{
    private const int Shapes = 5;
    /// <summary>One cloud per this many square pixels of map.</summary>
    private const float AreaPerCloud = 260_000f;
    private const float DayAlpha = 0.13f;
    /// <summary>Cloud pictures are drawn at this many screen pixels per pixel, chunky like the rest of the art.</summary>
    private const float PixelScale = 3f;
    private static readonly Color Shade = new(0.02f, 0.04f, 0.12f);

    private readonly List<(Vector2 Position, int Shape, float Speed)> _clouds = new();
    private readonly List<ImageTexture> _shapes = new();
    private MatchController _match = null!;
    private Rect2 _world;
    private float _alpha = DayAlpha;

    public void Initialize(MatchController match)
    {
        _match = match;
        _world = new Rect2(Vector2.Zero, new Vector2(match.Sim.Map.Width, match.Sim.Map.Height) * SimConvert.TileSize);
        var random = new Random(match.ActualSeed);
        for (int i = 0; i < Shapes; i++)
            _shapes.Add(MakeShape(random));
        int count = Math.Max(4, (int)(_world.Area / AreaPerCloud));
        for (int i = 0; i < count; i++)
        {
            var at = new Vector2(random.NextSingle() * _world.Size.X, random.NextSingle() * _world.Size.Y);
            _clouds.Add((at, random.Next(Shapes), 0.8f + random.NextSingle() * 0.4f));
        }
    }

    /// <summary>A cloud's shadow: overlapping round puffs with a ragged rim, as an image one pixel per art pixel.</summary>
    private static ImageTexture MakeShape(Random random)
    {
        int w = 60 + random.Next(50), h = 30 + random.Next(20);
        var image = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        var puffs = new List<(float X, float Y, float R)>();
        int count = 5 + random.Next(5);
        for (int i = 0; i < count; i++)
        {
            float r = h * (0.22f + random.NextSingle() * 0.2f);
            puffs.Add((r + random.NextSingle() * (w - 2 * r), r + random.NextSingle() * (h - 2 * r), r));
        }
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float inside = 0f;
                foreach (var (px, py, r) in puffs)
                    inside = Math.Max(inside, 1f - MathF.Sqrt((x - px) * (x - px) + (y - py) * (y - py)) / r);
                // A thin lighter rim, dithered, so the edge is soft but still pixel art.
                if (inside > 0.18f || inside > 0f && (x + y) % 2 == 0)
                    image.SetPixel(x, y, new Color(1f, 1f, 1f, inside > 0.18f ? 1f : 0.5f));
            }
        return ImageTexture.CreateFromImage(image);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        float target = _match.Sim.IsNight ? 0f : DayAlpha;
        _alpha = Mathf.Lerp(_alpha, target, Smoothing.Factor(0.5f, dt));
        var wind = _match.Wind;
        var margin = new Vector2(400f, 200f);
        for (int i = 0; i < _clouds.Count; i++)
        {
            var (position, shape, speed) = _clouds[i];
            position += wind * speed * dt;
            // Wrap round the map (with room for the cloud to leave before it comes back on the other side).
            var size = _world.Size + margin * 2f;
            position = new Vector2(
                Mathf.PosMod(position.X + margin.X, size.X) - margin.X,
                Mathf.PosMod(position.Y + margin.Y, size.Y) - margin.Y);
            _clouds[i] = (position, shape, speed);
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_alpha < 0.005f)
            return;
        var color = Shade with { A = _alpha };
        var view = _match.Camera.VisibleRect;
        foreach (var (position, shape, _) in _clouds)
        {
            var texture = _shapes[shape];
            var rect = new Rect2(position.Round(), texture.GetSize() * PixelScale);
            if (rect.Intersects(view))
                DrawTextureRect(texture, rect, false, color);
        }
    }
}

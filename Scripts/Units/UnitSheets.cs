using System;
using System.Collections.Generic;
using Godot;
using Lordsio.World;

namespace Lordsio.Units;

/// <summary>
/// The unit sheet with a faction's colour baked into its mask, one texture per colour, made on first use. A unit is
/// then one sprite with one texture, so the renderer draws a whole army of a faction in few batches instead of
/// switching between the body sheet and the mask sheet for every soldier. The match owns these textures and frees
/// them when it ends (engine objects must not outlive the tree).
/// </summary>
public sealed class UnitSheets : IDisposable
{
    private readonly Dictionary<Color, ImageTexture> _byColor = new();
    private Image? _body;
    private Image? _mask;

    /// <summary>The sheet for units of <paramref name="team"/>'s colour.</summary>
    public Texture2D For(Color team)
    {
        if (_byColor.TryGetValue(team, out var texture))
            return texture;
        texture = ImageTexture.CreateFromImage(Bake(team));
        _byColor[team] = texture;
        return texture;
    }

    /// <summary>The mask tinted in <paramref name="team"/>'s colour laid over the body, as the two sprites used to draw it.</summary>
    private Image Bake(Color team)
    {
        _body ??= Load(Sprites.Units);
        _mask ??= Load(Sprites.UnitsTeam);
        int w = _body.GetWidth(), h = _body.GetHeight();
        var image = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var b = _body.GetPixel(x, y);
                var m = _mask.GetPixel(x, y);
                if (m.A <= 0f)
                {
                    image.SetPixel(x, y, b);
                    continue;
                }
                // Source-over: the tinted mask on top of the body.
                var tint = new Color(team.R * m.R, team.G * m.G, team.B * m.B);
                float a = m.A + b.A * (1f - m.A);
                var color = (tint * m.A + b * b.A * (1f - m.A)) / Mathf.Max(a, 1e-6f);
                image.SetPixel(x, y, color with { A = a });
            }
        }
        return image;
    }

    private static Image Load(string path)
    {
        var image = GD.Load<Texture2D>(path).GetImage();
        image.Decompress();
        return image;
    }

    public void Dispose()
    {
        foreach (var texture in _byColor.Values)
            texture.Dispose();
        _byColor.Clear();
        _body?.Dispose();
        _mask?.Dispose();
        _body = _mask = null;
    }
}

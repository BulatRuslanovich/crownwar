using System;
using System.Collections.Generic;
using Godot;
using Warcrown.World;

namespace Warcrown.Units;

/// <summary>
/// The unit sheets (soldiers and big beasts) with a faction's colour baked into their masks, one texture per colour and
/// sheet, made on first use. A unit is
/// then one sprite with one texture, so the renderer draws a whole army of a faction in few batches instead of
/// switching between the body sheet and the mask sheet for every soldier. The match owns these textures and frees
/// them when it ends (engine objects must not outlive the tree).
/// </summary>
public sealed class UnitSheets : IDisposable
{
    private readonly Dictionary<(Color, bool), ImageTexture> _byColor = new();
    private readonly Dictionary<bool, (Image Body, Image Mask)> _sources = new();

    /// <summary>The sheet (soldiers', or the big beasts' when <paramref name="big"/>) for units of <paramref name="team"/>'s colour.</summary>
    public Texture2D For(Color team, bool big = false)
    {
        if (_byColor.TryGetValue((team, big), out var texture))
            return texture;
        texture = ImageTexture.CreateFromImage(Bake(team, big));
        _byColor[(team, big)] = texture;
        return texture;
    }

    /// <summary>The mask tinted in <paramref name="team"/>'s colour laid over the body, as the two sprites used to draw it.</summary>
    private Image Bake(Color team, bool big)
    {
        if (!_sources.TryGetValue(big, out var source))
            _sources[big] = source = (Load(Sprites.Units(big)), Load(Sprites.Units(big, "_team")));
        var (body, mask) = source;
        int w = body.GetWidth(), h = body.GetHeight();
        var image = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var b = body.GetPixel(x, y);
                var m = mask.GetPixel(x, y);
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
        image.GenerateMipmaps(); // smooth when zoomed far out
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
        foreach (var (body, mask) in _sources.Values)
        {
            body.Dispose();
            mask.Dispose();
        }
        _sources.Clear();
    }
}

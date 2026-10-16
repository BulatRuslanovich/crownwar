using Godot;
using Warcrown.Sim.World;

namespace Warcrown.World;

/// <summary>
/// The fog of war as a texture, one texel per tile: clear where the team sees, <c>remembered</c> over explored ground
/// out of sight, <c>unexplored</c> elsewhere. Repainted only when <see cref="Visibility.Version"/> changes, in one
/// buffer and one upload instead of a call into the engine per tile.
/// </summary>
public sealed class FogPainter
{
    private readonly Color _unexplored;
    private readonly Color _remembered;
    private readonly Image _image;
    private readonly byte[] _pixels;
    private int _paintedVersion = -1;

    public FogPainter(int width, int height, Color unexplored, Color remembered)
    {
        _unexplored = unexplored;
        _remembered = remembered;
        _image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        _pixels = new byte[width * height * 4];
        Texture = ImageTexture.CreateFromImage(_image);
    }

    public ImageTexture Texture { get; }

    /// <summary>Repaints the texture if the fog changed since the last call; true if it did.</summary>
    public bool Refresh(Visibility fog)
    {
        if (fog.Version == _paintedVersion)
            return false;
        _paintedVersion = fog.Version;
        int i = 0;
        for (int y = 0; y < fog.Height; y++)
        {
            for (int x = 0; x < fog.Width; x++)
            {
                var cell = new Cell(x, y);
                var color = fog.IsVisible(cell) ? Colors.Transparent : fog.IsExplored(cell) ? _remembered : _unexplored;
                _pixels[i++] = (byte)color.R8;
                _pixels[i++] = (byte)color.G8;
                _pixels[i++] = (byte)color.B8;
                _pixels[i++] = (byte)color.A8;
            }
        }
        _image.SetData(fog.Width, fog.Height, false, Image.Format.Rgba8, _pixels);
        Texture.Update(_image);
        return true;
    }
}

using Godot;
using Lordsio.Core;
using Lordsio.Sim.World;

namespace Lordsio.World;

/// <summary>
/// The fog of war over the map: black where the player has never looked, a dark veil over explored ground
/// out of sight. One texel per tile, stretched over the map with linear filtering for soft edges; the
/// texture is rewritten only when <see cref="Visibility.Version"/> changes.
/// </summary>
public partial class FogView : Node2D
{
    private static readonly Color Unexplored = new(0.02f, 0.03f, 0.05f, 0.94f);
    private static readonly Color Remembered = new(0.02f, 0.03f, 0.05f, 0.5f);

    private MatchController _match = null!;
    private Image _image = null!;
    private byte[] _pixels = System.Array.Empty<byte>();
    private ImageTexture _texture = null!;
    private int _drawnVersion = -1;

    public void Initialize(MatchController match)
    {
        _match = match;
        var fog = match.Fog;
        _image = Image.CreateEmpty(fog.Width, fog.Height, false, Image.Format.Rgba8);
        _pixels = new byte[fog.Width * fog.Height * 4];
        _texture = ImageTexture.CreateFromImage(_image);
        TextureFilter = TextureFilterEnum.Linear; // soft edges; the pixel art below keeps its own nearest filter
        Refresh();
    }

    public override void _Process(double delta) => Refresh();

    private void Refresh()
    {
        var fog = _match.Fog;
        if (fog.Version == _drawnVersion)
            return;
        _drawnVersion = fog.Version;
        Visible = !fog.RevealedAll;
        if (!Visible)
            return;
        // One buffer and one upload instead of a call into the engine per tile.
        int i = 0;
        for (int y = 0; y < fog.Height; y++)
        {
            for (int x = 0; x < fog.Width; x++)
            {
                var cell = new Cell(x, y);
                var color = fog.IsVisible(cell) ? Colors.Transparent : fog.IsExplored(cell) ? Remembered : Unexplored;
                _pixels[i++] = (byte)color.R8;
                _pixels[i++] = (byte)color.G8;
                _pixels[i++] = (byte)color.B8;
                _pixels[i++] = (byte)color.A8;
            }
        }
        _image.SetData(fog.Width, fog.Height, false, Image.Format.Rgba8, _pixels);
        _texture.Update(_image);
        QueueRedraw();
    }

    public override void _Draw()
    {
        var fog = _match.Fog;
        DrawTextureRect(_texture, new Rect2(0, 0, fog.Width * SimConvert.TileSize, fog.Height * SimConvert.TileSize), tile: false);
    }
}

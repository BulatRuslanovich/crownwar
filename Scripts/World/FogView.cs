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
    private FogPainter _painter = null!;

    public void Initialize(MatchController match)
    {
        _match = match;
        _painter = new FogPainter(match.Fog.Width, match.Fog.Height, Unexplored, Remembered);
        TextureFilter = TextureFilterEnum.Linear; // soft edges; the pixel art below keeps its own nearest filter
        Refresh();
    }

    public override void _Process(double delta) => Refresh();

    private void Refresh()
    {
        var fog = _match.Fog;
        Visible = !fog.RevealedAll;
        if (Visible && _painter.Refresh(fog))
            QueueRedraw();
    }

    public override void _Draw()
    {
        var fog = _match.Fog;
        DrawTextureRect(_painter.Texture, new Rect2(0, 0, fog.Width * SimConvert.TileSize, fog.Height * SimConvert.TileSize), tile: false);
    }
}

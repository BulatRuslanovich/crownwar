using System.Collections.Generic;
using Godot;
using Lordsio.Core;
using Lordsio.Sim.Core;
using Lordsio.Sim.Territory;
using Lordsio.Sim.World;

namespace Lordsio.Territory;

/// <summary>
/// Tints every region in its owner's colour and outlines region borders. Rendered into a small
/// image (a few pixels per tile) that is rebuilt only when some settlement changes owner.
/// </summary>
public partial class TerritoryOverlay : Sprite2D
{
    private const int PixelsPerCell = 4;
    private const float FillAlpha = 0.22f;
    private const float OwnerBorderAlpha = 0.85f;

    private static readonly Color RegionBorder = new(0f, 0f, 0f, 0.16f);

    private readonly Dictionary<int, Settlement> _settlementsById = new();
    private Simulation _sim = null!;
    private Image _image = null!;
    private ImageTexture _texture = null!;
    private byte[] _pixels = null!;
    private int[] _drawnOwners = null!;

    public void Initialize(MatchController match)
    {
        _sim = match.Sim;
        foreach (var s in _sim.Settlements)
            _settlementsById[s.Id] = s;
        _drawnOwners = new int[_sim.Settlements.Count];

        int w = _sim.Map.Width * PixelsPerCell, h = _sim.Map.Height * PixelsPerCell;
        _pixels = new byte[w * h * 4];
        _image = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        _texture = ImageTexture.CreateFromImage(_image);
        Texture = _texture;
        Centered = false;
        Scale = Vector2.One * ((float)SimConvert.TileSize / PixelsPerCell);
        Redraw();
    }

    public override void _Process(double delta)
    {
        for (int i = 0; i < _sim.Settlements.Count; i++)
        {
            if (_sim.Settlements[i].OwnerId != _drawnOwners[i])
            {
                Redraw();
                return;
            }
        }
    }

    private void Redraw()
    {
        for (int i = 0; i < _sim.Settlements.Count; i++)
            _drawnOwners[i] = _sim.Settlements[i].OwnerId;

        int mapW = _sim.Map.Width, mapH = _sim.Map.Height;
        int w = mapW * PixelsPerCell;
        for (int cy = 0; cy < mapH; cy++)
        {
            for (int cx = 0; cx < mapW; cx++)
            {
                int region = _sim.Regions.SettlementAt(new Cell(cx, cy));
                int owner = OwnerOf(region);
                Color fill = owner == Settlement.NeutralOwner
                    ? Colors.Transparent
                    : FactionColors.Get(owner) with { A = FillAlpha };

                for (int py = 0; py < PixelsPerCell; py++)
                {
                    for (int px = 0; px < PixelsPerCell; px++)
                    {
                        Color c = fill;
                        // Edge pixels of a cell take a border colour if the neighbour across that edge differs.
                        int nx = px == 0 ? cx - 1 : px == PixelsPerCell - 1 ? cx + 1 : cx;
                        int ny = py == 0 ? cy - 1 : py == PixelsPerCell - 1 ? cy + 1 : cy;
                        c = Border(c, region, owner, nx, cy);
                        c = Border(c, region, owner, cx, ny);
                        Put((cy * PixelsPerCell + py) * w + cx * PixelsPerCell + px, c);
                    }
                }
            }
        }

        _image.SetData(w, mapH * PixelsPerCell, false, Image.Format.Rgba8, _pixels);
        _texture.Update(_image);
    }

    private Color Border(Color current, int region, int owner, int nx, int ny)
    {
        var neighbourCell = new Cell(nx, ny);
        if (!_sim.Map.InBounds(neighbourCell))
            return current;
        int neighbourRegion = _sim.Regions.SettlementAt(neighbourCell);
        if (neighbourRegion == region)
            return current;
        int neighbourOwner = OwnerOf(neighbourRegion);
        if (neighbourOwner != owner && owner != Settlement.NeutralOwner)
            return FactionColors.Get(owner) with { A = OwnerBorderAlpha };
        return current.A >= RegionBorder.A ? current.Blend(RegionBorder) : RegionBorder;
    }

    private int OwnerOf(int settlementId) =>
        _settlementsById.TryGetValue(settlementId, out var s) ? s.OwnerId : Settlement.NeutralOwner;

    private void Put(int pixel, Color c)
    {
        int i = pixel * 4;
        _pixels[i] = (byte)(c.R * 255);
        _pixels[i + 1] = (byte)(c.G * 255);
        _pixels[i + 2] = (byte)(c.B * 255);
        _pixels[i + 3] = (byte)(c.A * 255);
    }
}

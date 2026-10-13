using System.Linq;
using Godot;
using Lordsio.Core;
using Lordsio.Sim.Core;
using Lordsio.Sim.World;

namespace Lordsio.UI;

/// <summary>
/// The whole map in the corner: terrain, the fog of war, gold, buildings and units the player can see,
/// and the camera's view. Left click or drag moves the camera; right click sends the selected troops
/// (advancing, or retreating with Ctrl), like a right click on the map itself.
/// </summary>
public partial class Minimap : Control
{
    private static readonly Color ViewFrame = new(1f, 1f, 1f, 0.85f);
    private static readonly Color Gold = new("f2c230");
    private static readonly Color Unexplored = new(0.03f, 0.03f, 0.05f, 0.78f);
    private static readonly Color Remembered = new(0.03f, 0.03f, 0.05f, 0.45f);

    private MatchController _match = null!;
    private ImageTexture _terrain = null!;
    private Image _fogImage = null!;
    private ImageTexture _fog = null!;
    private byte[] _fogPixels = System.Array.Empty<byte>();
    private int _fogVersion = -1;
    private bool _dragging;

    public void Initialize(MatchController match)
    {
        _match = match;
        var map = match.Sim.Map;
        var image = Image.CreateEmpty(map.Width, map.Height, false, Image.Format.Rgba8);
        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
                image.SetPixel(x, y, TerrainColor(map.GetTerrain(new Cell(x, y))));
        _terrain = ImageTexture.CreateFromImage(image);
        _fogImage = Image.CreateEmpty(map.Width, map.Height, false, Image.Format.Rgba8);
        _fogPixels = new byte[map.Width * map.Height * 4];
        _fog = ImageTexture.CreateFromImage(_fogImage);
        TextureFilter = TextureFilterEnum.Nearest;
        MouseFilter = MouseFilterEnum.Stop;
    }

    private static Color TerrainColor(Terrain terrain) => terrain switch
    {
        Terrain.Forest => new Color("2e6a2c"),
        Terrain.Water => new Color("2f6db0"),
        Terrain.Mountain => new Color("7a746c"),
        Terrain.Road => new Color("b39461"),
        _ => new Color("5a9a3c"),
    };

    public override void _Process(double delta)
    {
        UpdateFog();
        QueueRedraw(); // units move every tick
    }

    private void UpdateFog()
    {
        var fog = _match.Fog;
        if (fog.Version == _fogVersion)
            return;
        _fogVersion = fog.Version;
        int i = 0;
        for (int y = 0; y < fog.Height; y++)
        {
            for (int x = 0; x < fog.Width; x++)
            {
                var cell = new Cell(x, y);
                var c = fog.IsVisible(cell) ? Colors.Transparent : fog.IsExplored(cell) ? Remembered : Unexplored;
                _fogPixels[i++] = (byte)c.R8;
                _fogPixels[i++] = (byte)c.G8;
                _fogPixels[i++] = (byte)c.B8;
                _fogPixels[i++] = (byte)c.A8;
            }
        }
        _fogImage.SetData(fog.Width, fog.Height, false, Image.Format.Rgba8, _fogPixels);
        _fog.Update(_fogImage);
    }

    /// <summary>Pixels per tile, keeping the map's proportions inside the control.</summary>
    private float TileScale => Mathf.Min(Size.X / _match.Sim.Map.Width, Size.Y / _match.Sim.Map.Height);

    private Vector2 Offset => (Size - new Vector2(_match.Sim.Map.Width, _match.Sim.Map.Height) * TileScale) / 2;

    private Vector2 ToMinimap(System.Numerics.Vector2 sim) => Offset + new Vector2(sim.X, sim.Y) * TileScale;

    public override void _Draw()
    {
        var map = _match.Sim.Map;
        float s = TileScale;
        var area = new Rect2(Offset, new Vector2(map.Width, map.Height) * s);
        DrawTextureRect(_terrain, area, tile: false);

        foreach (var deposit in _match.Sim.Deposits)
        {
            var color = deposit.IsExhausted ? Gold.Darkened(0.5f) : Gold;
            DrawRect(new Rect2(ToMinimap(new(deposit.Anchor.X, deposit.Anchor.Y)), Vector2.One * GoldDeposit.Size * s), color);
        }
        foreach (var building in _match.Sim.Buildings)
        {
            if (_match.IsHidden(building))
                continue;
            var color = FactionColors.Get(building.FactionId);
            var rect = new Rect2(ToMinimap(new(building.Anchor.X, building.Anchor.Y)), Vector2.One * building.Type.Size * s);
            DrawRect(rect.Grow(0.5f), Colors.Black);
            DrawRect(rect, building.IsComplete ? color : color with { A = 0.5f });
        }
        foreach (var unit in _match.Sim.Units)
        {
            if (_match.IsHidden(unit))
                continue;
            var p = ToMinimap(unit.Position);
            DrawRect(new Rect2(p - Vector2.One, Vector2.One * 2.5f), FactionColors.Get(unit.FactionId));
        }

        DrawTextureRect(_fog, area, tile: false);

        // What the camera shows.
        var camera = _match.Camera;
        var half = camera.GetViewportRect().Size / (2 * camera.ZoomLevel) / SimConvert.TileSize;
        var center = camera.GetScreenCenterPosition() / SimConvert.TileSize;
        var view = new Rect2(Offset + (center - half) * s, half * 2 * s).Intersection(area);
        DrawRect(view, ViewFrame, filled: false, width: 1f);
        DrawRect(area, new Color(0, 0, 0, 0.8f), filled: false, width: 1f);
    }

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } left:
                _dragging = left.Pressed;
                if (left.Pressed)
                    LookAt(left.Position);
                AcceptEvent();
                break;
            case InputEventMouseMotion motion when _dragging:
                LookAt(motion.Position);
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } right when _match.Selection.Count > 0:
                var mode = right.CtrlPressed ? MoveMode.AttackMove : MoveMode.Move;
                _match.Issue(new MoveCommand(_match.LocalFactionId, _match.Selection.Ids.ToArray(), ToSim(right.Position), mode));
                AcceptEvent();
                break;
        }
    }

    private System.Numerics.Vector2 ToSim(Vector2 local)
    {
        var tiles = (local - Offset) / TileScale;
        return new System.Numerics.Vector2(
            Mathf.Clamp(tiles.X, 0.5f, _match.Sim.Map.Width - 0.5f), Mathf.Clamp(tiles.Y, 0.5f, _match.Sim.Map.Height - 0.5f));
    }

    private void LookAt(Vector2 local) => _match.Camera.CenterOn(SimConvert.ToWorld(ToSim(local)));
}

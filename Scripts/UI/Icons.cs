using Godot;
using Warcrown.World;

namespace Warcrown.UI;

/// <summary>
/// Small textures for the interface, made from the game sprites: a unit's idle frame or a building,
/// with the team mask tinted in the owner's colour, and a coin for gold. Textures are reference-counted
/// and owned by the controls showing them, so nothing is cached statically (that would outlive the engine).
/// </summary>
public static class Icons
{
    /// <summary>The unit's standing frame, enlarged <paramref name="scale"/> times.</summary>
    public static Texture2D Unit(string unitTypeId, Color team, int scale = 2)
    {
        var region = new Rect2I(ArtLayout.FrameIdle * ArtLayout.UnitCellWidth, ArtLayout.UnitRow(unitTypeId) * ArtLayout.UnitCellHeight,
            ArtLayout.UnitCellWidth, ArtLayout.UnitCellHeight);
        return Compose(Sprites.Units, Sprites.UnitsTeam, region, team, scale);
    }

    /// <summary>The building sprite (its sheet's first frame) at its natural size (or scaled), tinted for its owner.</summary>
    public static Texture2D Building(string buildingTypeId, Color team, int scale = 1) =>
        Compose(Sprites.Path(buildingTypeId), Sprites.Path(buildingTypeId, "_team"), null, team, scale,
            frames: ArtLayout.BuildingFrames);

    /// <summary>A 12×12 gold coin, enlarged <paramref name="scale"/> times.</summary>
    public static Texture2D Coin(int scale = 2)
    {
        var rim = new Color("8a5a14");
        var face = UiPalette.Gold;
        var shine = new Color("fff1a8");
        var shade = new Color("c99418");
        var image = Image.CreateEmpty(12, 12, false, Image.Format.Rgba8);
        for (int y = 0; y < 12; y++)
        {
            for (int x = 0; x < 12; x++)
            {
                float dx = x - 5.5f, dy = y - 5.5f, d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d > 5.6f)
                    continue;
                var c = d > 4.6f ? rim : dx + dy < -3f ? shine : dx + dy > 3f ? shade : face;
                if (x == 6 && y >= 3 && y <= 8)
                    c = shade; // the stamp on the coin
                image.SetPixel(x, y, c);
            }
        }
        return Scaled(image, scale);
    }

    /// <param name="frames">Frames side by side in the sheet: without a region, the first one is taken.</param>
    private static Texture2D Compose(string basePath, string maskPath, Rect2I? region, Color team, int scale, int frames = 1)
    {
        var baseImage = GD.Load<Texture2D>(basePath).GetImage();
        var maskTexture = ResourceLoader.Exists(maskPath) ? GD.Load<Texture2D>(maskPath) : null;
        var mask = maskTexture?.GetImage();
        baseImage.Decompress();
        mask?.Decompress();
        var r = region ?? new Rect2I(0, 0, baseImage.GetWidth() / frames, baseImage.GetHeight());
        var image = Image.CreateEmpty(r.Size.X, r.Size.Y, false, Image.Format.Rgba8);
        for (int y = 0; y < r.Size.Y; y++)
        {
            for (int x = 0; x < r.Size.X; x++)
            {
                var b = baseImage.GetPixel(r.Position.X + x, r.Position.Y + y);
                var m = mask?.GetPixel(r.Position.X + x, r.Position.Y + y) ?? Colors.Transparent;
                image.SetPixel(x, y, m.A > 0 ? new Color(team.R * m.R, team.G * m.G, team.B * m.B, m.A) : b);
            }
        }
        return Scaled(image, scale);
    }

    private static Texture2D Scaled(Image image, int scale)
    {
        if (scale > 1)
            image.Resize(image.GetWidth() * scale, image.GetHeight() * scale, Image.Interpolation.Nearest);
        return ImageTexture.CreateFromImage(image);
    }
}

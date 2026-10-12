using Godot;

namespace Lordsio.Core;

/// <summary>
/// Player preferences, saved to user://settings.cfg and applied on start. The project's only autoload:
/// settings are needed by the menu, the pause screen and the camera alike, and outlive any one match.
/// </summary>
public partial class GameSettings : Node
{
    private const string FilePath = "user://settings.cfg";
    private const string Section = "settings";

    /// <summary>The autoloaded instance; null only while the scene tree is being torn down.</summary>
    public static GameSettings? Instance { get; private set; }

    public static readonly float[] UiScales = { 1f, 1.25f, 1.5f, 2f };

    /// <summary>"" follows the system language; otherwise a locale code such as "en" or "ru".</summary>
    public static readonly string[] Languages = { "", "en", "ru" };

    public float MasterVolume { get; set; } = 0.8f;
    public bool Fullscreen { get; set; }
    public float UiScale { get; set; } = 1f;
    public bool EdgePan { get; set; } = true;
    public string Language { get; set; } = "";

    public override void _EnterTree() => Instance = this;

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    public override void _Ready()
    {
        Load();
        Apply();
    }

    public void Apply()
    {
        AudioServer.SetBusVolumeDb(0, Mathf.LinearToDb(Mathf.Max(MasterVolume, 0.0001f)));
        AudioServer.SetBusMute(0, MasterVolume <= 0.001f);
        DisplayServer.WindowSetMode(Fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed);
        GetTree().Root.ContentScaleFactor = UiScale;
        TranslationServer.SetLocale(Language == "" ? OS.GetLocaleLanguage() : Language);
    }

    public void Save()
    {
        var config = new ConfigFile();
        config.SetValue(Section, "master_volume", MasterVolume);
        config.SetValue(Section, "fullscreen", Fullscreen);
        config.SetValue(Section, "ui_scale", UiScale);
        config.SetValue(Section, "edge_pan", EdgePan);
        config.SetValue(Section, "language", Language);
        var error = config.Save(FilePath);
        if (error != Error.Ok)
            GD.PushWarning($"Could not save settings to {FilePath}: {error}");
    }

    private void Load()
    {
        var config = new ConfigFile();
        if (config.Load(FilePath) != Error.Ok)
            return; // first run: keep defaults
        MasterVolume = Mathf.Clamp((float)config.GetValue(Section, "master_volume", MasterVolume), 0f, 1f);
        Fullscreen = (bool)config.GetValue(Section, "fullscreen", Fullscreen);
        UiScale = Mathf.Clamp((float)config.GetValue(Section, "ui_scale", UiScale), 0.5f, 3f);
        EdgePan = (bool)config.GetValue(Section, "edge_pan", EdgePan);
        Language = (string)config.GetValue(Section, "language", Language);
    }
}

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

    /// <summary>Music volume relative to the master volume.</summary>
    public float MusicVolume { get; set; } = 0.6f;

    /// <summary>Volume of the game's sounds (battle, alerts, buttons) relative to the master volume.</summary>
    public float SoundVolume { get; set; } = 0.5f;
    public bool Fullscreen { get; set; }
    public float UiScale { get; set; } = 1f;
    public bool EdgePan { get; set; } = true;
    public string Language { get; set; } = "";

    /// <summary>How hard the screen shakes at heavy blows, 0 (never) to 1 (full).</summary>
    public float ScreenShake { get; set; } = 1f;

    /// <summary>Name shown to other players in network games.</summary>
    public string PlayerName { get; set; } = PlayerNames.Sanitize(System.Environment.UserName);

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
        SetBusVolume(Bus(Audio.MusicPlayer.Bus), MusicVolume);
        SetBusVolume(Bus(Audio.SoundBank.Bus), SoundVolume);
        DisplayServer.WindowSetMode(Fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed);
        GetTree().Root.ContentScaleFactor = UiScale;
        TranslationServer.SetLocale(Language == "" ? OS.GetLocaleLanguage() : Language);
    }

    public void Save()
    {
        var config = new ConfigFile();
        config.SetValue(Section, "master_volume", MasterVolume);
        config.SetValue(Section, "music_volume", MusicVolume);
        config.SetValue(Section, "sound_volume", SoundVolume);
        config.SetValue(Section, "fullscreen", Fullscreen);
        config.SetValue(Section, "ui_scale", UiScale);
        config.SetValue(Section, "edge_pan", EdgePan);
        config.SetValue(Section, "language", Language);
        config.SetValue(Section, "screen_shake", ScreenShake);
        config.SetValue(Section, "player_name", PlayerName);
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
        MusicVolume = Mathf.Clamp((float)config.GetValue(Section, "music_volume", MusicVolume), 0f, 1f);
        SoundVolume = Mathf.Clamp((float)config.GetValue(Section, "sound_volume", SoundVolume), 0f, 1f);
        Fullscreen = (bool)config.GetValue(Section, "fullscreen", Fullscreen);
        UiScale = Mathf.Clamp((float)config.GetValue(Section, "ui_scale", UiScale), 0.5f, 3f);
        EdgePan = (bool)config.GetValue(Section, "edge_pan", EdgePan);
        Language = (string)config.GetValue(Section, "language", Language);
        ScreenShake = Mathf.Clamp((float)config.GetValue(Section, "screen_shake", ScreenShake), 0f, 1f);
        PlayerName = (string)config.GetValue(Section, "player_name", PlayerName);
    }

    /// <summary>
    /// A bus by name (created on first use, feeding the master bus): the music and the sounds each get one, so each
    /// has its own volume. No effects: the music recordings carry their own room.
    /// </summary>
    private static int Bus(string name)
    {
        int index = AudioServer.GetBusIndex(name);
        if (index < 0)
        {
            AudioServer.AddBus();
            index = AudioServer.BusCount - 1;
            AudioServer.SetBusName(index, name);
            AudioServer.SetBusSend(index, "Master");
        }
        return index;
    }

    private static void SetBusVolume(int bus, float volume)
    {
        AudioServer.SetBusVolumeDb(bus, Mathf.LinearToDb(Mathf.Max(volume, 0.0001f)));
        AudioServer.SetBusMute(bus, volume <= 0.001f);
    }
}

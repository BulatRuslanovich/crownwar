using System.Collections.Generic;
using Godot;
using Lordsio.Core;
using Lordsio.Sim.Core;
using SimVector2 = System.Numerics.Vector2;

namespace Lordsio.Audio;

/// <summary>
/// Turns simulation events into sounds. World sounds play positionally and only near the screen;
/// each sound has a minimum interval so a big battle stays a battle, not white noise.
/// Uses a fixed set of players instead of one node per sound.
/// </summary>
public partial class MatchSounds : Node
{
    private const int WorldVoices = 12;
    /// <summary>World sounds further than this from the screen edge (pixels) are skipped.</summary>
    private const float AudibleMargin = 160f;

    private static readonly Dictionary<Sound, double> MinInterval = new()
    {
        [Sound.SwordHit] = 0.06,
        [Sound.ArrowShot] = 0.07,
        [Sound.ArrowHit] = 0.07,
        [Sound.BuildingHit] = 0.12,
        [Sound.UnitDeath] = 0.1,
        [Sound.UnitReady] = 0.6,
    };

    private readonly List<AudioStreamPlayer2D> _world = new();
    private readonly Dictionary<Sound, double> _lastPlayed = new();
    private readonly RandomNumberGenerator _rng = new();
    private AudioStreamPlayer _ui = null!;
    private MatchController _match = null!;
    private int _nextVoice;
    private bool _enabled;
    private Dictionary<Sound, AudioStreamWav> _streams = new();

    public void Initialize(MatchController match)
    {
        _match = match;
        for (int i = 0; i < WorldVoices; i++)
        {
            var player = new AudioStreamPlayer2D { MaxDistance = 1400f, Attenuation = 1.5f };
            AddChild(player);
            _world.Add(player);
        }
        // Without a display (automated runs) nothing is heard: skip audio entirely.
        _enabled = DisplayServer.GetName() != "headless";
        _streams = SoundBank.CreateAll();
        _ui = new AudioStreamPlayer();
        AddChild(_ui);
    }

    public void OnSimEvent(SimEvent e)
    {
        int player = _match.LocalFactionId;
        switch (e)
        {
            case SplashLanded splash:
                PlayAt(Sound.BuildingHit, splash.Position, 0f);
                break;
            case AttackLanded { Splash: true }:
                break; // the stone's own thud covers everyone it hit
            case AttackLanded hit:
                bool building = _match.Sim.GetBuilding(hit.TargetId) != null;
                PlayAt(building ? Sound.BuildingHit : hit.Ranged ? Sound.ArrowHit : Sound.SwordHit, hit.Position, -4f);
                break;
            case ProjectileFired shot:
                PlayAt(Sound.ArrowShot, shot.Position, -8f);
                break;
            case UnitDied died:
                PlayAt(Sound.UnitDeath, died.Position, -6f);
                break;
            case BuildingDestroyed destroyed:
                PlayAt(Sound.BuildingDestroyed, destroyed.Position, 2f, margin: 600f);
                break;
            case BuildingPlaced placed when placed.FactionId == player:
                PlayUi(Sound.BuildingPlaced, -6f);
                break;
            case BuildingCompleted completed when completed.FactionId == player:
                PlayUi(Sound.BuildingCompleted, -4f);
                break;
            case UnitRecruited recruited when recruited.FactionId == player:
                PlayUi(Sound.UnitReady, -10f);
                break;
            case FactionEliminated eliminated when eliminated.FactionId == player:
                PlayUi(Sound.Defeat, 0f);
                break;
            case MatchEnded ended:
                PlayUi(ended.WinningTeam == _match.LocalTeam ? Sound.Victory : Sound.Defeat, 0f);
                break;
        }
    }

    public void PlayAlert() => PlayUi(Sound.Alert, -2f);

    /// <summary>Stops playback and releases the generated streams when the match goes away.</summary>
    public override void _ExitTree()
    {
        foreach (var voice in _world)
        {
            voice.Stop();
            voice.Stream = null;
        }
        if (_ui != null)
        {
            _ui.Stop();
            _ui.Stream = null;
        }
        foreach (var stream in _streams.Values)
            stream.Dispose();
        _streams.Clear();
    }

    private void PlayAt(Sound sound, SimVector2 simPosition, float volumeDb, float margin = AudibleMargin)
    {
        var position = SimConvert.ToWorld(simPosition);
        if (!_enabled || !IsNearScreen(position, margin) || !Throttle(sound))
            return;
        var voice = _world[_nextVoice];
        _nextVoice = (_nextVoice + 1) % _world.Count;
        voice.Stream = _streams[sound];
        voice.GlobalPosition = position;
        voice.VolumeDb = volumeDb;
        voice.PitchScale = _rng.RandfRange(0.92f, 1.08f); // keeps repeated hits from sounding mechanical
        voice.Play();
    }

    private void PlayUi(Sound sound, float volumeDb)
    {
        if (!_enabled || !Throttle(sound))
            return;
        _ui.Stream = _streams[sound];
        _ui.VolumeDb = volumeDb;
        _ui.Play();
    }

    private bool Throttle(Sound sound)
    {
        double now = Time.GetTicksMsec() / 1000.0;
        if (_lastPlayed.TryGetValue(sound, out double last) && now - last < MinInterval.GetValueOrDefault(sound, 0.0))
            return false;
        _lastPlayed[sound] = now;
        return true;
    }

    private bool IsNearScreen(Vector2 world, float margin)
    {
        var camera = _match.Camera;
        var half = camera.GetViewportRect().Size / (2f * camera.ZoomLevel);
        var center = camera.GetScreenCenterPosition();
        return Mathf.Abs(world.X - center.X) <= half.X + margin && Mathf.Abs(world.Y - center.Y) <= half.Y + margin;
    }
}

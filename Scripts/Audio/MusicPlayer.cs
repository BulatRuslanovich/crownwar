using System;
using System.Collections.Generic;
using Godot;

namespace Lordsio.Audio;

public enum MusicTrack
{
    Menu,
    Day,
    Night,
}

/// <summary>
/// Plays the music on the "Music" bus. A change of <see cref="Track"/> crossfades to the other piece; within the day
/// and the night piece, <see cref="Intensity"/> (0 calm, 1 tension, 2 battle) crossfades between its three versions,
/// which play in sync (one <see cref="AudioStreamSynchronized"/>), so the music turns tense or martial mid-phrase
/// instead of starting over. Lives under <see cref="Core.Main"/> so it keeps playing across screens.
/// </summary>
/// <remarks>
/// The pieces are recorded loops (Assets/Music, rendered by tools/MusicStudio from the VSCO 2 Community Edition
/// samples): a version shares every note of the calmer one it grows from, so a crossfade between them only brings
/// parts in or out.
/// </remarks>
public partial class MusicPlayer : Node
{
    public const string Bus = "Music";
    private const string Folder = "res://Assets/Music/";
    private const float TrackFadeSeconds = 2.5f;
    /// <summary>The music answers a fight quickly and calms down slowly.</summary>
    private const float RiseSeconds = 1.2f;
    private const float FallSeconds = 4f;
    private static readonly string[] Layers = { "calm", "tension", "battle" };

    private readonly Dictionary<MusicTrack, AudioStream> _streams = new();
    /// <summary>Every stream wrapper made, to let go of at exit (the layered pieces hold three loops each).</summary>
    private readonly List<AudioStream> _owned = new();
    private AudioStreamPlayer _current = null!;
    private AudioStreamPlayer _previous = null!;
    private MusicTrack? _playing;
    /// <summary>How loud each version of the playing piece is (linear, 0..1).</summary>
    private readonly float[] _layerGain = { 1f, 0f, 0f };

    /// <summary>What should be playing; changes fade over.</summary>
    public MusicTrack Track { get; set; } = MusicTrack.Menu;

    /// <summary>How tense the day or night music is: 0 calm, 1 tension, 2 battle.</summary>
    public int Intensity { get; set; }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always; // keeps fading while the game is paused
        _current = MakePlayer();
        _previous = MakePlayer();
    }

    public override void _Process(double delta)
    {
        if (_playing != Track)
        {
            // Swap: the old piece fades out on the spare player, the new one fades in at its present intensity.
            (_current, _previous) = (_previous, _current);
            _current.Stream = Stream(Track);
            _current.VolumeDb = -60f;
            for (int i = 0; i < _layerGain.Length; i++)
                _layerGain[i] = i == Math.Clamp(Intensity, 0, 2) ? 1f : 0f;
            ApplyLayers(_current);
            _current.Play();
            _playing = Track;
        }

        float step = (float)delta / TrackFadeSeconds;
        Fade(_current, +step);
        Fade(_previous, -step);
        if (_previous.Playing && Amplitude(_previous) <= 0.001f)
        {
            _previous.Stop();
            _previous.Stream = null;
        }

        // The version that matches the intensity comes up, the others go down.
        int wanted = Math.Clamp(Intensity, 0, 2);
        for (int i = 0; i < _layerGain.Length; i++)
        {
            float target = i == wanted ? 1f : 0f;
            float rate = (float)delta / (target > _layerGain[i] ? RiseSeconds : FallSeconds);
            _layerGain[i] = Mathf.MoveToward(_layerGain[i], target, rate);
        }
        ApplyLayers(_current);
    }

    /// <summary>The piece's stream, loaded on first use: one loop, or the three versions in sync.</summary>
    private AudioStream Stream(MusicTrack track)
    {
        if (_streams.TryGetValue(track, out var stream))
            return stream;
        string name = track.ToString().ToLowerInvariant();
        if (track == MusicTrack.Menu)
            stream = Loop(name);
        else
        {
            var layered = new AudioStreamSynchronized { StreamCount = Layers.Length };
            for (int i = 0; i < Layers.Length; i++)
                layered.SetSyncStream(i, Loop($"{name}_{Layers[i]}"));
            stream = layered;
            _owned.Add(layered);
        }
        _streams[track] = stream;
        return stream;
    }

    private AudioStreamOggVorbis Loop(string name)
    {
        var stream = GD.Load<AudioStreamOggVorbis>(Folder + name + ".ogg");
        stream.Loop = true;
        _owned.Add(stream);
        return stream;
    }

    /// <summary>Sets the versions' volumes from <see cref="_layerGain"/>, if the player holds a layered piece.</summary>
    private void ApplyLayers(AudioStreamPlayer player)
    {
        if (player.Stream is not AudioStreamSynchronized layered)
            return;
        for (int i = 0; i < _layerGain.Length; i++)
            layered.SetSyncStreamVolume(i, Mathf.LinearToDb(Mathf.Max(_layerGain[i], 0.0001f)));
    }

    private AudioStreamPlayer MakePlayer()
    {
        var player = new AudioStreamPlayer { Bus = Bus, VolumeDb = -60f };
        AddChild(player);
        return player;
    }

    private static float Amplitude(AudioStreamPlayer player) => Mathf.DbToLinear(player.VolumeDb);

    private static void Fade(AudioStreamPlayer player, float step) =>
        player.VolumeDb = Mathf.LinearToDb(Mathf.Clamp(Amplitude(player) + step, 0.0001f, 1f));

    /// <summary>Silences the music at once (before quitting).</summary>
    public void StopAll()
    {
        StopPlayers();
        _playing = null;
        Track = MusicTrack.Menu;
        SetProcess(false);
    }

    private void StopPlayers()
    {
        foreach (var player in new[] { _current, _previous })
        {
            player.Stop();
            player.Stream = null;
        }
    }

    /// <summary>Lets go of the streams (engine objects must not outlive the tree).</summary>
    public override void _ExitTree()
    {
        StopPlayers();
        _streams.Clear();
        foreach (var stream in _owned)
            stream.Dispose();
        _owned.Clear();
    }
}

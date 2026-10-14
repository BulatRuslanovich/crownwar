using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace Lordsio.Audio;

/// <summary>
/// Plays the music on the "Music" bus and crossfades when <see cref="Track"/> changes. Lives under
/// <see cref="Core.Main"/> so it keeps playing across screens. Tracks are composed on worker threads at
/// startup (<see cref="MusicComposer"/> is pure C#); a track starts as soon as it is ready.
/// </summary>
public partial class MusicPlayer : Node
{
    public const string Bus = "Music";
    private const float FadeSeconds = 2f;

    private readonly Dictionary<MusicTrack, Task<float[]>> _rendering = new();
    private readonly Dictionary<MusicTrack, AudioStreamWav> _streams = new();
    private AudioStreamPlayer _current = null!;
    private AudioStreamPlayer _previous = null!;
    private MusicTrack? _playing;

    /// <summary>What should be playing; changes fade over.</summary>
    public MusicTrack Track { get; set; } = MusicTrack.Menu;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always; // keeps fading while the game is paused
        _current = MakePlayer();
        _previous = MakePlayer();
        foreach (var track in Enum.GetValues<MusicTrack>())
            _rendering[track] = Task.Run(() => MusicComposer.Render(track));
    }

    public override void _Process(double delta)
    {
        if (_playing != Track && Stream(Track) is { } stream)
        {
            // Swap: the old track fades out on the spare player, the new one fades in.
            (_current, _previous) = (_previous, _current);
            _current.Stream = stream;
            _current.VolumeDb = -60f;
            _current.Play();
            _playing = Track;
        }

        float step = (float)delta / FadeSeconds;
        Fade(_current, +step);
        Fade(_previous, -step);
        if (_previous.Playing && Amplitude(_previous) <= 0.001f)
            _previous.Stop();
    }

    /// <summary>The finished stream of a track, built on the main thread once its rendering is done.</summary>
    private AudioStreamWav? Stream(MusicTrack track)
    {
        if (_streams.TryGetValue(track, out var stream))
            return stream;
        if (!_rendering.TryGetValue(track, out var task) || !task.IsCompleted)
            return null;
        stream = ToStream(task.Result);
        _streams[track] = stream;
        return stream;
    }

    /// <summary>Interleaved stereo samples (left, right, …) as a looping 16-bit stream.</summary>
    private static AudioStreamWav ToStream(float[] samples)
    {
        var data = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short s = (short)Math.Clamp(samples[i] * short.MaxValue, short.MinValue, short.MaxValue);
            data[i * 2] = (byte)s;
            data[i * 2 + 1] = (byte)(s >> 8);
        }
        return new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = MusicComposer.Rate,
            Stereo = true,
            Data = data,
            LoopMode = AudioStreamWav.LoopModeEnum.Forward,
            LoopBegin = 0,
            LoopEnd = samples.Length / 2, // in frames (a left and a right sample each)
        };
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
        foreach (var player in new[] { _current, _previous })
        {
            player.Stop();
            player.Stream = null;
        }
        _playing = null;
        Track = MusicTrack.Menu;
        SetProcess(false);
    }

    /// <summary>Releases the streams with the music (engine objects must not outlive the tree).</summary>
    public override void _ExitTree()
    {
        foreach (var player in new[] { _current, _previous })
        {
            player.Stop();
            player.Stream = null;
        }
        foreach (var stream in _streams.Values)
            stream.Dispose();
        _streams.Clear();
    }

    /// <summary>Dev-only: renders every track to <paramref name="directory"/> as .wav files for listening.</summary>
    public static void ExportAll(string directory)
    {
        foreach (var track in Enum.GetValues<MusicTrack>())
        {
            using var stream = ToStream(MusicComposer.Render(track));
            var error = stream.SaveToWav(System.IO.Path.Combine(directory, $"music_{track}.wav"));
            GD.Print($"music {track}: {error}");
        }
    }
}

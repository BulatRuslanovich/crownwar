namespace Lordsio.MusicStudio;

/// <summary>
/// An instrument built from recorded notes: a note is played from the nearest recorded pitch of the closest
/// dynamic layer, resampled to the exact pitch, and stopped with a short release. Every recording is trimmed to its
/// onset, tuned by its measured pitch (when the measurement agrees with its name) and levelled so the dynamic layers
/// join smoothly; takes of the same note and layer alternate.
/// </summary>
public sealed class Sampler
{
    /// <summary>A recording: <paramref name="Midi"/> is its true pitch (fractional after tuning).</summary>
    private sealed record Zone(Stereo Audio, double Midi, int Layer, float Gain);

    private readonly List<Zone> _zones = new();
    private readonly int _layers;
    private readonly Dictionary<(int Midi, int Layer), int> _takes = new();

    public string Name { get; }

    /// <summary>Release time (seconds) after the note ends, unless a note asks for its own.</summary>
    public double Release { get; init; } = 0.25;

    /// <summary>Overall gain, to balance the instrument against the others.</summary>
    public float Volume { get; init; } = 1f;

    /// <param name="octaveShift">Semitones to add to the names (the VSCO contrabass is named an octave low).</param>
    /// <param name="maxSeconds">Recordings are cut (with a fade) to this length to save memory.</param>
    public Sampler(string name, string cache, string folder, int octaveShift = 0, double maxSeconds = 12)
    {
        Name = name;
        var files = Vsco.List(cache, folder);
        var ranks = files.Select(f => f.Dynamic).Distinct().Order().ToList();
        _layers = ranks.Count;

        var loaded = new List<(Stereo Audio, double Midi, int Layer, float Rms)>();
        foreach (var file in files)
        {
            var raw = Wav.Read(file.Path);
            int named = file.Midi + octaveShift;
            double measured = MeasuredMidi(raw, named);
            var audio = Trim(raw, Pitch.Onset(raw, 0.01f), maxSeconds);
            loaded.Add((audio, measured, ranks.IndexOf(file.Dynamic), Rms(audio, 0.3)));
        }

        // Level the layers: the loudest layer keeps its loudness, softer ones are set by their rank, so a velocity
        // picks a timbre and the gain only bridges the small step to the next layer.
        float reference = loaded.Where(z => z.Layer == _layers - 1).Select(z => z.Rms).DefaultIfEmpty(0.1f).Average();
        foreach (var z in loaded)
        {
            float target = reference * MathF.Pow(LayerVelocity(z.Layer), 1.6f);
            _zones.Add(new Zone(z.Audio, z.Midi, z.Layer, z.Rms > 1e-5f ? target / z.Rms : 1f));
        }
        Console.WriteLine($"  {name}: {_zones.Count} recordings, {_layers} layers, notes {_zones.Min(z => z.Midi):F0}–{_zones.Max(z => z.Midi):F0}");
    }

    private float LayerVelocity(int layer) => (layer + 1f) / _layers;

    /// <summary>
    /// Plays a note into <paramref name="mix"/> from <paramref name="start"/> for <paramref name="seconds"/> (then the
    /// release). <paramref name="velocity"/> 0..1 picks the layer and the loudness; <paramref name="attack"/> fades in
    /// (a soft, legato entry) and <paramref name="skip"/> starts that far into the recording.
    /// </summary>
    public void Play(Mix mix, double start, double seconds, double midi, float velocity, float pan = 0f,
                     double attack = 0, double skip = 0, double? release = null)
    {
        int layer = Math.Clamp((int)MathF.Ceiling(velocity * _layers) - 1, 0, _layers - 1);
        var zone = Nearest(midi, layer);
        float gain = Volume * zone.Gain * MathF.Pow(velocity / LayerVelocity(layer), 1.6f);
        double ratio = Math.Pow(2, (midi - zone.Midi) / 12) * zone.Audio.Rate / mix.Rate;
        double rel = release ?? Release;

        var (pl, pr) = Mix.Pan(pan);
        int at = (int)Math.Round(start * mix.Rate);
        int hold = (int)(seconds * mix.Rate), fade = Math.Max(1, (int)(rel * mix.Rate)), rise = (int)(attack * mix.Rate);
        double pos = skip * zone.Audio.Rate;
        var src = zone.Audio;
        for (int i = 0; i < hold + fade; i++, pos += ratio)
        {
            int k = (int)pos;
            if (k + 2 >= src.Length)
                break;
            float env = i < hold ? 1f : Damp(i - hold, fade);
            if (i < rise)
                env *= i / (float)rise;
            double frac = pos - k;
            mix.Add(at + i, Hermite(src.Left, k, frac) * gain * env * pl, Hermite(src.Right, k, frac) * gain * env * pr);
        }
    }

    /// <summary>Exponential-ish damper: −40 dB by the end of the release.</summary>
    private static float Damp(int i, int length) => MathF.Exp(-4.6f * i / length) * (1f - i / (float)length);

    private Zone Nearest(double midi, int layer)
    {
        Zone? best = null;
        double bestCost = double.MaxValue;
        foreach (var z in _zones)
        {
            // Prefer the right layer, but not at the price of a far transposition (it shifts the body of the tone):
            // a recording more than three semitones away loses to a neighbouring layer at the right pitch.
            double cost = Math.Abs(z.Midi - midi) + 3 * Math.Abs(z.Layer - layer);
            if (cost < bestCost - 1e-9)
            {
                bestCost = cost;
                best = z;
            }
        }
        // Alternate the takes of that note and layer.
        var takes = _zones.Where(z => Math.Abs(z.Midi - best!.Midi) < 0.5 && z.Layer == best.Layer).ToList();
        var key = ((int)Math.Round(best!.Midi), best.Layer);
        _takes.TryGetValue(key, out int n);
        _takes[key] = n + 1;
        return takes[n % takes.Count];
    }

    /// <summary>
    /// The pitch measured near the name (at the moment the recording is most clearly periodic: a pizzicato settles
    /// after its attack), or the name itself if the recording is too noisy to measure.
    /// </summary>
    private static double MeasuredMidi(Stereo audio, int named)
    {
        var best = Pitch.Clearest(audio, named);
        return best.Dip < 0.2 ? best.Midi : named;
    }

    private static Stereo Trim(Stereo s, int from, double maxSeconds)
    {
        int length = Math.Min(s.Length - from, (int)(maxSeconds * s.Rate));
        var l = new float[length];
        var r = new float[length];
        Array.Copy(s.Left, from, l, 0, length);
        Array.Copy(s.Right, from, r, 0, length);
        int fade = Math.Min(length, s.Rate / 2);
        for (int i = 0; i < fade; i++)
        {
            float g = i / (float)fade;
            l[length - 1 - i] *= g;
            r[length - 1 - i] *= g;
        }
        return new Stereo(l, r, s.Rate);
    }

    private static float Rms(Stereo s, double seconds)
    {
        int n = Math.Min(s.Length, (int)(seconds * s.Rate));
        double sum = 0;
        for (int i = 0; i < n; i++)
            sum += (s.Left[i] * s.Left[i] + s.Right[i] * s.Right[i]) * 0.5;
        return n > 0 ? (float)Math.Sqrt(sum / n) : 0f;
    }

    /// <summary>Cubic Hermite interpolation between x[k] and x[k+1].</summary>
    private static float Hermite(float[] x, int k, double t)
    {
        float xm1 = k > 0 ? x[k - 1] : x[k], x0 = x[k], x1 = x[k + 1], x2 = x[k + 2];
        float c1 = 0.5f * (x1 - xm1);
        float c2 = xm1 - 2.5f * x0 + 2f * x1 - 0.5f * x2;
        float c3 = 0.5f * (x2 - xm1) + 1.5f * (x0 - x1);
        float f = (float)t;
        return ((c3 * f + c2) * f + c1) * f + x0;
    }
}

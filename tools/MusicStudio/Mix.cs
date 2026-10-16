namespace Warcrown.MusicStudio;

/// <summary>A stereo buffer that instruments play into, with a beat grid (4/4 by default) for placing notes.</summary>
public sealed class Mix
{
    public const int DefaultRate = 44100;

    private float[] _left;
    private float[] _right;

    public int Rate { get; }
    public double Bpm { get; }
    public int BeatsPerBar { get; }

    public Mix(double bpm, double seconds, int beatsPerBar = 4, int rate = DefaultRate)
    {
        Rate = rate;
        Bpm = bpm;
        BeatsPerBar = beatsPerBar;
        _left = new float[(int)(seconds * rate)];
        _right = new float[_left.Length];
    }

    public double BeatSeconds => 60.0 / Bpm;

    /// <summary>Length in frames.</summary>
    public int Length => _left.Length;

    /// <summary>Replaces the contents (both channels the same length).</summary>
    public void Load(float[] left, float[] right)
    {
        _left = left;
        _right = right;
    }

    /// <summary>Seconds at a bar and a beat within it (both from 0).</summary>
    public double Time(int bar, double beat) => (bar * BeatsPerBar + beat) * BeatSeconds;

    public double Seconds(double beats) => beats * BeatSeconds;

    public void Add(int index, float left, float right)
    {
        if ((uint)index >= (uint)_left.Length)
            return;
        _left[index] += left;
        _right[index] += right;
    }

    /// <summary>Equal-power gains of a position (−1 left … +1 right).</summary>
    public static (float Left, float Right) Pan(float pan)
    {
        float angle = (Math.Clamp(pan, -1f, 1f) + 1f) * MathF.PI / 4f;
        return (MathF.Cos(angle) * MathF.Sqrt(2f), MathF.Sin(angle) * MathF.Sqrt(2f));
    }

    /// <summary>Adds another mix (same rate) scaled by <paramref name="gain"/>.</summary>
    public void Add(Mix other, float gain = 1f)
    {
        int n = Math.Min(_left.Length, other._left.Length);
        for (int i = 0; i < n; i++)
        {
            _left[i] += other._left[i] * gain;
            _right[i] += other._right[i] * gain;
        }
    }

    /// <summary>Sends the mix through <see cref="Reverb"/> and adds the result back with <paramref name="wet"/>.</summary>
    public void AddReverb(float wet, float roomSize = 0.82f, float damping = 0.35f)
    {
        var reverb = new Reverb(Rate, roomSize, damping);
        for (int i = 0; i < _left.Length; i++)
        {
            var (l, r) = reverb.Process(_left[i], _right[i]);
            _left[i] += l * wet;
            _right[i] += r * wet;
        }
    }

    public float Peak()
    {
        float peak = 0f;
        for (int i = 0; i < _left.Length; i++)
            peak = MathF.Max(peak, MathF.Max(MathF.Abs(_left[i]), MathF.Abs(_right[i])));
        return peak;
    }

    /// <summary>RMS level in dBFS, over the whole buffer or from one time to another (seconds).</summary>
    public float Rms(double from = 0, double to = double.MaxValue)
    {
        int a = Math.Clamp((int)(from * Rate), 0, _left.Length), b = Math.Clamp((int)Math.Min(to * Rate, _left.Length), a, _left.Length);
        double sum = 0;
        for (int i = a; i < b; i++)
            sum += (_left[i] * _left[i] + _right[i] * _right[i]) * 0.5;
        return 10f * MathF.Log10((float)(sum / Math.Max(1, b - a)) + 1e-12f);
    }

    public void Scale(float gain)
    {
        for (int i = 0; i < _left.Length; i++)
        {
            _left[i] *= gain;
            _right[i] *= gain;
        }
    }

    /// <summary>
    /// Rounds off peaks above <paramref name="threshold"/> (tanh knee, never past 0.95) instead of clipping them;
    /// returns how many samples it touched.
    /// </summary>
    public int SoftLimit(float threshold)
    {
        float room = 0.95f - threshold;
        int touched = 0;
        foreach (var channel in new[] { _left, _right })
            for (int i = 0; i < channel.Length; i++)
            {
                float a = MathF.Abs(channel[i]);
                if (a <= threshold)
                    continue;
                channel[i] = MathF.CopySign(threshold + room * MathF.Tanh((a - threshold) / room), channel[i]);
                touched++;
            }
        return touched;
    }

    public Stereo ToStereo() => new(_left, _right, Rate);
}

namespace Warcrown.MusicStudio;

/// <summary>Pitch and onset helpers, used to check that each sample really is the note its file name says.</summary>
public static class Pitch
{
    /// <summary>First frame whose level reaches <paramref name="threshold"/> of the peak (the start of the attack).</summary>
    public static int Onset(Stereo s, float threshold = 0.02f)
    {
        float peak = 0f;
        for (int i = 0; i < s.Length; i++)
            peak = MathF.Max(peak, MathF.Max(MathF.Abs(s.Left[i]), MathF.Abs(s.Right[i])));
        for (int i = 0; i < s.Length; i++)
            if (MathF.Max(MathF.Abs(s.Left[i]), MathF.Abs(s.Right[i])) >= peak * threshold)
                return i;
        return 0;
    }

    /// <summary>
    /// Fundamental (YIN: cumulative-mean-normalised difference with a threshold) of a window starting
    /// <paramref name="delaySeconds"/> after the onset, as a fractional MIDI note; NaN if no clear pitch.
    /// </summary>
    public static double DetectMidi(Stereo s, double delaySeconds = 0.25, double minHz = 28, double maxHz = 4500)
    {
        int maxLag = (int)(s.Rate / minHz), minLag = Math.Max(2, (int)(s.Rate / maxHz));
        if (Cmnd(s, delaySeconds, maxLag) is not { } cmnd)
            return double.NaN;

        int best = -1;
        for (int lag = minLag; lag < maxLag; lag++)
            if (cmnd[lag] < 0.15)
            {
                while (lag + 1 < maxLag && cmnd[lag + 1] < cmnd[lag])
                    lag++;
                best = lag;
                break;
            }
        if (best < 0)
        {
            double min = double.MaxValue;
            for (int lag = minLag; lag < maxLag; lag++)
                if (cmnd[lag] < min) { min = cmnd[lag]; best = lag; }
            if (min > 0.4)
                return double.NaN;
        }

        return ToMidi(s.Rate, Refine(cmnd, best));
    }

    /// <summary>
    /// Checks a sound against the note it should be, immune to the octave slips of a free search: the period is
    /// looked for only within half a semitone of <paramref name="expectedMidi"/>. Returns the pitch found there, how
    /// clearly periodic the sound is at it (YIN dip: 0 = perfectly, 1 = not at all) and the same at half the period
    /// (low there means the sound is really an octave higher).
    /// </summary>
    public static (double Midi, double Dip, double OctaveUpDip) Verify(Stereo s, double expectedMidi, double delaySeconds)
    {
        double period = s.Rate / (440 * Math.Pow(2, (expectedMidi - 69) / 12));
        if (Cmnd(s, delaySeconds, (int)(period * 1.1) + 2) is not { } cmnd)
            return (double.NaN, 1, 1);
        int best = Lowest(cmnd, period);
        return (ToMidi(s.Rate, Refine(cmnd, best)), cmnd[best], cmnd[Lowest(cmnd, period / 2)]);
    }

    /// <summary><see cref="Verify"/> at a few moments after the onset, keeping the most clearly periodic one.</summary>
    public static (double Midi, double Dip, double OctaveUpDip) Clearest(Stereo s, double expectedMidi)
    {
        (double Midi, double Dip, double OctaveUpDip) best = (double.NaN, 1, 1);
        foreach (double delay in new[] { 0.15, 0.3, 0.5 })
        {
            var v = Verify(s, expectedMidi, delay);
            if (!double.IsNaN(v.Midi) && v.Dip < best.Dip)
                best = v;
        }
        return best;
    }

    /// <summary>The lag with the deepest dip within half a semitone of <paramref name="period"/>.</summary>
    private static int Lowest(double[] cmnd, double period)
    {
        int from = Math.Max(1, (int)Math.Floor(period * Math.Pow(2, -0.5 / 12)));
        int to = Math.Min(cmnd.Length - 2, (int)Math.Ceiling(period * Math.Pow(2, 0.5 / 12)));
        int best = from;
        for (int lag = from; lag <= to; lag++)
            if (cmnd[lag] < cmnd[best])
                best = lag;
        return best;
    }

    /// <summary>YIN's cumulative-mean-normalised difference for lags up to <paramref name="maxLag"/>, or null if too short.</summary>
    private static double[]? Cmnd(Stereo s, double delaySeconds, int maxLag)
    {
        int start = Onset(s) + (int)(delaySeconds * s.Rate);
        int window = Math.Max(4096, maxLag * 2);
        if (start + window + maxLag >= s.Length)
            start = Math.Max(0, s.Length - window - maxLag - 1);
        if (start + window + maxLag >= s.Length)
            return null;

        var x = new float[window + maxLag];
        for (int i = 0; i < x.Length; i++)
            x[i] = s.Left[start + i] + s.Right[start + i];

        var cmnd = new double[maxLag + 1];
        cmnd[0] = 1;
        double running = 0;
        for (int lag = 1; lag <= maxLag; lag++)
        {
            double sum = 0;
            for (int i = 0; i < window; i++)
            {
                double diff = x[i] - x[i + lag];
                sum += diff * diff;
            }
            running += sum;
            cmnd[lag] = running > 0 ? sum * lag / running : 1;
        }
        return cmnd;
    }

    /// <summary>Parabolic interpolation around a dip, for a sub-sample period.</summary>
    private static double Refine(double[] cmnd, int lag)
    {
        if (lag < 1 || lag + 1 >= cmnd.Length)
            return lag;
        double a = cmnd[lag - 1], b = cmnd[lag], c = cmnd[lag + 1];
        double denom = a - 2 * b + c;
        return Math.Abs(denom) > 1e-12 ? lag + 0.5 * (a - c) / denom : lag;
    }

    private static double ToMidi(int rate, double period) => 69 + 12 * Math.Log2(rate / period / 440);
}

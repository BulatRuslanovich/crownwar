namespace Lordsio.MusicStudio;

/// <summary>
/// A jazz kit played with brushes, synthesised: the sweep (a brush circling on the snare head: band-passed noise with
/// a grainy swell), the tap (a short brush slap with a little drum body and snare wires), a felt kick and a softly
/// brushed ride. The same seed always plays the same grain.
/// </summary>
public sealed class Brushes(int seed)
{
    private readonly Random _random = new(seed);

    private float Noise() => (float)(_random.NextDouble() * 2 - 1);

    /// <summary>A sweep of <paramref name="seconds"/>: rises quickly, swells, then fades as the brush lifts.</summary>
    public void Sweep(Mix mix, double start, double seconds, float amp, float pan = -0.15f)
    {
        var band = Biquad.BandPass(mix.Rate, 3200, 0.6);
        var low = Biquad.HighPass(mix.Rate, 700, 0.7);
        var grain = Biquad.LowPass(mix.Rate, 35, 0.7);
        var (pl, pr) = Mix.Pan(pan);
        int at = (int)(start * mix.Rate), count = (int)(seconds * mix.Rate);
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)count;
            float shape = MathF.Pow(MathF.Sin(MathF.PI * MathF.Pow(t, 0.7f)), 1.5f);
            float rough = 0.6f + 2.5f * MathF.Abs(grain.Process(Noise()));
            float v = low.Process(band.Process(Noise())) * shape * rough * amp;
            mix.Add(at + i, v * pl, v * pr);
        }
    }

    /// <summary>A brush tap on the snare; an accent rings longer and brighter.</summary>
    public void Tap(Mix mix, double start, float amp, bool accent = false, float pan = -0.15f)
    {
        var band = Biquad.BandPass(mix.Rate, accent ? 3000 : 2400, 0.5);
        var wires = Biquad.HighPass(mix.Rate, 4000, 0.7);
        var (pl, pr) = Mix.Pan(pan);
        int at = (int)(start * mix.Rate);
        float decay = accent ? 0.11f : 0.06f;
        int count = (int)(decay * 6 * mix.Rate);
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)mix.Rate;
            float env = MathF.Min(1f, t / 0.002f) * MathF.Exp(-t / decay);
            float body = 0.25f * MathF.Sin(MathF.Tau * 185f * t) * MathF.Exp(-t / 0.04f);
            float buzz = 0.35f * wires.Process(Noise()) * MathF.Exp(-t / (decay * 1.6f));
            float v = (band.Process(Noise()) * env + body + buzz * env) * amp;
            mix.Add(at + i, v * pl, v * pr);
        }
    }

    /// <summary>A soft, felt-beater kick: a short falling sine.</summary>
    public void Kick(Mix mix, double start, float amp)
    {
        int at = (int)(start * mix.Rate), count = (int)(0.6 * mix.Rate);
        double phase = 0;
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)mix.Rate;
            float hz = 48f + 40f * MathF.Exp(-t / 0.03f);
            phase += MathF.Tau * hz / mix.Rate;
            float v = (float)Math.Sin(phase) * MathF.Min(1f, t / 0.004f) * MathF.Exp(-t / 0.16f) * amp;
            mix.Add(at + i, v, v);
        }
    }

    /// <summary>A brushed ride: inharmonic metal partials and a hiss, washing out over a second or two.</summary>
    public void Ride(Mix mix, double start, float amp, float pan = 0.35f)
    {
        float[] partials = { 412f, 627f, 1153f, 1697f, 2843f, 4321f, 6111f };
        var hiss = Biquad.HighPass(mix.Rate, 6000, 0.7);
        var (pl, pr) = Mix.Pan(pan);
        int at = (int)(start * mix.Rate), count = (int)(2.2 * mix.Rate);
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)mix.Rate;
            float metal = 0f;
            for (int p = 0; p < partials.Length; p++)
                metal += MathF.Sin(MathF.Tau * partials[p] * t) * MathF.Exp(-t / (0.9f - p * 0.09f)) / (p + 2);
            float v = (0.5f * metal + 0.7f * hiss.Process(Noise()) * MathF.Exp(-t / 0.5f)) * MathF.Min(1f, t / 0.003f) * amp;
            mix.Add(at + i, v * pl, v * pr);
        }
    }
}

/// <summary>A second-order filter (RBJ cookbook), direct form I.</summary>
public sealed class Biquad
{
    private readonly float _b0, _b1, _b2, _a1, _a2;
    private float _x1, _x2, _y1, _y2;

    private Biquad(double b0, double b1, double b2, double a0, double a1, double a2)
    {
        _b0 = (float)(b0 / a0);
        _b1 = (float)(b1 / a0);
        _b2 = (float)(b2 / a0);
        _a1 = (float)(a1 / a0);
        _a2 = (float)(a2 / a0);
    }

    public static Biquad LowPass(int rate, double hz, double q)
    {
        var (cos, alpha) = Coefficients(rate, hz, q);
        return new Biquad((1 - cos) / 2, 1 - cos, (1 - cos) / 2, 1 + alpha, -2 * cos, 1 - alpha);
    }

    public static Biquad HighPass(int rate, double hz, double q)
    {
        var (cos, alpha) = Coefficients(rate, hz, q);
        return new Biquad((1 + cos) / 2, -(1 + cos), (1 + cos) / 2, 1 + alpha, -2 * cos, 1 - alpha);
    }

    /// <summary>Band-pass with 0 dB peak gain.</summary>
    public static Biquad BandPass(int rate, double hz, double q)
    {
        var (cos, alpha) = Coefficients(rate, hz, q);
        return new Biquad(alpha, 0, -alpha, 1 + alpha, -2 * cos, 1 - alpha);
    }

    private static (double Cos, double Alpha) Coefficients(int rate, double hz, double q)
    {
        double w = 2 * Math.PI * hz / rate;
        return (Math.Cos(w), Math.Sin(w) / (2 * q));
    }

    public float Process(float x)
    {
        float y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
        _x2 = _x1;
        _x1 = x;
        _y2 = _y1;
        _y1 = y;
        return y;
    }
}

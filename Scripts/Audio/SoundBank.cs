using System;
using System.Collections.Generic;
using Godot;

namespace Lordsio.Audio;

public enum Sound
{
    SwordHit,
    ArrowShot,
    ArrowHit,
    BuildingHit,
    /// <summary>A catapult's arm swings: a wooden creak, a whoosh and a thump.</summary>
    CatapultShot,
    /// <summary>A catapult's stone comes down: a heavy thud with grit.</summary>
    StoneImpact,
    UnitDeath,
    BuildingDestroyed,
    BuildingPlaced,
    BuildingCompleted,
    UnitReady,
    Alert,
    Victory,
    Defeat,
    /// <summary>"Can't do that": a refused order (no gold, a blocked site, a full army).</summary>
    Deny,
    /// <summary>A soft acknowledgement of an order.</summary>
    Order,
    /// <summary>Coins clinking: gold taken from a fallen enemy building.</summary>
    Coin,
    /// <summary>A low war horn, two long notes: a wave of the horde is coming (Defense).</summary>
    Horn,
}

/// <summary>
/// Placeholder sound effects synthesised in code (sines, noise and envelopes) at startup, so the game
/// needs no audio assets yet. Each sound can later be replaced by a recorded file without touching callers.
/// </summary>
public static class SoundBank
{
    private const int Rate = 22050;

    /// <summary>
    /// Renders every sound. The caller owns the streams and must dispose them: they are engine objects,
    /// and a static cache would outlive the engine at exit (reported as leaked instances).
    /// </summary>
    public static Dictionary<Sound, AudioStreamWav> CreateAll() => Build();

    /// <summary>A soft wooden click for button presses.</summary>
    public static AudioStreamWav UiClick() =>
        Make(0.06f, (t, n) => Sine(1250 - 6000 * t, t) * Decay(t, 0.012f) * 0.8f + LowNoise(n) * Decay(t, 0.006f) * 0.4f);

    /// <summary>A barely audible tick when the pointer moves onto a button.</summary>
    public static AudioStreamWav UiHover() =>
        Make(0.03f, (t, _) => Sine(2100, t) * Decay(t, 0.006f) * 0.5f);

    /// <summary>Dev-only: writes every sound to <paramref name="directory"/> as a .wav file for listening.</summary>
    public static void ExportAll(string directory)
    {
        foreach (var (sound, stream) in CreateAll())
        {
            var error = stream.SaveToWav(System.IO.Path.Combine(directory, sound + ".wav"));
            GD.Print($"{sound}: {error}");
            stream.Dispose();
        }
    }

    private static Dictionary<Sound, AudioStreamWav> Build() => new()
    {
        [Sound.SwordHit] = Make(0.14f, (t, n) =>
            (Sine(1850, t) * 0.5f + Sine(2630, t) * 0.35f + Sine(3710, t) * 0.25f) * Decay(t, 0.035f)
            + n * Decay(t, 0.01f) * 0.6f),
        [Sound.ArrowShot] = Make(0.2f, Whoosh),
        [Sound.ArrowHit] = Make(0.09f, (t, n) => Sine(170 - 400 * t, t) * Decay(t, 0.03f) + n * Decay(t, 0.008f) * 0.4f),
        [Sound.BuildingHit] = Make(0.18f, (t, n) => Sine(95, t) * Decay(t, 0.05f) * 0.9f + LowNoise(n) * Decay(t, 0.03f) * 0.6f),
        [Sound.CatapultShot] = Make(0.5f, (t, n) =>
            Soft(Saw(150 + 40 * MathF.Sin(t * 70f), t)) * Decay(t, 0.06f) * 0.25f // the creak of the arm
            + LowNoise(n) * Attack(t, 0.05f) * Decay(t, 0.18f) * 0.5f // the throw
            + Sine(70 - 40 * t, t) * Decay(t, 0.1f) * 0.7f), // the arm hits the crossbar
        [Sound.StoneImpact] = Make(0.45f, (t, n) =>
            Sine(58 - 30 * t, t) * Decay(t, 0.14f) + LowNoise(n) * Decay(t, 0.09f) * 0.8f + n * Decay(t, 0.02f) * 0.25f),
        [Sound.UnitDeath] = Make(0.38f, (t, n) =>
            Soft(Square(300 - 420 * t, t)) * Decay(t, 0.12f) * 0.45f + n * Decay(t, 0.05f) * 0.25f),
        [Sound.BuildingDestroyed] = Make(1.3f, (t, n) =>
            LowNoise(n) * Decay(t, 0.45f) * Attack(t, 0.02f) + Sine(48, t) * Decay(t, 0.5f) * 0.7f),
        [Sound.BuildingPlaced] = Make(0.16f, (t, n) => Sine(220, t) * Decay(t, 0.04f) + n * Decay(t, 0.01f) * 0.3f),
        [Sound.BuildingCompleted] = Make(0.5f, (t, _) => Notes(t, 0.12f, 880, 1320) * 0.5f),
        [Sound.UnitReady] = Make(0.32f, (t, _) =>
            Soft(Saw(440 * (1f + 0.01f * MathF.Sin(t * 40f)), t)) * Attack(t, 0.03f) * Decay(t, 0.2f) * 0.4f),
        [Sound.Alert] = Make(0.7f, (t, _) =>
            Soft(Square((int)(t / 0.15f) % 2 == 0 ? 660 : 440, t)) * 0.35f * (1f - t / 0.7f)),
        [Sound.Victory] = Make(1.4f, (t, _) => Notes(t, 0.18f, 523, 659, 784, 1046) * 0.5f),
        [Sound.Defeat] = Make(1.6f, (t, _) => Notes(t, 0.3f, 392, 349, 311, 262) * 0.5f),
        [Sound.Deny] = Make(0.22f, (t, _) =>
            Soft(Square(t < 0.09f ? 196 : 147, t)) * Attack(t, 0.005f) * Decay(t, 0.1f) * 0.5f),
        [Sound.Order] = Make(0.09f, (t, n) =>
            Sine(620 + 900 * t, t) * Attack(t, 0.004f) * Decay(t, 0.03f) * 0.6f + LowNoise(n) * Decay(t, 0.006f) * 0.25f),
        [Sound.Coin] = Make(0.45f, (t, _) =>
            (Sine(2637, t) + 0.5f * Sine(3951, t)) * Decay(t, 0.06f) * 0.5f
            + (t > 0.08f ? (Sine(3136, t) + 0.4f * Sine(4699, t)) * Decay(t - 0.08f, 0.09f) * 0.45f : 0f)),
        [Sound.Horn] = Make(2.1f, Horn),
    };

    /// <summary>
    /// A war horn: a low note that swells in, sagging up to pitch, then a fifth higher after a breath; brassy from a
    /// saw wave pushed into saturation, with a little breath noise.
    /// </summary>
    private static float Horn(float t, float noise)
    {
        const float second = 1.05f, length = 2.1f;
        // Each note counts its own time, so the pitch slide starts cleanly.
        float local = t < second ? t : t - second;
        float pitch = t < second ? 98f * (0.93f + 0.07f * MathF.Min(1f, local / 0.2f)) : 147f * (0.97f + 0.03f * MathF.Min(1f, local / 0.12f));
        float envelope = Attack(local, t < second ? 0.18f : 0.08f)
                         * MathF.Min(1f, (t < second ? second - 0.03f - t : length - t) / 0.3f);
        float brass = Soft(Saw(pitch, local) * 1.6f + Saw(pitch * 2f, local) * 0.35f);
        return MathF.Max(0f, envelope) * (brass * 0.6f + LowNoise(noise) * 0.06f);
    }

    /// <summary>Renders <paramref name="seconds"/> of audio from a sample function f(time, white noise).</summary>
    private static AudioStreamWav Make(float seconds, Func<float, float, float> sample)
    {
        const float TargetPeak = 0.8f;
        int count = (int)(seconds * Rate);
        var samples = new float[count];
        _lowpass = 0f; // filter state of the previous sound must not leak into this one
        var rng = new Random(count);
        float fade = 0.005f; // short fade-out avoids a click at the end
        float peak = 1e-6f;
        for (int i = 0; i < count; i++)
        {
            float t = (float)i / Rate;
            float noise = (float)(rng.NextDouble() * 2 - 1);
            samples[i] = sample(t, noise) * MathF.Min(1f, (seconds - t) / fade);
            peak = MathF.Max(peak, MathF.Abs(samples[i]));
        }

        // Normalise every sound to the same peak: no clipping, and loudness is set where it is played.
        float gain = TargetPeak / peak;
        var bytes = Pcm.Encode16(samples, count, gain);
        return new AudioStreamWav
        {
            Data = bytes,
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = Rate,
            Stereo = false,
        };
    }

    private static float _lowpass;

    private static float Whoosh(float t, float noise)
    {
        // Noise through a filter that opens and closes: a short "fwip".
        float open = MathF.Sin(MathF.PI * MathF.Min(1f, t / 0.18f));
        _lowpass += (noise - _lowpass) * (0.05f + 0.5f * open);
        return _lowpass * open * 0.8f;
    }

    private static float LowNoise(float noise)
    {
        _lowpass += (noise - _lowpass) * 0.08f;
        return _lowpass * 3f;
    }

    /// <summary>A melody of decaying sine notes, <paramref name="step"/> seconds apart.</summary>
    private static float Notes(float t, float step, params float[] frequencies)
    {
        float v = 0f;
        for (int i = 0; i < frequencies.Length; i++)
        {
            float local = t - i * step;
            if (local < 0f)
                break;
            v += (Sine(frequencies[i], local) + 0.3f * Sine(frequencies[i] * 2, local)) * Decay(local, 0.25f) * Attack(local, 0.01f);
        }
        return v;
    }

    private static float Sine(float hz, float t) => MathF.Sin(MathF.Tau * hz * t);
    private static float Square(float hz, float t) => Sine(hz, t) >= 0f ? 1f : -1f;
    private static float Saw(float hz, float t) => 2f * (hz * t - MathF.Floor(hz * t + 0.5f));
    private static float Soft(float v) => MathF.Tanh(v * 0.8f);
    private static float Decay(float t, float tau) => MathF.Exp(-t / tau);
    private static float Attack(float t, float time) => MathF.Min(1f, t / time);
}

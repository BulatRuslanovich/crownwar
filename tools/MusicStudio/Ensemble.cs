namespace Lordsio.MusicStudio;

/// <summary>A chord: the bass note and four hand tones for the piano (MIDI, low to high).</summary>
public sealed record Chord(int Bass, int[] Hand)
{
    /// <summary>The bass note in the contrabass's clean pizzicato range (at most B2).</summary>
    public int PizzBass => Bass <= 47 ? Bass : Bass - 12;
}

/// <summary>
/// The trio a piece is written for — upright piano, violin and contrabass (pizzicato and bowed) — each playing
/// into its own stem, with a little human timing and touch. A piece writes notes by bar and beat, then
/// <see cref="Finish"/> mixes the stems into a seamless loop at a common loudness.
/// </summary>
public sealed class Ensemble
{
    /// <summary>Room left after the last bar for ringing notes and reverb; folded back onto the start of the loop.</summary>
    private const double TailSeconds = 8;

    /// <summary>Room before the first bar for notes played a hair early; folded onto the end of the loop.</summary>
    private const double PreRoll = 0.1;

    private static Sampler? _piano, _violin, _bassPizz, _bassArco;

    private readonly Random _human;

    public Sampler Piano { get; }
    public Sampler Violin { get; }
    public Sampler BassPizz { get; }
    public Sampler BassArco { get; }

    public Mix PianoStem { get; }
    public Mix ViolinStem { get; }
    public Mix BassStem { get; }

    public int Bars { get; }
    public double Bpm { get; }

    public Ensemble(string cache, double bpm, int bars, int seed)
    {
        // The recordings take a while to load; every piece shares them.
        Piano = _piano ??= new Sampler("piano", cache, Vsco.UprightPiano, maxSeconds: 10) { Release = 0.35 };
        Violin = _violin ??= new Sampler("violin", cache, Vsco.ViolinArco, maxSeconds: 12) { Release = 0.4, Volume = 2.7f };
        BassPizz = _bassPizz ??= new Sampler("bass pizz", cache, Vsco.BassPizz, octaveShift: 12, maxSeconds: 4) { Release = 0.12, Volume = 6.3f };
        BassArco = _bassArco ??= new Sampler("bass arco", cache, Vsco.BassArco, octaveShift: 12, maxSeconds: 12) { Release = 0.5, Volume = 3f };
        _human = new Random(seed);
        Bpm = bpm;
        Bars = bars;
        double seconds = PreRoll + bars * 4 * 60 / bpm + TailSeconds;
        PianoStem = new Mix(bpm, seconds);
        ViolinStem = new Mix(bpm, seconds);
        BassStem = new Mix(bpm, seconds);
    }

    /// <summary>Seconds at a bar and a beat within it.</summary>
    public double T(int bar, double beat = 0) => PreRoll + PianoStem.Time(bar, beat);

    public double Beats(double beats) => PianoStem.Seconds(beats);

    /// <summary>A random nudge of up to ± <paramref name="seconds"/>.</summary>
    public double Jitter(double seconds = 0.008) => (_human.NextDouble() * 2 - 1) * seconds;

    /// <summary>A velocity with a little variation of touch.</summary>
    public float Vary(float velocity, float spread = 0.05f) =>
        Math.Clamp(velocity + (float)(_human.NextDouble() * 2 - 1) * spread, 0.05f, 1f);

    // ---------------------------------------------------------------- piano

    /// <summary>
    /// One bar of the piano: the left hand's root and fifth (and the low octave if <paramref name="octaves"/>) held
    /// under the pedal, the right hand playing <paramref name="pattern"/> (indexes into the chord's hand tones, one per
    /// <paramref name="step"/> beats; −1 rests) with the accents of <paramref name="accents"/>.
    /// </summary>
    public void PianoBar(int bar, Chord chord, float loud, int[] pattern, float[] accents, double step = 0.5,
                         bool octaves = false, double hold = 0)
    {
        double end = T(bar + 1) + 0.05; // the pedal changes with the chord
        double ring = hold > 0 ? hold : end - T(bar);
        Piano.Play(PianoStem, T(bar) + Jitter(0.006), ring, chord.Bass + 12, Vary(loud), -0.2f);
        Piano.Play(PianoStem, T(bar, 0.02) + Jitter(0.006), ring, chord.Bass + 19, Vary(loud * 0.8f), -0.2f);
        if (octaves)
            Piano.Play(PianoStem, T(bar) + Jitter(0.004), ring, chord.Bass, Vary(loud * 0.7f), -0.3f);
        for (int i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] < 0)
                continue;
            double t = T(bar, i * step) + Jitter();
            int note = pattern[i] < 4 ? chord.Hand[pattern[i]] : chord.Hand[pattern[i] - 4] + 12; // 4..7: an octave up
            Piano.Play(PianoStem, t, (hold > 0 ? T(bar) + hold : end) - t, note, Vary(loud * accents[i % accents.Length]), 0.15f);
        }
    }

    /// <summary>The chord as one soft, slightly rolled stroke that rings for <paramref name="beats"/>.</summary>
    public void PianoChord(int bar, double beat, Chord chord, float loud, double beats)
    {
        double t = T(bar, beat), ring = Beats(beats);
        Piano.Play(PianoStem, t, ring, chord.Bass + 12, Vary(loud), -0.2f);
        Piano.Play(PianoStem, t + 0.015, ring, chord.Bass + 19, Vary(loud * 0.8f), -0.2f);
        for (int i = 0; i < chord.Hand.Length; i++)
            Piano.Play(PianoStem, t + 0.03 + 0.035 * i + Jitter(0.005), ring, chord.Hand[i], Vary(loud * 0.85f), 0.15f);
    }

    /// <summary>A single piano note (a melody doubled on the piano, a passing note).</summary>
    public void PianoNote(int bar, double beat, double beats, int midi, float loud) =>
        Piano.Play(PianoStem, T(bar, beat) + Jitter(), Beats(beats), midi, Vary(loud), 0.15f);

    // ---------------------------------------------------------------- violin

    /// <summary>A violin line: (bar, beat, beats, note) with the bar counted from <paramref name="fromBar"/>, each note bowed in softly so the line sings.</summary>
    public void ViolinLine(int fromBar, float loud, IEnumerable<(int Bar, double Beat, double Beats, int Midi)> notes, float swell = 0f)
    {
        foreach (var (bar, beat, beats, midi) in notes)
        {
            // A long note is bowed a little stronger: the line swells into it.
            float v = loud + swell * (float)Math.Min(1, beats / 4);
            Violin.Play(ViolinStem, T(fromBar + bar, beat) + Jitter(0.01), Beats(beats) + 0.05, midi, Vary(v), 0.3f,
                        attack: 0.07, skip: 0.04);
        }
    }

    // ---------------------------------------------------------------- bass

    /// <summary>A plucked note.</summary>
    public void Pluck(int bar, double beat, double beats, int midi, float loud) =>
        BassPizz.Play(BassStem, T(bar, beat) + Jitter(0.01), Beats(beats), Math.Min(midi, 47), Vary(loud), 0.05f);

    /// <summary>A bowed note, entering softly.</summary>
    public void Bow(int bar, double beat, double beats, int midi, float loud) =>
        BassArco.Play(BassStem, T(bar, beat) + Jitter(0.01), Beats(beats), midi, Vary(loud), 0.05f, attack: 0.25, skip: 0.05);

    // ---------------------------------------------------------------- the mix

    /// <summary>
    /// Mixes the stems, adds the room, folds the tail onto the start (so the loop's first bars carry the last bars'
    /// ringing, as they would on a repeat) and the pre-roll onto the end, and sets the loudness: RMS <paramref name="targetDb"/> dBFS with a soft
    /// limit on the peaks.
    /// </summary>
    public Stereo Finish(float targetDb = -18f, float reverb = 0.35f)
    {
        var mix = new Mix(Bpm, PianoStem.Length / (double)PianoStem.Rate);
        mix.Add(PianoStem);
        mix.Add(ViolinStem);
        mix.Add(BassStem);
        mix.AddReverb(reverb);

        var full = mix.ToStereo();
        int pre = (int)Math.Round(PreRoll * full.Rate), loop = (int)Math.Round((T(Bars) - PreRoll) * full.Rate);
        var left = new float[loop];
        var right = new float[loop];
        for (int i = 0; i < full.Length; i++)
        {
            int at = ((i - pre) % loop + loop) % loop; // before the start → the end; past the end → the start
            left[at] += full.Left[i];
            right[at] += full.Right[i];
        }
        var looped = new Mix(Bpm, loop / (double)full.Rate);
        looped.Load(left, right);
        float gain = MathF.Pow(10f, (targetDb - looped.Rms()) / 20f);
        looped.Scale(gain);
        int limited = looped.SoftLimit(0.7f);
        Console.WriteLine($"  loop {loop / (double)full.Rate:F1} s, gain {20 * MathF.Log10(gain):F1} dB, {limited} samples softened above 0.7, peak {looped.Peak():F2}");
        return looped.ToStereo();
    }
}

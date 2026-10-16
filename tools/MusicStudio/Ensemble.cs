namespace Warcrown.MusicStudio;

/// <summary>A chord: the bass note and four hand tones for the piano (MIDI, low to high).</summary>
public sealed record Chord(int Bass, int[] Hand)
{
    /// <summary>The bass note in the contrabass's clean pizzicato range (at most B2).</summary>
    public int PizzBass => Bass <= 47 ? Bass : Bass - 12;
}

/// <summary>
/// The trio a piece is written for — upright piano, violin and contrabass (bowed, plucked and spiccato) — each
/// playing into its own stem, with a little human timing and touch. A piece writes notes by bar and beat, then
/// <see cref="Finish"/> (or <see cref="RenderLayers"/>, for a piece in intensity layers) mixes the stems into a
/// seamless loop at a common loudness.
/// </summary>
/// <remarks>
/// The human touch depends only on each note (its time and pitch), not on the order notes are written in: the
/// versions of a layered piece add parts to one another, and the notes they share must render identically, or the
/// game's crossfade between them would smear.
/// </remarks>
public sealed class Ensemble
{
    /// <summary>Room left after the last bar for ringing notes and reverb; folded back onto the start of the loop.</summary>
    private const double TailSeconds = 8;

    /// <summary>Room before the first bar for notes played a hair early; folded onto the end of the loop.</summary>
    private const double PreRoll = 0.1;

    private static Sampler? _piano, _violin, _violinSpiccato, _violinTremolo, _bassPizz, _bassArco, _bassSpiccato;

    public Sampler Piano { get; }
    public Sampler Violin { get; }
    public Sampler ViolinSpiccato { get; }
    public Sampler ViolinTremolo { get; }
    public Sampler BassPizz { get; }
    public Sampler BassArco { get; }
    public Sampler BassSpiccato { get; }

    public Mix PianoStem { get; }
    public Mix ViolinStem { get; }
    public Mix BassStem { get; }

    public int Bars { get; }
    public double Bpm { get; }

    /// <summary>Which version of a layered piece is being written: 0 calm, 1 tension, 2 battle (each adds to the one before).</summary>
    public int Level { get; }

    /// <param name="bpm">Beats a minute, where a beat is whatever the bar counts (quarters in 4/4, eighths in 7/8).</param>
    public Ensemble(string cache, double bpm, int bars, int beatsPerBar = 4, int level = 0)
    {
        // The recordings take a while to load; every piece shares them.
        Piano = _piano ??= new Sampler("piano", cache, Vsco.UprightPiano, maxSeconds: 10) { Release = 0.35 };
        Violin = _violin ??= new Sampler("violin", cache, Vsco.ViolinArco, maxSeconds: 12) { Release = 0.4, Volume = 2.7f };
        ViolinSpiccato = _violinSpiccato ??= new Sampler("violin spiccato", cache, Vsco.ViolinSpiccato, octaveShift: 12, maxSeconds: 2) { Release = 0.08, Volume = 5.5f };
        ViolinTremolo = _violinTremolo ??= new Sampler("violin tremolo", cache, Vsco.ViolinTremolo, octaveShift: 12, maxSeconds: 10) { Release = 0.5, Volume = 2f };
        BassPizz = _bassPizz ??= new Sampler("bass pizz", cache, Vsco.BassPizz, octaveShift: 12, maxSeconds: 4) { Release = 0.12, Volume = 6.3f };
        BassArco = _bassArco ??= new Sampler("bass arco", cache, Vsco.BassArco, octaveShift: 12, maxSeconds: 12) { Release = 0.5, Volume = 3f };
        BassSpiccato = _bassSpiccato ??= new Sampler("bass spiccato", cache, Vsco.BassSpiccato, octaveShift: 12, maxSeconds: 2) { Release = 0.08, Volume = 4f };
        Bpm = bpm;
        Bars = bars;
        Level = level;
        double seconds = PreRoll + bars * beatsPerBar * 60 / bpm + TailSeconds;
        PianoStem = new Mix(bpm, seconds, beatsPerBar);
        ViolinStem = new Mix(bpm, seconds, beatsPerBar);
        BassStem = new Mix(bpm, seconds, beatsPerBar);
    }

    /// <summary>Seconds at a bar and a beat within it.</summary>
    public double T(int bar, double beat = 0) => PreRoll + PianoStem.Time(bar, beat);

    public double Beats(double beats) => PianoStem.Seconds(beats);

    /// <summary>
    /// Plays a note with a human touch: up to ± <paramref name="jitter"/> seconds early or late and ± 0.05 velocity,
    /// both fixed by the note itself.
    /// </summary>
    private static void Note(Sampler sampler, Mix stem, double t, double seconds, double midi, float velocity, float pan,
                             double jitter = 0.008, double attack = 0, double skip = 0)
    {
        double at = t + Human.Signed(t, midi, 1) * jitter;
        float v = Math.Clamp(velocity + (float)Human.Signed(t, midi, 2) * 0.05f, 0.05f, 1f);
        sampler.Play(stem, at, seconds, midi, v, pan, attack, skip);
    }

    // ---------------------------------------------------------------- piano

    /// <summary>
    /// One bar of the piano: the left hand's root and fifth (and the low octave if <paramref name="octaves"/>) held
    /// under the pedal, the right hand playing <paramref name="pattern"/> (indexes into the chord's hand tones, one per
    /// <paramref name="step"/> beats; 4..7 the same an octave up; −1 rests) with the accents of <paramref name="accents"/>.
    /// </summary>
    public void PianoBar(int bar, Chord chord, float loud, int[] pattern, float[] accents, double step = 0.5,
                         bool octaves = false, bool leftHand = true)
    {
        double end = T(bar + 1) + 0.05; // the pedal changes with the chord
        if (leftHand)
        {
            Note(Piano, PianoStem, T(bar), end - T(bar), chord.Bass + 12, loud, -0.2f, 0.006);
            Note(Piano, PianoStem, T(bar, 0.02), end - T(bar), chord.Bass + 19, loud * 0.8f, -0.2f, 0.006);
        }
        if (octaves)
            Note(Piano, PianoStem, T(bar, 0.01), end - T(bar), chord.Bass, loud * 0.7f, -0.3f, 0.004);
        for (int i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] < 0)
                continue;
            double t = T(bar, i * step);
            int note = pattern[i] < 4 ? chord.Hand[pattern[i]] : chord.Hand[pattern[i] - 4] + 12;
            Note(Piano, PianoStem, t, end - t, note, loud * accents[i % accents.Length], 0.15f);
        }
    }

    /// <summary>The chord as one soft, slightly rolled stroke that rings for <paramref name="beats"/>.</summary>
    public void PianoChord(int bar, double beat, Chord chord, float loud, double beats)
    {
        double t = T(bar, beat), ring = Beats(beats);
        Note(Piano, PianoStem, t, ring, chord.Bass + 12, loud, -0.2f, 0);
        Note(Piano, PianoStem, t + 0.015, ring, chord.Bass + 19, loud * 0.8f, -0.2f, 0);
        for (int i = 0; i < chord.Hand.Length; i++)
            Note(Piano, PianoStem, t + 0.03 + 0.035 * i, ring, chord.Hand[i], loud * 0.85f, 0.15f, 0.005);
    }

    /// <summary>A single piano note (a pulse, a stab, a passing note).</summary>
    public void PianoNote(int bar, double beat, double beats, int midi, float loud, float pan = 0.15f) =>
        Note(Piano, PianoStem, T(bar, beat), Beats(beats), midi, loud, pan);

    // ---------------------------------------------------------------- violin

    /// <summary>A violin line: (bar, beat, beats, note) with the bar counted from <paramref name="fromBar"/>, each note bowed in softly so the line sings.</summary>
    public void ViolinLine(int fromBar, float loud, IEnumerable<(int Bar, double Beat, double Beats, int Midi)> notes, float swell = 0f)
    {
        foreach (var (bar, beat, beats, midi) in notes)
        {
            // A long note is bowed a little stronger: the line swells into it.
            float v = loud + swell * (float)Math.Min(1, beats / 4);
            Note(Violin, ViolinStem, T(fromBar + bar, beat), Beats(beats) + 0.05, midi, v, 0.3f, 0.01, attack: 0.07, skip: 0.04);
        }
    }

    /// <summary>A short, bouncing violin stroke.</summary>
    public void ViolinBounce(int bar, double beat, int midi, float loud, float pan = 0.4f) =>
        Note(ViolinSpiccato, ViolinStem, T(bar, beat), Beats(0.5), midi, loud, pan, 0.006);

    /// <summary>A trembling, held violin note that fades in.</summary>
    public void ViolinTrem(int bar, double beat, double beats, int midi, float loud) =>
        Note(ViolinTremolo, ViolinStem, T(bar, beat), Beats(beats), midi, loud, 0.35f, 0.01, attack: 0.4, skip: 0.05);

    // ---------------------------------------------------------------- bass

    /// <summary>A plucked note.</summary>
    public void Pluck(int bar, double beat, double beats, int midi, float loud) =>
        Note(BassPizz, BassStem, T(bar, beat), Beats(beats), Math.Min(midi, 47), loud, 0.05f, 0.01);

    /// <summary>A bowed note, entering softly.</summary>
    public void Bow(int bar, double beat, double beats, int midi, float loud) =>
        Note(BassArco, BassStem, T(bar, beat), Beats(beats), midi, loud, 0.05f, 0.01, attack: 0.25, skip: 0.05);

    /// <summary>A short, driving bowed stroke.</summary>
    public void Drive(int bar, double beat, int midi, float loud) =>
        Note(BassSpiccato, BassStem, T(bar, beat), Beats(0.5), midi, loud, -0.05f, 0.006);

    // ---------------------------------------------------------------- the mix

    /// <summary>
    /// Mixes the stems, adds the room, and folds the tail onto the start (so the loop's first bars carry the last
    /// bars' ringing, as they would on a repeat) and the pre-roll onto the end.
    /// </summary>
    public Mix Mixdown(float reverb = 0.35f)
    {
        Console.WriteLine($"  level {Level} stems (RMS dBFS, before gain): piano {PianoStem.Rms():F1}, violin {ViolinStem.Rms():F1}, bass {BassStem.Rms():F1}");
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
        return looped;
    }

    /// <summary>A single-version piece: the mixdown at RMS <paramref name="targetDb"/> dBFS with a soft limit on the peaks.</summary>
    public Stereo Finish(float targetDb = -18f)
    {
        var mix = Mixdown();
        Normalise(mix, MathF.Pow(10f, (targetDb - mix.Rms()) / 20f));
        return mix.ToStereo();
    }

    /// <summary>
    /// A piece in intensity layers: <paramref name="compose"/> writes it at each level (0 calm, 1 tension, 2 battle).
    /// All versions get the same gain — the one that brings the tension version to <paramref name="targetDb"/> — so the
    /// parts they share stay equally loud and the calm one is naturally softer, the battle louder.
    /// </summary>
    public static Stereo[] RenderLayers(Func<int, Ensemble> compose, float targetDb = -18f)
    {
        var mixes = Enumerable.Range(0, 3).Select(level => compose(level).Mixdown()).ToArray();
        float gain = MathF.Pow(10f, (targetDb - mixes[1].Rms()) / 20f);
        foreach (var mix in mixes)
            Normalise(mix, gain);
        return mixes.Select(m => m.ToStereo()).ToArray();
    }

    private static void Normalise(Mix mix, float gain)
    {
        mix.Scale(gain);
        int limited = mix.SoftLimit(0.7f);
        Console.WriteLine($"  {mix.Length / (double)mix.Rate:F1} s, gain {20 * MathF.Log10(gain):F1} dB, RMS {mix.Rms():F1} dBFS, {limited} samples softened above 0.7, peak {mix.Peak():F2}");
    }
}

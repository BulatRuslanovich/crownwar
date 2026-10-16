namespace Lordsio.MusicStudio;

/// <summary>
/// A 17-bar test of the quartet, to judge the sound before writing the real pieces. D Dorian at 92 bpm, straight
/// eighths, over a rising loop Dm9 – Em7 – Fmaj7♯11 – G6/9: the piano alone with a 3+3+2 ostinato, then the
/// pizzicato bass and brush sweeps join, then the violin takes a melody over brush taps, and all of it once more
/// with a felt kick, ending on Dm9.
/// </summary>
public static class Demo
{
    private sealed record Chord(int Bass, int[] Hand);

    private static readonly Chord[] Loop =
    {
        new(38, new[] { 65, 69, 72, 76 }), // Dm9: D | F A C E
        new(40, new[] { 67, 71, 74, 76 }), // Em7: E | G B D E
        new(41, new[] { 69, 71, 72, 76 }), // Fmaj7#11: F | A B C E
        new(43, new[] { 69, 71, 74, 76 }), // G6/9: G | A B D E
    };

    /// <summary>The violin: (bar, beat, beats, note), from bar 8.</summary>
    private static readonly (int Bar, double Beat, double Beats, int Midi)[] Melody =
    {
        (8, 0, 2.5, 81), (8, 2.5, 0.5, 79), (8, 3, 1, 77),
        (9, 0, 3, 76), (9, 3, 1, 74),
        (10, 0, 1.5, 76), (10, 1.5, 2.5, 83),
        (11, 0, 4, 81),
        (12, 0, 2.5, 86), (12, 2.5, 0.5, 84), (12, 3, 1, 81),
        (13, 0, 2, 83), (13, 2, 2, 79),
        (14, 0, 1.5, 81), (14, 1.5, 1.5, 79), (14, 3, 1, 76),
        (15, 0, 2, 76), (15, 2, 2, 74),
        (16, 0, 4, 81),
    };

    public static Stereo Render(string cache)
    {
        Console.WriteLine("loading instruments");
        var piano = new Sampler("piano", cache, Vsco.UprightPiano, maxSeconds: 10) { Release = 0.35 };
        var bass = new Sampler("bass", cache, Vsco.BassPizz, octaveShift: 12, maxSeconds: 4) { Release = 0.12, Volume = 6.3f };
        var violin = new Sampler("violin", cache, Vsco.ViolinArco, maxSeconds: 12) { Release = 0.4, Volume = 2.7f };
        var kit = new Brushes(seed: 7);
        var human = new Random(3);
        double Jitter(double seconds) => (human.NextDouble() * 2 - 1) * seconds;
        float Vary(float v) => Math.Clamp(v + (float)(human.NextDouble() * 2 - 1) * 0.05f, 0.05f, 1f);

        const int bars = 17;
        const double bpm = 92;
        double length = bars * 4 * 60 / bpm + 6;
        var mix = new Mix(bpm, length);
        var bassMix = new Mix(bpm, length);
        var kitMix = new Mix(bpm, length);
        var violinMix = new Mix(bpm, length);
        Console.WriteLine($"rendering {bars} bars ({mix.Time(bars, 0):F1} s)");

        int[] pattern = { 0, 2, 3, 1, 2, 3, 0, 2 };
        float[] accent = { 1f, 0.6f, 0.65f, 0.9f, 0.6f, 0.65f, 0.85f, 0.6f }; // 3 + 3 + 2
        for (int bar = 0; bar < bars; bar++)
        {
            var chord = Loop[bar % Loop.Length];
            bool last = bar == bars - 1;
            double barEnd = mix.Time(bar + 1, 0) + 0.05; // the pedal changes with the chord

            // Piano: left hand root and fifth held under the pedal, right hand the ostinato (in the last bar, the chord).
            float loud = bar < 4 ? 0.5f : bar < 12 ? 0.55f : 0.65f;
            piano.Play(mix, mix.Time(bar, 0) + Jitter(0.006), (last ? 5 : barEnd - mix.Time(bar, 0)), chord.Bass + 12, Vary(loud), -0.2f);
            piano.Play(mix, mix.Time(bar, 0.02) + Jitter(0.006), (last ? 5 : barEnd - mix.Time(bar, 0)), chord.Bass + 19, Vary(loud * 0.8f), -0.2f);
            if (bar >= 12)
                piano.Play(mix, mix.Time(bar, 0), (last ? 5 : barEnd - mix.Time(bar, 0)), chord.Bass, Vary(loud * 0.7f), -0.3f);
            if (last)
                foreach (var (note, i) in chord.Hand.Select((n, i) => (n, i)))
                    piano.Play(mix, mix.Time(bar, 0.04 * i), 5, note, Vary(0.45f), 0.15f);
            else
                for (int e = 0; e < 8; e++)
                {
                    double t = mix.Time(bar, e * 0.5) + Jitter(0.008);
                    piano.Play(mix, t, barEnd - t, chord.Hand[pattern[e]], Vary(loud * accent[e]), 0.15f);
                }

            // Bass pizzicato from bar 4: root, the root again on the "and" of 2, the fifth on 4.
            if (bar >= 4)
            {
                bass.Play(bassMix, mix.Time(bar, 0) + Jitter(0.01), mix.Seconds(last ? 4 : 1.4), chord.Bass, Vary(0.8f), 0.05f);
                if (!last)
                {
                    bass.Play(bassMix, mix.Time(bar, 1.5) + Jitter(0.01), mix.Seconds(1), chord.Bass, Vary(0.5f), 0.05f);
                    int fifth = chord.Bass + 7 <= 47 ? chord.Bass + 7 : chord.Bass - 5; // stay in the bass's clean range
                    bass.Play(bassMix, mix.Time(bar, 3) + Jitter(0.01), mix.Seconds(0.9), fifth, Vary(0.6f), 0.05f);
                }
            }

            // Brushes from bar 4: a sweep each half bar; taps on 2 and 4 from bar 8; the kick on 1 from bar 12.
            if (bar >= 4 && !last)
            {
                float sweep = bar < 8 ? 0.06f : 0.075f;
                kit.Sweep(kitMix, mix.Time(bar, 0), mix.Seconds(2), sweep);
                kit.Sweep(kitMix, mix.Time(bar, 2), mix.Seconds(2), sweep * 0.85f);
                if (bar >= 8)
                {
                    kit.Tap(kitMix, mix.Time(bar, 1) + Jitter(0.006), Vary(0.2f), accent: true);
                    kit.Tap(kitMix, mix.Time(bar, 3) + Jitter(0.006), Vary(0.2f), accent: true);
                    kit.Tap(kitMix, mix.Time(bar, 3.5) + Jitter(0.006), Vary(0.07f));
                }
                if (bar >= 12)
                    kit.Kick(kitMix, mix.Time(bar, 0), 0.2f);
            }
            if (last)
                kit.Ride(kitMix, mix.Time(bar, 0), 0.07f);
        }

        // Violin: each note bowed in softly so the line sings legato.
        foreach (var (bar, beat, beats, midi) in Melody)
            violin.Play(violinMix, mix.Time(bar, beat) + Jitter(0.01), mix.Seconds(beats) + 0.05, midi,
                        Vary(bar >= 12 ? 0.75f : 0.6f), 0.3f, attack: 0.06, skip: 0.04);

        double from = mix.Time(8, 0), to = mix.Time(16, 0);
        Console.WriteLine($"levels in bars 8-15 (RMS dBFS): piano {mix.Rms(from, to):F1}, bass {bassMix.Rms(from, to):F1}, brushes {kitMix.Rms(from, to):F1}, violin {violinMix.Rms(from, to):F1}");
        mix.Add(bassMix);
        mix.Add(kitMix);
        mix.Add(violinMix);
        float dry = mix.Rms();
        mix.AddReverb(wet: 0.35f);
        Console.WriteLine($"dry {dry:F1} dBFS, with reverb {mix.Rms():F1} dBFS");
        float peak = mix.Peak();
        mix.Scale(0.89f / peak); // −1 dBFS
        Console.WriteLine($"peak before normalising {peak:F2}");
        return mix.ToStereo();
    }
}

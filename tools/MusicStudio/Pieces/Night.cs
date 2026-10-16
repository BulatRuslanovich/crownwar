namespace Lordsio.MusicStudio.Pieces;

/// <summary>
/// The night, in the same three versions as <see cref="Day"/>. E minor in a slow 4/4 at 66 bpm: 40 bars, 145 s, five
/// blocks of eight bars on two chord rows — N (Em9 – Cmaj7♯11 – Am9 – B7sus4 – Em9 – Cmaj7♯11 – Am9 – B7♭9) and the
/// theme's row M (Gmaj7 – F♯m7♭5 – Em9 – Cmaj7 – Am9 – D6/9 – Bsus4 – B7♭9).
/// Calm: sparse piano quarters with a high star note now and then, a long bowed bass, the violin theme over M.
/// Tension adds a heartbeat in the piano, plucks on 1 and 3 and a faint violin tremolo.
/// Battle trades those for broken sixteenths (3 + 3 + 2) in spiccato bass and violin and the piano's octave stabs.
/// </summary>
public static class Night
{
    private static readonly Chord Em9 = new(40, new[] { 66, 67, 71, 74 });
    private static readonly Chord Cmaj7Sharp11 = new(36, new[] { 64, 66, 67, 71 });
    private static readonly Chord Am9 = new(33, new[] { 60, 64, 67, 71 });
    private static readonly Chord B7Sus4 = new(35, new[] { 64, 66, 69, 71 });
    private static readonly Chord B7Flat9 = new(35, new[] { 63, 66, 69, 72 });
    private static readonly Chord Gmaj7 = new(43, new[] { 62, 66, 67, 71 });
    private static readonly Chord FSharpHalfDim = new(42, new[] { 64, 66, 69, 72 });
    private static readonly Chord Cmaj7 = new(36, new[] { 64, 67, 71, 72 });
    private static readonly Chord D69 = new(38, new[] { 64, 66, 69, 71 });

    private static readonly Chord[] N = { Em9, Cmaj7Sharp11, Am9, B7Sus4, Em9, Cmaj7Sharp11, Am9, B7Flat9 };
    private static readonly Chord[] M = { Gmaj7, FSharpHalfDim, Em9, Cmaj7, Am9, D69, B7Sus4, B7Flat9 };

    /// <summary>Which blocks are the theme's (row M); the others are row N.</summary>
    private static readonly bool[] Form = { false, false, true, false, true };

    /// <summary>The theme over M: (bar, beat, beats, note).</summary>
    private static readonly (int, double, double, int)[] Theme =
    {
        (0, 0, 3, 83), (0, 3, 1, 81),
        (1, 0, 2, 84), (1, 2, 2, 81),
        (2, 0, 4, 79),
        (3, 0, 2, 76), (3, 2, 1, 79), (3, 3, 1, 83),
        (4, 0, 3, 84), (4, 3, 1, 83),
        (5, 0, 4, 81),
        (6, 0, 2, 76), (6, 2, 2, 78),
        (7, 0, 4, 75),
    };

    private static readonly int[] Quarters = { 1, 2, 3, 2 };
    private static readonly float[] Even = { 1f, 0.8f, 0.9f, 0.8f };
    /// <summary>Sixteenths grouped 3 + 3 + 2 (twice a bar): where the accents fall.</summary>
    private static readonly float[] Broken = { 0.85f, 0.45f, 0.5f, 0.8f, 0.45f, 0.5f, 0.75f, 0.45f };
    private static readonly double[] Stabs = { 0, 0.75, 1.5, 2, 2.75, 3.5 };
    /// <summary>The violin's spiccato figure over a chord's hand tones (one per sixteenth of a half bar).</summary>
    private static readonly int[] Figure = { 3, 1, 2, 3, 1, 2, 3, 2 };

    public static Stereo[] Render(string cache) => Ensemble.RenderLayers(level => Compose(cache, level));

    private static Ensemble Compose(string cache, int level)
    {
        var q = new Ensemble(cache, bpm: 66, bars: Form.Length * 8, beatsPerBar: 4, level);
        for (int block = 0; block < Form.Length; block++)
        {
            bool theme = Form[block];
            var row = theme ? M : N;
            int first = block * 8;

            for (int i = 0; i < 8; i++)
            {
                int bar = first + i;
                var chord = row[i];
                int root = chord.PizzBass;

                // Calm: quarters, a star note on the "and" of 3 every other bar, the bowed bass under it all.
                q.PianoBar(bar, chord, theme ? 0.3f : 0.34f, Quarters, Even, step: 1);
                if (i % 2 == 1)
                    q.PianoNote(bar, 2.5, 3, chord.Hand[3] + 12, 0.22f, 0.25f);
                q.Bow(bar, 0, 4.2, Math.Max(root, 30), 0.32f);

                if (level == 1)
                {
                    // Tension: a heartbeat (da-dum on 1 and 3), plucks on 1 and 3, a faint tremolo over N.
                    foreach (double beat in new[] { 0.0, 2.0 })
                    {
                        q.PianoNote(bar, beat, 0.5, chord.Bass + 12, 0.36f, -0.25f);
                        q.PianoNote(bar, beat + 0.5, 1, chord.Bass + 12, 0.3f, -0.25f);
                        q.Pluck(bar, beat, 1.5, root, 0.48f);
                    }
                    if (!theme)
                        q.ViolinTrem(bar, 0, 4.2, chord.Hand[1] + 12, 0.28f);
                }
                else if (level == 2)
                {
                    // Battle: broken sixteenths in the bass and the violin, the piano stabbing the same accents.
                    for (int s = 0; s < 16; s++)
                    {
                        float accent = Broken[s % 8];
                        q.Drive(bar, s * 0.25, root, accent * 0.6f);
                        int note = chord.Hand[Figure[s % 8]] + (theme ? 0 : 12);
                        q.ViolinBounce(bar, s * 0.25, note, (theme ? 0.38f : 0.5f) * accent / 0.85f);
                    }
                    foreach (double beat in Stabs)
                    {
                        q.PianoNote(bar, beat, 0.6, chord.Bass, 0.36f, -0.3f);
                        q.PianoNote(bar, beat, 0.6, chord.Bass + 12, 0.32f, -0.25f);
                    }
                }
            }
            if (theme)
                q.ViolinLine(first, 0.46f, Theme, swell: 0.1f); // the same in every version, so the crossfade keeps one violin
        }
        return q;
    }
}

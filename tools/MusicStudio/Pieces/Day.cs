namespace Lordsio.MusicStudio.Pieces;

/// <summary>
/// The day, in three versions the game crossfades between by how hot the fighting is. A Dorian in 7/8 (eighths
/// grouped 2 + 2 + 3) at 240 eighths a minute: 80 bars, 140 s, ten blocks of eight bars on two chord rows —
/// X (Am9 – Cmaj7♯11 – D6/9 – Am9 – Fmaj7 – G6 – Em7 – Am9) and Y (Fmaj9 – G6 – Em7 – Am9 – Fmaj9 – G6 – Esus4 – E7♭9).
/// Calm: the piano's lilting 7/8 arpeggio, the bass on the downbeats, the violin's theme in the middle blocks.
/// Tension adds plucks on every group, a low piano pulse and a high violin tremolo.
/// Battle trades those for driving spiccato in the bass and violin and the piano's low octave stabs.
/// </summary>
public static class Day
{
    private static readonly Chord Am9 = new(33, new[] { 67, 71, 72, 76 });
    private static readonly Chord Cmaj7Sharp11 = new(36, new[] { 66, 67, 71, 76 });
    private static readonly Chord D69 = new(38, new[] { 66, 69, 71, 76 });
    private static readonly Chord Fmaj7 = new(41, new[] { 64, 69, 72, 76 });
    private static readonly Chord G6 = new(43, new[] { 64, 67, 71, 74 });
    private static readonly Chord Em7 = new(40, new[] { 67, 71, 74, 78 });
    private static readonly Chord Fmaj9 = new(41, new[] { 67, 69, 72, 76 });
    private static readonly Chord Esus4 = new(40, new[] { 64, 69, 71, 76 });
    private static readonly Chord E7Flat9 = new(40, new[] { 62, 65, 68, 71 });

    private static readonly Chord[] X = { Am9, Cmaj7Sharp11, D69, Am9, Fmaj7, G6, Em7, Am9 };
    private static readonly Chord[] Y = { Fmaj9, G6, Em7, Am9, Fmaj9, G6, Esus4, E7Flat9 };

    private enum Block { X, XTheme, XAnswer, Y, YLine }

    private static readonly Block[] Form =
        { Block.X, Block.X, Block.XTheme, Block.XAnswer, Block.Y, Block.YLine, Block.XTheme, Block.XAnswer, Block.Y, Block.X };

    /// <summary>The theme over X: (bar, eighth, eighths, note).</summary>
    private static readonly (int, double, double, int)[] Theme =
    {
        (0, 0, 2, 69), (0, 2, 2, 72), (0, 4, 3, 76),
        (1, 0, 7, 74),
        (2, 0, 2, 76), (2, 2, 2, 78), (2, 4, 3, 81),
        (3, 0, 7, 79),
        (4, 0, 4, 81), (4, 4, 3, 84),
        (5, 0, 4, 83), (5, 4, 3, 81),
        (6, 0, 4, 79), (6, 4, 3, 78),
        (7, 0, 7, 76),
    };

    /// <summary>Its answer, from the top down to A.</summary>
    private static readonly (int, double, double, int)[] Answer =
    {
        (0, 0, 4, 84), (0, 4, 3, 83),
        (1, 0, 4, 79), (1, 4, 3, 78),
        (2, 0, 7, 81),
        (3, 0, 2, 76), (3, 2, 2, 79), (3, 4, 3, 81),
        (4, 0, 2, 84), (4, 2, 2, 81), (4, 4, 3, 76),
        (5, 0, 4, 74), (5, 4, 3, 76),
        (6, 0, 4, 71), (6, 4, 3, 74),
        (7, 0, 7, 69),
    };

    /// <summary>The violin over Y, ending on the leading note G♯.</summary>
    private static readonly (int, double, double, int)[] YLine =
    {
        (0, 0, 7, 81),
        (1, 0, 4, 83), (1, 4, 3, 86),
        (2, 0, 7, 79),
        (3, 0, 4, 84), (3, 4, 3, 83),
        (4, 0, 2, 81), (4, 2, 2, 79), (4, 4, 3, 76),
        (5, 0, 7, 74),
        (6, 0, 4, 76), (6, 4, 3, 81),
        (7, 0, 7, 80),
    };

    /// <summary>2 + 2 + 3: low, high | middle, top | low, high, top.</summary>
    private static readonly int[] Lilt = { 0, 2, 1, 3, 0, 2, 3 };
    /// <summary>The same, brighter, for Y (5, 6: hand tones an octave up).</summary>
    private static readonly int[] LiltHigh = { 1, 3, 2, 5, 1, 3, 6 };
    private static readonly float[] Groups = { 1f, 0.6f, 0.85f, 0.6f, 0.85f, 0.6f, 0.65f };
    /// <summary>The violin's spiccato figure over a chord's hand tones.</summary>
    private static readonly int[] Fiddle = { 3, 2, 3, 1, 3, 2, 1 };

    public static Stereo[] Render(string cache) => Ensemble.RenderLayers(level => Compose(cache, level));

    private static Ensemble Compose(string cache, int level)
    {
        var q = new Ensemble(cache, bpm: 240, bars: Form.Length * 8, beatsPerBar: 7, level);
        for (int block = 0; block < Form.Length; block++)
        {
            var kind = Form[block];
            var row = kind is Block.Y or Block.YLine ? Y : X;
            bool melody = kind is Block.XTheme or Block.XAnswer or Block.YLine;
            int first = block * 8;

            for (int i = 0; i < 8; i++)
            {
                int bar = first + i;
                var chord = row[i];
                int root = chord.PizzBass;

                // Calm: the piano, lighter under the violin; the bass on the downbeat from the second block.
                q.PianoBar(bar, chord, melody ? 0.4f : 0.46f, row == Y ? LiltHigh : Lilt, Groups, step: 1);
                if (block > 0)
                    q.Pluck(bar, 0, 3, root, 0.55f);

                if (level == 1)
                {
                    // Tension: plucks on the other two groups, a low pulse on the last group, a high tremolo.
                    q.Pluck(bar, 2, 2, root, 0.42f);
                    q.Pluck(bar, 4, 3, root + 7 <= 47 ? root + 7 : root - 5, 0.48f);
                    q.PianoNote(bar, 4, 3, chord.Bass, 0.35f, -0.3f);
                    if (!melody && i % 2 == 0)
                    {
                        int high = chord.Hand[3] + 12 <= 88 ? chord.Hand[3] + 12 : chord.Hand[3];
                        q.ViolinTrem(bar, 0, 14, high, 0.35f);
                    }
                }
                else if (level == 2)
                {
                    // Battle: driving eighths in the bass and the violin, the piano's low octaves on each group.
                    float[] drive = { 0.8f, 0.5f, 0.75f, 0.5f, 0.75f, 0.5f, 0.55f };
                    for (int e = 0; e < 7; e++)
                        q.Drive(bar, e, root, drive[e]);
                    foreach (int beat in new[] { 0, 2, 4 })
                    {
                        q.PianoNote(bar, beat, 1.5, chord.Bass, 0.42f, -0.3f);
                        q.PianoNote(bar, beat, 1.5, chord.Bass + 12, 0.38f, -0.25f);
                    }
                    for (int e = 0; e < 7; e++)
                    {
                        int note = chord.Hand[Fiddle[e]] - (melody ? 12 : 0); // under the melody, an octave down
                        q.ViolinBounce(bar, e, Math.Max(note, 55), (melody ? 0.4f : 0.55f) * drive[e] / 0.8f);
                    }
                }
            }

            switch (kind)
            {
                case Block.XTheme: q.ViolinLine(first, 0.45f, Theme, swell: 0.1f); break;
                case Block.XAnswer: q.ViolinLine(first, 0.45f, Answer, swell: 0.1f); break;
                case Block.YLine: q.ViolinLine(first, 0.45f, YLine, swell: 0.1f); break;
            }
        }
        return q;
    }
}

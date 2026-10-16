namespace Lordsio.MusicStudio.Pieces;

/// <summary>
/// The main menu: the game's theme, unhurried and welcoming. D Dorian at 80 bpm in straight eighths, a two-minute
/// loop that grows and settles back:
/// intro (4 bars) the piano alone in quarter notes;
/// A (8) the 3+3+2 ostinato over the rising loop Dm9 – Em7 – Fmaj7♯11 – G6/9, the bass creeping in;
/// B (8) the violin sings the theme;
/// C (8) a bridge borrowed from D minor (B♭maj9 – Am7 – Gm9 – A7), wide arpeggios over a bowed bass;
/// D (8) the theme again, fuller: a walking bass and the piano in octaves;
/// coda (4) thinning out to the piano's last chord, which leads back into the intro.
/// </summary>
public static class Menu
{
    private static readonly Chord Dm9 = new(38, new[] { 65, 69, 72, 76 });
    private static readonly Chord Em7 = new(40, new[] { 67, 71, 74, 76 });
    private static readonly Chord Fmaj7Sharp11 = new(41, new[] { 69, 71, 72, 76 });
    private static readonly Chord G69 = new(43, new[] { 69, 71, 74, 76 });
    private static readonly Chord[] Loop = { Dm9, Em7, Fmaj7Sharp11, G69 };

    private static readonly Chord BbMaj9 = new(34, new[] { 65, 69, 72, 74 });
    private static readonly Chord Am7 = new(33, new[] { 64, 67, 72, 76 });
    private static readonly Chord Gm9 = new(31, new[] { 65, 69, 70, 74 });
    private static readonly Chord A7Sus4 = new(33, new[] { 64, 67, 69, 74 });
    private static readonly Chord A7Flat9 = new(33, new[] { 61, 67, 70, 76 });
    private static readonly Chord[] Bridge = { BbMaj9, Am7, Gm9, A7Sus4, BbMaj9, Am7, Gm9, A7Flat9 };

    /// <summary>The theme: (bar, beat, beats, note) over eight bars of the loop.</summary>
    private static readonly (int, double, double, int)[] Theme =
    {
        (0, 0, 2.5, 81), (0, 2.5, 0.5, 79), (0, 3, 1, 77),
        (1, 0, 3, 76), (1, 3, 1, 74),
        (2, 0, 1.5, 76), (2, 1.5, 2.5, 83),
        (3, 0, 4, 81),
        (4, 0, 2.5, 86), (4, 2.5, 0.5, 84), (4, 3, 1, 81),
        (5, 0, 2, 83), (5, 2, 2, 79),
        (6, 0, 1.5, 81), (6, 1.5, 1.5, 79), (6, 3, 1, 76),
    };

    /// <summary>The theme's last bar: a pickup into the bridge, or settling on D at the end.</summary>
    private static readonly (int, double, double, int)[] ThemeToBridge = { (7, 0, 3, 74), (7, 3, 1, 76) };
    private static readonly (int, double, double, int)[] ThemeEnd = { (7, 0, 4, 74) };

    private static readonly (int, double, double, int)[] BridgeLine =
    {
        (0, 0, 3, 81), (0, 3, 1, 79),
        (1, 0, 4, 76),
        (2, 0, 2, 77), (2, 2, 2, 74),
        (3, 0, 4, 76),
        (4, 0, 2, 81), (4, 2, 2, 84),
        (5, 0, 3, 83), (5, 3, 1, 79),
        (6, 0, 2, 81), (6, 2, 2, 82),
        (7, 0, 4, 81),
    };

    private static readonly int[] Quarters = { 0, 2, 3, 2 };
    private static readonly float[] QuarterAccents = { 0.9f, 0.6f, 0.7f, 0.6f };
    /// <summary>The ostinato: eighths grouped 3 + 3 + 2.</summary>
    private static readonly int[] Ostinato = { 0, 2, 3, 1, 2, 3, 0, 2 };
    /// <summary>The same with a high note to close the bar (6: the third hand tone an octave up).</summary>
    private static readonly int[] OstinatoHigh = { 0, 2, 3, 1, 2, 3, 0, 6 };
    private static readonly float[] ThreeThreeTwo = { 1f, 0.6f, 0.65f, 0.9f, 0.6f, 0.65f, 0.85f, 0.6f };
    /// <summary>A wide arpeggio up and back (5: the second hand tone an octave up).</summary>
    private static readonly int[] Arpeggio = { 0, 1, 2, 3, 5, 3, 2, 1 };
    private static readonly float[] ArpeggioAccents = { 0.9f, 0.55f, 0.6f, 0.65f, 0.75f, 0.6f, 0.55f, 0.5f };

    public static Stereo Render(string cache)
    {
        const int intro = 0, a = 4, b = 12, c = 20, d = 28, coda = 36, bars = 40;
        var q = new Ensemble(cache, bpm: 80, bars, seed: 11);

        for (int bar = intro; bar < a; bar++)
            q.PianoBar(bar, Loop[bar % 4], 0.52f, Quarters, QuarterAccents, step: 1);

        for (int bar = a; bar < b; bar++)
        {
            var chord = Loop[bar % 4];
            q.PianoBar(bar, chord, 0.52f, Ostinato, ThreeThreeTwo);
            if (bar >= a + 4)
            {
                q.Pluck(bar, 0, 2.5, chord.PizzBass, 0.6f);
                q.Pluck(bar, 2.5, 1.5, chord.PizzBass, 0.4f);
            }
        }

        for (int bar = b; bar < c; bar++)
        {
            var chord = Loop[bar % 4];
            q.PianoBar(bar, chord, 0.5f, bar % 2 == 1 ? OstinatoHigh : Ostinato, ThreeThreeTwo);
            PlainBass(q, bar, chord);
        }
        q.ViolinLine(b, 0.55f, Theme.Concat(ThemeToBridge), swell: 0.1f);

        for (int bar = c; bar < d; bar++)
        {
            var chord = Bridge[bar - c];
            q.PianoBar(bar, chord, 0.45f, Arpeggio, ArpeggioAccents);
            q.Bow(bar, 0, 4, chord.Bass, 0.5f);
        }
        q.ViolinLine(c, 0.5f, BridgeLine, swell: 0.1f);

        for (int bar = d; bar < coda; bar++)
        {
            var chord = Loop[bar % 4];
            q.PianoBar(bar, chord, 0.54f, bar % 2 == 1 ? OstinatoHigh : Ostinato, ThreeThreeTwo, octaves: true);
            WalkingBass(q, bar, chord, Loop[(bar + 1) % 4]);
        }
        q.ViolinLine(d, 0.6f, Theme.Concat(ThemeEnd), swell: 0.1f);

        // Coda: the ostinato thins to quarters, then two rolled chords; the last (G6/9) rings into the intro's Dm9.
        q.PianoBar(coda, Dm9, 0.5f, Quarters, QuarterAccents, step: 1);
        q.PianoBar(coda + 1, Em7, 0.47f, Quarters, QuarterAccents, step: 1);
        q.PianoChord(coda + 2, 0, Fmaj7Sharp11, 0.45f, 4);
        q.PianoChord(coda + 3, 0, G69, 0.42f, 4);
        q.Pluck(coda, 0, 4, Dm9.PizzBass, 0.5f);
        q.Pluck(coda + 1, 0, 4, Em7.PizzBass, 0.45f);
        q.Bow(coda + 2, 0, 8, Fmaj7Sharp11.Bass, 0.35f);

        return q.Finish();
    }

    /// <summary>Root on 1, again on the "and" of 2, the fifth (or the fourth below) on 4.</summary>
    private static void PlainBass(Ensemble q, int bar, Chord chord)
    {
        int root = chord.PizzBass, fifth = root + 7 <= 47 ? root + 7 : root - 5;
        q.Pluck(bar, 0, 1.4, root, 0.75f);
        q.Pluck(bar, 1.5, 1, root, 0.5f);
        q.Pluck(bar, 3, 0.9, fifth, 0.6f);
    }

    /// <summary>A busier line: root, fifth, octave or root, and a chromatic step into the next chord's root.</summary>
    private static void WalkingBass(Ensemble q, int bar, Chord chord, Chord next)
    {
        int root = chord.PizzBass, fifth = root + 7 <= 47 ? root + 7 : root - 5;
        int target = next.PizzBass, approach = target - 1 >= 28 ? target - 1 : target + 1;
        q.Pluck(bar, 0, 1, root, 0.8f);
        q.Pluck(bar, 1, 1, fifth, 0.55f);
        q.Pluck(bar, 2, 1, root + 12 <= 47 ? root + 12 : root, 0.6f);
        q.Pluck(bar, 3, 0.5, fifth, 0.5f);
        q.Pluck(bar, 3.5, 0.5, approach, 0.55f);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace Lordsio.Audio;

public enum MusicTrack
{
    Menu,
    Peace,
    Battle,
    /// <summary>The calm of the night (when there is no battle): harp, bells and crickets.</summary>
    Night,
}

/// <summary>
/// Placeholder music composed and synthesised in code, in the spirit of <see cref="SoundBank"/>: modal chord
/// progressions arranged in sections (intro, theme A, theme B, a breakdown, the theme again) with a plucked lute
/// or harp (Karplus–Strong), a chorused pad, a recorder, horn or bell melody with a ping-pong echo, and, for
/// battle, frame drums and low strings; the day has birdsong and the night crickets. Rendered in stereo with each
/// instrument in its place, as a seamless loop (the tails of the last notes are folded back onto the start); the
/// music bus adds reverb (<see cref="Core.GameSettings"/>). Pure C# with no engine calls, so it can run on a
/// worker thread; the same track always renders the same way.
/// </summary>
public static partial class MusicComposer
{
    public const int Rate = 32000;

    /// <summary>Semitones of the church modes used, from the root.</summary>
    private static readonly int[] Dorian = { 0, 2, 3, 5, 7, 9, 10 };
    private static readonly int[] Aeolian = { 0, 2, 3, 5, 7, 8, 10 };

    private enum Lead { Flute, Horn, Bell }

    private enum Ambience { None, Birds, Crickets }

    private sealed record Piece(int Bpm, int RootMidi, int[] Mode, int Seed, Lead Lead, Ambience Ambience);

    /// <summary>What plays in a section.</summary>
    [Flags]
    private enum Part
    {
        Pad = 1,
        Bass = 2,
        Lute = 4,
        /// <summary>The lute fingerpicks a syncopated pattern instead of plain broken chords.</summary>
        LuteAlt = 8,
        Melody = 16,
        Harp = 32,
        Drums = 64,
        /// <summary>Busier drums with sixteenth taps (the climax of the battle).</summary>
        DrumsBusy = 128,
        Strings = 256,
        /// <summary>A long, very soft bass note per bar instead of plucked roots.</summary>
        LongBass = 512,
    }

    /// <param name="Phrase">Which melody plays: sections with the same letter share it (over their own chords).</param>
    private sealed record Section(char Phrase, Part Parts, params int[] Chords);

    public static float[] Render(MusicTrack track) => track switch
    {
        MusicTrack.Menu => Menu(),
        MusicTrack.Peace => Peace(),
        MusicTrack.Night => Night(),
        _ => Battle(),
    };

    // ---------------------------------------------------------------- the pieces

    /// <summary>D Dorian, unhurried and welcoming: the theme from the first bar.</summary>
    private static float[] Menu()
    {
        const Part full = Part.Pad | Part.Bass | Part.Lute | Part.Melody;
        return Arrange(new Piece(84, 50, Dorian, 11, Lead.Flute, Ambience.None), new[]
        {
            new Section('A', full, 0, 3, 6, 0, 0, 3, 4, 4),
            new Section('B', Part.Pad | Part.Bass | Part.LuteAlt | Part.Melody, 5, 3, 6, 0, 5, 3, 4, 4),
            new Section('-', Part.Pad | Part.Harp, 0, 6, 3, 4),
            new Section('A', full, 0, 3, 6, 0),
        });
    }

    /// <summary>D Dorian with birdsong: a quiet introduction, two themes, a harp interlude, the theme again.</summary>
    private static float[] Peace()
    {
        const Part full = Part.Pad | Part.Bass | Part.Lute | Part.Melody;
        return Arrange(new Piece(76, 50, Dorian, 23, Lead.Flute, Ambience.Birds), new[]
        {
            new Section('-', Part.Pad | Part.Lute, 0, 6, 3, 0),
            new Section('A', full, 0, 6, 3, 0, 0, 6, 3, 4),
            new Section('B', Part.Pad | Part.Bass | Part.LuteAlt | Part.Melody, 5, 6, 3, 4, 5, 3, 6, 4),
            new Section('-', Part.Pad | Part.Harp | Part.LongBass, 3, 0, 6, 4),
            new Section('A', full | Part.Harp, 0, 6, 3, 0, 0, 6, 3, 0),
        });
    }

    /// <summary>A Aeolian: drums and strings build up, the horn calls, a breakdown, then everything at once.</summary>
    private static float[] Battle()
    {
        const Part drive = Part.Pad | Part.Strings | Part.Drums;
        return Arrange(new Piece(126, 45, Aeolian, 37, Lead.Horn, Ambience.None), new[]
        {
            new Section('-', Part.Drums | Part.Strings, 0, 0, 5, 6),
            new Section('A', drive | Part.Melody, 0, 0, 5, 6, 0, 0, 5, 4),
            new Section('B', drive | Part.DrumsBusy | Part.Melody, 3, 3, 5, 6, 3, 5, 6, 4),
            new Section('-', Part.Drums | Part.Pad, 5, 6, 5, 4),
            new Section('A', drive | Part.DrumsBusy | Part.Melody, 0, 0, 5, 6, 0, 5, 6, 0),
        });
    }

    /// <summary>D Aeolian at a walking pace: a slow harp, sparse bells with long echoes, crickets in the grass.</summary>
    private static float[] Night()
    {
        const Part calm = Part.Pad | Part.Harp | Part.LongBass;
        return Arrange(new Piece(64, 50, Aeolian, 53, Lead.Bell, Ambience.Crickets), new[]
        {
            new Section('-', calm, 0, 5, 3, 6),
            new Section('A', calm | Part.Melody, 0, 5, 3, 6, 0, 5, 2, 4),
            new Section('B', calm | Part.Melody, 5, 3, 6, 0, 5, 3, 4, 4),
            new Section('-', Part.Pad | Part.LongBass, 0, 5, 3, 6),
        });
    }

    // ---------------------------------------------------------------- arrangement

    private static float[] Arrange(Piece p, Section[] sections)
    {
        var chords = new List<int>();
        foreach (var section in sections)
            chords.AddRange(section.Chords);
        var song = new Song(p, chords.Count);

        int bar = 0;
        foreach (var section in sections)
        {
            foreach (int chord in section.Chords)
            {
                PlayBar(song, p, section.Parts, bar, chord);
                bar++;
            }
        }

        var melody = Melody(p, chords, sections);
        foreach (var (b, beat, beats, degree) in melody)
        {
            switch (p.Lead)
            {
                case Lead.Flute: song.Flute(b, beat, beats, song.Note(degree, octave: 1), 0.15f, pan: 0.3f); break;
                case Lead.Horn: song.Horn(b, beat, beats, song.Note(degree, octave: 0), 0.13f, pan: 0.15f); break;
                default: song.Bell(b, beat, song.Note(degree, octave: 1), 0.11f, pan: b % 2 == 0 ? -0.35f : 0.35f); break;
            }
        }

        switch (p.Ambience)
        {
            case Ambience.Birds: song.Birds(); break;
            case Ambience.Crickets: song.Crickets(); break;
        }
        return song.Finish();
    }

    private static void PlayBar(Song song, Piece p, Part parts, int bar, int chord)
    {
        if (parts.HasFlag(Part.Pad))
        {
            // Triad, plus the seventh on every other bar for colour.
            foreach (int tone in bar % 2 == 1 ? new[] { 0, 2, 4, 6 } : new[] { 0, 2, 4 })
                song.Pad(bar, 0, 4, song.Note(chord + tone, octave: 0), tone == 6 ? 0.025f : 0.045f);
        }
        if (parts.HasFlag(Part.Bass))
        {
            song.Pluck(bar, 0, 2, song.Note(chord, octave: -1), 0.3f, pan: -0.05f);
            song.Pluck(bar, 2, 2, song.Note(chord + (bar % 2 == 0 ? 4 : 0), octave: -1), 0.24f, pan: -0.05f);
        }
        if (parts.HasFlag(Part.LongBass))
            song.Pad(bar, 0, 4, song.Note(chord, octave: -1), 0.07f, voices: 1);
        if (parts.HasFlag(Part.Lute) || parts.HasFlag(Part.LuteAlt))
        {
            // Broken chords in eighths, or a fingerpicked pattern with a pushed fourth note.
            (float Beat, int Tone)[] pattern = parts.HasFlag(Part.LuteAlt)
                ? new[] { (0f, 0), (0.5f, 4), (1f, 2), (1.75f, 7), (2.5f, 4), (3f, 2), (3.5f, 9) }
                : new[] { (0f, 0), (0.5f, 2), (1f, 4), (1.5f, 7), (2f, 4), (2.5f, 2), (3f, 4), (3.5f, 2) };
            foreach (var (beat, tone) in pattern)
                song.Pluck(bar, beat, 1f, song.Note(chord + tone, octave: 0), beat == 0f ? 0.2f : 0.13f, pan: -0.35f);
        }
        if (parts.HasFlag(Part.Harp))
        {
            // Slow rising arpeggio, high and wide: each note a little further out to the side.
            int[] tones = { 0, 2, 4, 7, 9, 7, 4, 2 };
            float step = p.Bpm < 70 ? 1f : 0.5f;
            for (int i = 0; i * step < 4f; i++)
                song.Pluck(bar, i * step, 2f, song.Note(chord + tones[i % tones.Length], octave: 1), 0.09f,
                    pan: i % 2 == 0 ? -0.55f : 0.55f, soft: true);
        }
        if (parts.HasFlag(Part.Strings))
        {
            for (int i = 0; i < 8; i++)
                song.Strings(bar, i * 0.5f, 0.45f, song.Note(chord + (i % 4 == 3 ? 4 : 0), octave: -1), 0.12f, pan: i % 2 == 0 ? -0.2f : 0.2f);
        }
        if (parts.HasFlag(Part.Drums))
        {
            // Boom on 1 and 3 (and the "and" of 4), taps on the off-beats; busier with sixteenths.
            song.Drum(bar, 0, low: true, 0.6f);
            song.Drum(bar, 2, low: true, 0.5f);
            song.Drum(bar, 3.5f, low: true, 0.35f);
            for (int i = 0; i < 4; i++)
                song.Drum(bar, i + 0.5f, low: false, 0.24f, pan: 0.25f);
            if (parts.HasFlag(Part.DrumsBusy))
            {
                for (int i = 0; i < 4; i++)
                    song.Drum(bar, i + 0.75f, low: false, 0.13f, pan: -0.3f);
                song.Drum(bar, 1.5f, low: true, 0.3f);
            }
        }
    }

    /// <summary>
    /// A melody of scale degrees in phrases of four bars over the whole piece: mostly stepwise, starting each bar on a
    /// chord tone, with a few rhythm cells so it does not sound like a metronome. Sections with the same letter replay
    /// the same phrase over their own chords; '-' sections are left to the accompaniment.
    /// </summary>
    private static List<(int Bar, float Beat, float Beats, int Degree)> Melody(Piece p, List<int> chords, Section[] sections)
    {
        float[][] rhythms = p.Lead switch
        {
            Lead.Bell => new[] { new[] { 2f, 2f }, new[] { 3f, 1f }, new[] { 1f, 3f }, new[] { 4f } },
            Lead.Horn => new[] { new[] { 1f, 1f, 1f, 1f }, new[] { 1.5f, 0.5f, 1f, 1f }, new[] { 1f, 0.5f, 0.5f, 2f }, new[] { 2f, 1f, 1f } },
            _ => new[] { new[] { 2f, 1f, 1f }, new[] { 1f, 1f, 2f }, new[] { 3f, 1f }, new[] { 1.5f, 0.5f, 2f }, new[] { 1f, 0.5f, 0.5f, 2f } },
        };

        var seeds = new Dictionary<char, int>();
        var rng = new Random(p.Seed);
        var all = new List<(int, float, float, int)>();
        int start = 0;
        foreach (var section in sections)
        {
            if (section.Phrase != '-')
            {
                if (!seeds.TryGetValue(section.Phrase, out int seed))
                    seeds[section.Phrase] = seed = rng.Next();
                for (int first = start; first < start + section.Chords.Length; first += 4)
                    all.AddRange(Phrase(first, Math.Min(4, start + section.Chords.Length - first), seed));
            }
            start += section.Chords.Length;
        }
        return all;

        List<(int, float, float, int)> Phrase(int firstBar, int bars, int seed)
        {
            var r = new Random(seed);
            var notes = new List<(int, float, float, int)>();
            int degree = 4;
            for (int b = 0; b < bars; b++)
            {
                int bar = firstBar + b;
                var rhythm = rhythms[r.Next(rhythms.Length)];
                float beat = 0f;
                for (int i = 0; i < rhythm.Length; i++)
                {
                    degree = i == 0
                        ? Nearest(degree, chords[bar] + new[] { 0, 2, 4 }[r.Next(3)])
                        : Math.Clamp(degree + new[] { -2, -1, -1, 1, 1, 2 }[r.Next(6)], 0, 9);
                    // Phrases end long and on the chord's root.
                    bool last = b == bars - 1 && i == rhythm.Length - 1;
                    notes.Add((bar, beat, rhythm[i], last ? Nearest(degree, chords[bar]) : degree));
                    beat += rhythm[i];
                }
            }
            return notes;
        }
    }

    /// <summary>The octave of <paramref name="target"/> (a scale degree) closest to <paramref name="from"/>.</summary>
    private static int Nearest(int from, int target)
    {
        int best = target;
        for (int octave = -2; octave <= 2; octave++)
            if (Math.Abs(target + octave * 7 - from) < Math.Abs(best - from))
                best = target + octave * 7;
        return Math.Clamp(best, 0, 9);
    }
}

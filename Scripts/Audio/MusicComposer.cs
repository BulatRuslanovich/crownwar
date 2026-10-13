using System;
using System.Collections.Generic;

namespace Lordsio.Audio;

public enum MusicTrack
{
    Menu,
    Peace,
    Battle,
}

/// <summary>
/// Placeholder music composed and synthesised in code, in the spirit of <see cref="SoundBank"/>: modal
/// chord progressions with a plucked lute (Karplus–Strong), a soft pad, a recorder or horn melody and,
/// for battle, frame drums and low strings. Each track is rendered as a seamless loop (the tails of the
/// last notes are folded back onto the start). Pure C# with no engine calls, so it can run on a worker
/// thread; the same track always renders the same way.
/// </summary>
public static class MusicComposer
{
    public const int Rate = 22050;

    /// <summary>Semitones of the church modes used, from the root.</summary>
    private static readonly int[] Dorian = { 0, 2, 3, 5, 7, 9, 10 };
    private static readonly int[] Aeolian = { 0, 2, 3, 5, 7, 8, 10 };

    private sealed record Piece(int Bpm, int RootMidi, int[] Mode, int[] Chords, int Seed);

    public static float[] Render(MusicTrack track) => track switch
    {
        // D Dorian: i  IV  VII  i ...
        MusicTrack.Menu => Compose(new Piece(88, 50, Dorian, new[] { 0, 3, 6, 0, 0, 3, 4, 4, 5, 3, 6, 0 }, 11), menu: true),
        MusicTrack.Peace => Compose(new Piece(76, 50, Dorian, new[] { 0, 6, 3, 0, 0, 6, 3, 4, 5, 6, 3, 4, 0, 6, 3, 0 }, 23)),
        // A Aeolian: i  VI  VII ...
        _ => ComposeBattle(new Piece(126, 45, Aeolian, new[] { 0, 0, 5, 6, 0, 0, 5, 4, 3, 3, 5, 6, 0, 5, 6, 0 }, 37)),
    };

    // ---------------------------------------------------------------- arrangements

    private static float[] Compose(Piece p, bool menu = false)
    {
        var song = new Song(p);
        var rng = new Random(p.Seed);
        var melody = Melody(p, rng, notesPerBar: menu ? 4 : 3);
        for (int bar = 0; bar < p.Chords.Length; bar++)
        {
            int chord = p.Chords[bar];
            // Pad: the whole bar, very soft.
            foreach (int tone in new[] { 0, 2, 4 })
                song.Pad(bar, 0, 4, song.Note(chord + tone, octave: 0), 0.05f);
            // Bass: root on beats 1 and 3.
            song.Pluck(bar, 0, 2, song.Note(chord, octave: -1), 0.32f);
            song.Pluck(bar, 2, 2, song.Note(chord + (bar % 2 == 0 ? 4 : 0), octave: -1), 0.26f);
            // Lute: a broken chord in eighths, up and back down.
            int[] pattern = { 0, 2, 4, 7, 4, 2, 4, 2 };
            for (int i = 0; i < 8; i++)
                song.Pluck(bar, i * 0.5f, 1f, song.Note(chord + pattern[i], octave: 0), i == 0 ? 0.22f : 0.15f);
        }
        // The melody enters after an introduction (from the start in the menu).
        foreach (var (bar, beat, beats, degree) in melody)
            if (menu || bar >= 4)
                song.Flute(bar, beat, beats, song.Note(degree, octave: 1), 0.16f);
        return song.Finish();
    }

    private static float[] ComposeBattle(Piece p)
    {
        var song = new Song(p);
        var rng = new Random(p.Seed);
        var melody = Melody(p, rng, notesPerBar: 4);
        for (int bar = 0; bar < p.Chords.Length; bar++)
        {
            int chord = p.Chords[bar];
            foreach (int tone in new[] { 0, 2, 4 })
                song.Pad(bar, 0, 4, song.Note(chord + tone, octave: 0), 0.04f);
            // Low strings: driving eighths on root and fifth.
            for (int i = 0; i < 8; i++)
                song.Strings(bar, i * 0.5f, 0.45f, song.Note(chord + (i % 4 == 3 ? 4 : 0), octave: -1), 0.12f);
            // Frame drums: boom on 1 and 3 (and the "and" of 4), taps on the off-beats.
            song.Drum(bar, 0, low: true, 0.6f);
            song.Drum(bar, 2, low: true, 0.5f);
            song.Drum(bar, 3.5f, low: true, 0.35f);
            for (int i = 0; i < 4; i++)
                song.Drum(bar, i + 0.5f, low: false, 0.25f);
        }
        foreach (var (bar, beat, beats, degree) in melody)
            if (bar >= 4 && bar < 12 || bar >= 14)
                song.Horn(bar, beat, beats, song.Note(degree, octave: 0), 0.14f);
        return song.Finish();
    }

    /// <summary>
    /// A melody of scale degrees in phrases of four bars, A A' B A: mostly stepwise, starting each bar on a
    /// chord tone, with a few rhythm cells so it does not sound like a metronome.
    /// </summary>
    private static List<(int Bar, float Beat, float Beats, int Degree)> Melody(Piece p, Random rng, int notesPerBar)
    {
        float[][] rhythms = notesPerBar >= 4
            ? new[] { new[] { 1f, 1f, 1f, 1f }, new[] { 1.5f, 0.5f, 1f, 1f }, new[] { 1f, 0.5f, 0.5f, 2f }, new[] { 2f, 1f, 1f } }
            : new[] { new[] { 2f, 1f, 1f }, new[] { 1f, 1f, 2f }, new[] { 3f, 1f }, new[] { 1.5f, 0.5f, 2f } };

        List<(int, float, float, int)> Phrase(int firstBar, int seed)
        {
            var r = new Random(seed);
            var notes = new List<(int, float, float, int)>();
            int degree = 4;
            for (int b = 0; b < 4 && firstBar + b < p.Chords.Length; b++)
            {
                int bar = firstBar + b;
                var rhythm = rhythms[r.Next(rhythms.Length)];
                float beat = 0f;
                for (int i = 0; i < rhythm.Length; i++)
                {
                    degree = i == 0
                        ? Nearest(degree, p.Chords[bar] + new[] { 0, 2, 4 }[r.Next(3)])
                        : Math.Clamp(degree + new[] { -2, -1, -1, 1, 1, 2 }[r.Next(6)], 0, 9);
                    // Phrases end long and on the chord's root.
                    bool last = b == 3 && i == rhythm.Length - 1;
                    notes.Add((bar, beat, rhythm[i], last ? Nearest(degree, p.Chords[bar]) : degree));
                    beat += rhythm[i];
                }
            }
            return notes;
        }

        int phraseA = rng.Next(), phraseB = rng.Next();
        var all = new List<(int, float, float, int)>();
        for (int start = 0; start < p.Chords.Length; start += 4)
        {
            int section = start / 4;
            // A A' B A: A' reuses A's notes over the next chords; B is new material.
            all.AddRange(Phrase(start, section == 2 ? phraseB : phraseA));
        }
        return all;
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

    // ---------------------------------------------------------------- synthesis

    /// <summary>A loop being rendered: a bar grid over a sample buffer with room for ringing tails.</summary>
    private sealed class Song
    {
        private readonly Piece _p;
        private readonly float[] _buffer;
        private readonly int _length;
        private readonly float _beatSeconds;
        private readonly Random _noise;

        public Song(Piece p)
        {
            _p = p;
            _beatSeconds = 60f / p.Bpm;
            _length = (int)(p.Chords.Length * 4 * _beatSeconds * Rate);
            _buffer = new float[_length + 4 * Rate]; // tails up to four seconds past the end
            _noise = new Random(p.Seed * 7 + 1);
        }

        /// <summary>Frequency of a scale degree (0 = root; 7 = root an octave up) in the given octave.</summary>
        public float Note(int degree, int octave)
        {
            int steps = ((degree % 7) + 7) % 7;
            int midi = _p.RootMidi + 12 * (octave + (int)Math.Floor(degree / 7.0)) + _p.Mode[steps];
            return 440f * MathF.Pow(2f, (midi - 69) / 12f);
        }

        private int Start(int bar, float beat) => (int)((bar * 4 + beat) * _beatSeconds * Rate);

        private int Samples(float beats) => (int)(beats * _beatSeconds * Rate);

        /// <summary>Plucked string (Karplus–Strong): a burst of noise through a decaying delay line.</summary>
        public void Pluck(int bar, float beat, float beats, float hz, float amp)
        {
            int start = Start(bar, beat);
            int period = Math.Max(2, (int)(Rate / hz));
            var line = new float[period];
            for (int i = 0; i < period; i++)
                line[i] = (float)(_noise.NextDouble() * 2 - 1);
            int count = Samples(beats) + Rate; // let it ring a second past its slot
            for (int i = 0, k = 0; i < count && start + i < _buffer.Length; i++)
            {
                int next = (k + 1) % period;
                float v = line[k];
                line[k] = 0.4985f * (v + line[next]);
                k = next;
                _buffer[start + i] += v * amp * MathF.Min(1f, i / 30f);
            }
        }

        /// <summary>Soft sustained chord tone: a sine with its octave, slow attack and release.</summary>
        public void Pad(int bar, float beat, float beats, float hz, float amp)
        {
            int start = Start(bar, beat), count = Samples(beats);
            float attack = 0.5f * Rate, release = 0.8f * Rate;
            for (int i = 0; i < count + release && start + i < _buffer.Length; i++)
            {
                float t = i / (float)Rate;
                float env = MathF.Min(1f, i / attack) * (i < count ? 1f : 1f - (i - count) / release);
                _buffer[start + i] += (MathF.Sin(MathF.Tau * hz * t) + 0.35f * MathF.Sin(MathF.Tau * hz * 2.003f * t)) * env * amp;
            }
        }

        /// <summary>Recorder: a breathy sine with vibrato that grows in after the attack.</summary>
        public void Flute(int bar, float beat, float beats, float hz, float amp)
        {
            int start = Start(bar, beat), count = Samples(beats * 0.92f);
            float attack = 0.04f * Rate, release = 0.12f * Rate;
            float phase = 0f;
            for (int i = 0; i < count + release && start + i < _buffer.Length; i++)
            {
                float t = i / (float)Rate;
                float vibrato = 1f + 0.006f * MathF.Min(1f, t / 0.3f) * MathF.Sin(MathF.Tau * 5.2f * t);
                phase += hz * vibrato / Rate;
                float env = MathF.Min(1f, i / attack) * (i < count ? 1f : 1f - (i - count) / release);
                float breath = (float)(_noise.NextDouble() * 2 - 1) * 0.04f;
                _buffer[start + i] += (MathF.Sin(MathF.Tau * phase) + 0.12f * MathF.Sin(MathF.Tau * phase * 3f) + breath) * env * amp;
            }
        }

        /// <summary>Horn: a rounded square wave (tanh of a hot sine) with a firm attack.</summary>
        public void Horn(int bar, float beat, float beats, float hz, float amp)
        {
            int start = Start(bar, beat), count = Samples(beats * 0.9f);
            float attack = 0.06f * Rate, release = 0.15f * Rate;
            for (int i = 0; i < count + release && start + i < _buffer.Length; i++)
            {
                float t = i / (float)Rate;
                float env = MathF.Min(1f, i / attack) * (i < count ? 1f : 1f - (i - count) / release);
                _buffer[start + i] += MathF.Tanh(2.2f * MathF.Sin(MathF.Tau * hz * t)) * env * amp * 0.7f;
            }
        }

        /// <summary>Bowed low strings: a sawtooth softened by a one-pole low-pass.</summary>
        public void Strings(int bar, float beat, float beats, float hz, float amp)
        {
            int start = Start(bar, beat), count = Samples(beats);
            float attack = 0.02f * Rate, release = 0.06f * Rate, low = 0f;
            for (int i = 0; i < count + release && start + i < _buffer.Length; i++)
            {
                float t = i / (float)Rate;
                float saw = 2f * (hz * t - MathF.Floor(hz * t + 0.5f));
                low += 0.12f * (saw - low);
                float env = MathF.Min(1f, i / attack) * (i < count ? 1f : 1f - (i - count) / release);
                _buffer[start + i] += low * env * amp;
            }
        }

        /// <summary>Frame drum: a low boom whose pitch falls, or a short muffled tap.</summary>
        public void Drum(int bar, float beat, bool low, float amp)
        {
            int start = Start(bar, beat);
            int count = (int)((low ? 0.35f : 0.08f) * Rate);
            float filtered = 0f;
            for (int i = 0; i < count && start + i < _buffer.Length; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)(_noise.NextDouble() * 2 - 1);
                filtered += 0.25f * (noise - filtered);
                float v = low
                    ? MathF.Sin(MathF.Tau * (55f + 45f * MathF.Exp(-t / 0.04f)) * t) * MathF.Exp(-t / 0.12f) + filtered * MathF.Exp(-t / 0.02f) * 0.5f
                    : filtered * MathF.Exp(-t / 0.018f);
                _buffer[start + i] += v * amp;
            }
        }

        /// <summary>Folds the tails onto the start (a seamless loop), warms the sound and normalises it.</summary>
        public float[] Finish()
        {
            var loop = new float[_length];
            for (int i = 0; i < _buffer.Length; i++)
                loop[i % _length] += _buffer[i];
            // Gentle low-pass, run twice round the loop so its state at the seam matches.
            float low = 0f;
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < _length; i++)
                {
                    low += 0.45f * (loop[i] - low);
                    if (pass == 1)
                        loop[i] = low;
                }
            // Same loudness for every track (drum peaks would make a peak-normalised battle track quiet),
            // with a soft limiter instead of clipping.
            const float TargetRms = 0.17f, Ceiling = 0.85f;
            double energy = 0;
            foreach (float v in loop)
                energy += v * v;
            float gain = TargetRms / MathF.Max(1e-6f, MathF.Sqrt((float)(energy / _length)));
            for (int i = 0; i < _length; i++)
                loop[i] = Ceiling * MathF.Tanh(loop[i] * gain / Ceiling);
            return loop;
        }
    }
}

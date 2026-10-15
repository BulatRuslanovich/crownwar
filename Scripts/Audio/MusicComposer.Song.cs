using System;
using System.Linq;

namespace Lordsio.Audio;

public static partial class MusicComposer
{
    /// <summary>A stereo loop being rendered: a bar grid over two sample buffers with room for ringing tails.</summary>
    private sealed class Song
    {
        private const float TailSeconds = 5f;

        private readonly Piece _p;
        private readonly float[] _left;
        private readonly float[] _right;
        /// <summary>The melody, also sent to the ping-pong echo.</summary>
        private readonly float[] _echoSend;
        private readonly int _length;
        private readonly float _beatSeconds;
        private readonly Random _noise;

        public Song(Piece p, int bars)
        {
            _p = p;
            _beatSeconds = 60f / p.Bpm;
            _length = (int)(bars * 4 * _beatSeconds * Rate);
            int size = _length + (int)(TailSeconds * Rate);
            _left = new float[size];
            _right = new float[size];
            _echoSend = new float[size];
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

        /// <summary>Equal-power gains of a position (−1 left … +1 right).</summary>
        private static (float Left, float Right) Pan(float pan)
        {
            float angle = (Math.Clamp(pan, -1f, 1f) + 1f) * MathF.PI / 4f;
            return (MathF.Cos(angle), MathF.Sin(angle));
        }

        /// <summary>Adds a sample at a stereo position, optionally to the echo too.</summary>
        private void Put(int index, float value, (float Left, float Right) at, bool echo = false)
        {
            if (index < 0 || index >= _left.Length)
                return;
            _left[index] += value * at.Left;
            _right[index] += value * at.Right;
            if (echo)
                _echoSend[index] += value;
        }

        /// <summary>
        /// Plucked string (Karplus–Strong): a burst of noise through a decaying delay line. A soft pluck (harp)
        /// starts from smoothed noise, which takes the edge off the attack.
        /// </summary>
        public void Pluck(int bar, float beat, float beats, float hz, float amp, float pan = 0f, bool soft = false)
        {
            var at = Pan(pan);
            int start = Start(bar, beat);
            int period = Math.Max(2, (int)(Rate / hz));
            var line = new float[period];
            float smooth = 0f;
            for (int i = 0; i < period; i++)
            {
                float n = Noise();
                smooth += (soft ? 0.35f : 1f) * (n - smooth);
                line[i] = smooth;
            }
            float decay = soft ? 0.4992f : 0.4988f;
            int count = Samples(beats) + Rate * 3 / 2; // let it ring past its slot
            for (int i = 0, k = 0; i < count; i++)
            {
                int next = (k + 1) % period;
                float v = line[k];
                line[k] = decay * (v + line[next]);
                k = next;
                Put(start + i, v * amp * MathF.Min(1f, i / 40f), at);
            }
        }

        /// <summary>
        /// Soft sustained chord tone with a slow swell: three voices a few cents apart, spread left, centre and right,
        /// with a gentle tremolo, so the pad breathes and fills the stereo field.
        /// </summary>
        public void Pad(int bar, float beat, float beats, float hz, float amp, int voices = 3)
        {
            int start = Start(bar, beat), count = Samples(beats);
            float attack = 0.9f * Rate, release = 1.4f * Rate;
            float[] detune = voices == 1 ? new[] { 0f } : new[] { -0.004f, 0f, 0.004f };
            var gains = (voices == 1 ? new[] { 0f } : new[] { -0.7f, 0f, 0.7f }).Select(Pan).ToArray();
            for (int i = 0; i < count + release; i++)
            {
                float t = i / (float)Rate;
                float env = MathF.Min(1f, i / attack) * (i < count ? 1f : MathF.Max(0f, 1f - (i - count) / release));
                float tremolo = 1f + 0.08f * MathF.Sin(MathF.Tau * 0.35f * t);
                for (int v = 0; v < detune.Length; v++)
                {
                    float f = hz * (1f + detune[v]);
                    float s = MathF.Sin(MathF.Tau * f * t) + 0.3f * MathF.Sin(MathF.Tau * f * 2f * t) + 0.12f * MathF.Sin(MathF.Tau * f * 3f * t);
                    Put(start + i, s * env * tremolo * amp / detune.Length * 1.6f, gains[v]);
                }
            }
        }

        /// <summary>Recorder: a breathy sine with vibrato that grows in after the attack; it also feeds the echo.</summary>
        public void Flute(int bar, float beat, float beats, float hz, float amp, float pan)
        {
            var at = Pan(pan);
            int start = Start(bar, beat), count = Samples(beats * 0.92f);
            float attack = 0.05f * Rate, release = 0.15f * Rate;
            float phase = 0f, breathLow = 0f;
            for (int i = 0; i < count + release; i++)
            {
                float t = i / (float)Rate;
                float vibrato = 1f + 0.006f * MathF.Min(1f, t / 0.3f) * MathF.Sin(MathF.Tau * 5.2f * t);
                phase += hz * vibrato / Rate;
                float env = Envelope(i, attack, count, release);
                breathLow += 0.3f * (Noise() - breathLow);
                Put(start + i, (MathF.Sin(MathF.Tau * phase) + 0.12f * MathF.Sin(MathF.Tau * phase * 3f) + breathLow * 0.06f) * env * amp, at, echo: true);
            }
        }

        /// <summary>Horn: a rounded square wave (tanh of a hot sine) with a firm attack; it also feeds the echo.</summary>
        public void Horn(int bar, float beat, float beats, float hz, float amp, float pan)
        {
            var at = Pan(pan);
            int start = Start(bar, beat), count = Samples(beats * 0.9f);
            float attack = 0.06f * Rate, release = 0.15f * Rate;
            for (int i = 0; i < count + release; i++)
            {
                float t = i / (float)Rate;
                float env = Envelope(i, attack, count, release);
                Put(start + i, MathF.Tanh(2.2f * MathF.Sin(MathF.Tau * hz * t)) * env * amp * 0.7f, at, echo: true);
            }
        }

        /// <summary>
        /// Bell: two-operator FM (a modulator at 3.5× the pitch whose depth fades), for a glassy strike that rings
        /// for a couple of seconds; it feeds the echo, which carries it far into the night.
        /// </summary>
        public void Bell(int bar, float beat, float hz, float amp, float pan)
        {
            var at = Pan(pan);
            int start = Start(bar, beat), count = (int)(2.6f * Rate);
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)Rate;
                float index = 2.2f * MathF.Exp(-t / 0.25f);
                float v = MathF.Sin(MathF.Tau * hz * t + index * MathF.Sin(MathF.Tau * hz * 3.5f * t)) * MathF.Exp(-t / 0.9f);
                Put(start + i, v * amp * MathF.Min(1f, i / 30f), at, echo: true);
            }
        }

        /// <summary>Bowed low strings: a sawtooth softened by a one-pole low-pass.</summary>
        public void Strings(int bar, float beat, float beats, float hz, float amp, float pan)
        {
            var at = Pan(pan);
            int start = Start(bar, beat), count = Samples(beats);
            float attack = 0.02f * Rate, release = 0.06f * Rate, low = 0f;
            for (int i = 0; i < count + release; i++)
            {
                float t = i / (float)Rate;
                float saw = 2f * (hz * t - MathF.Floor(hz * t + 0.5f));
                low += 0.09f * (saw - low);
                float env = Envelope(i, attack, count, release);
                Put(start + i, low * env * amp, at);
            }
        }

        /// <summary>Frame drum: a low boom whose pitch falls, or a short muffled tap.</summary>
        public void Drum(int bar, float beat, bool low, float amp, float pan = 0f)
        {
            var at = Pan(pan);
            int start = Start(bar, beat);
            int count = (int)((low ? 0.35f : 0.08f) * Rate);
            float filtered = 0f;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)Rate;
                float noise = Noise();
                filtered += 0.2f * (noise - filtered);
                float v = low
                    ? MathF.Sin(MathF.Tau * (55f + 45f * MathF.Exp(-t / 0.04f)) * t) * MathF.Exp(-t / 0.12f) + filtered * MathF.Exp(-t / 0.02f) * 0.5f
                    : filtered * MathF.Exp(-t / 0.018f);
                Put(start + i, v * amp, at);
            }
        }

        /// <summary>Birdsong: now and then a bird somewhere to the side sings two to four quick chirps.</summary>
        public void Birds()
        {
            var r = new Random(_p.Seed * 13 + 5);
            for (float at = 1.5f; at < _length / (float)Rate - 1f; at += 2.5f + (float)r.NextDouble() * 4f)
            {
                var where = Pan((float)r.NextDouble() * 1.6f - 0.8f);
                float pitch = 2600f + (float)r.NextDouble() * 1600f;
                int chirps = 2 + r.Next(3);
                for (int c = 0; c < chirps; c++)
                {
                    int start = (int)((at + c * 0.13f) * Rate), count = (int)(0.08f * Rate);
                    float phase = 0f;
                    for (int i = 0; i < count; i++)
                    {
                        float u = i / (float)count;
                        float hz = pitch * (1f + 0.45f * MathF.Sin(MathF.PI * u)) * (c % 2 == 0 ? 1f : 1.12f);
                        phase += hz / Rate;
                        Put(start + i, MathF.Sin(MathF.Tau * phase) * MathF.Sin(MathF.PI * u) * 0.022f, where);
                    }
                }
            }
        }

        /// <summary>Two crickets in the grass, one on each side, trilling at their own pace through the whole loop.</summary>
        public void Crickets()
        {
            foreach (var (hz, every, offset, pan) in new[] { (4700f, 0.9f, 0.2f, -0.6f), (5300f, 1.3f, 0.65f, 0.55f) })
            {
                var where = Pan(pan);
                for (float at = offset; at < _length / (float)Rate; at += every)
                {
                    // A trill: three pulses of 18 ms.
                    for (int pulse = 0; pulse < 3; pulse++)
                    {
                        int start = (int)((at + pulse * 0.036f) * Rate), count = (int)(0.018f * Rate);
                        for (int i = 0; i < count; i++)
                        {
                            float u = i / (float)count;
                            Put(start + i, MathF.Sin(MathF.Tau * hz * i / Rate) * MathF.Sin(MathF.PI * u) * 0.014f, where);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Folds the tails onto the start (a seamless loop), adds the melody's ping-pong echo (dotted eighths,
        /// bouncing left and right and fading), warms the sound, levels the loudness and interleaves left/right.
        /// </summary>
        public float[] Finish()
        {
            var left = Fold(_left);
            var right = Fold(_right);
            var send = Fold(_echoSend);

            int delay = (int)(0.75f * _beatSeconds * Rate);
            float gain = 0.42f;
            for (int tap = 1; tap <= 5; tap++, gain *= 0.55f)
            {
                var target = tap % 2 == 1 ? left : right;
                int shift = tap * delay;
                for (int i = 0; i < _length; i++)
                    target[(i + shift) % _length] += send[i] * gain;
            }

            // Gentle low-pass, run twice round the loop so its state at the seam matches.
            foreach (var channel in new[] { left, right })
            {
                float low = 0f;
                for (int pass = 0; pass < 2; pass++)
                    for (int i = 0; i < _length; i++)
                    {
                        low += 0.62f * (channel[i] - low);
                        if (pass == 1)
                            channel[i] = low;
                    }
            }

            // Same loudness for every track (drum peaks would make a peak-normalised battle track quiet),
            // with a soft limiter instead of clipping.
            const float TargetRms = 0.16f, Ceiling = 0.85f;
            double energy = 0;
            for (int i = 0; i < _length; i++)
                energy += left[i] * left[i] + right[i] * right[i];
            float level = TargetRms / MathF.Max(1e-6f, MathF.Sqrt((float)(energy / (2.0 * _length))));
            var stereo = new float[_length * 2];
            for (int i = 0; i < _length; i++)
            {
                stereo[2 * i] = Ceiling * MathF.Tanh(left[i] * level / Ceiling);
                stereo[2 * i + 1] = Ceiling * MathF.Tanh(right[i] * level / Ceiling);
            }
            return stereo;
        }

        private float[] Fold(float[] buffer)
        {
            var loop = new float[_length];
            for (int i = 0; i < buffer.Length; i++)
                loop[i % _length] += buffer[i];
            return loop;
        }
        /// <summary>A note's loudness at sample <paramref name="i"/>: a linear attack, held for <paramref name="count"/> samples, then a linear release.</summary>
        private static float Envelope(int i, float attack, int count, float release) =>
            MathF.Min(1f, i / attack) * (i < count ? 1f : 1f - (i - count) / release);

        /// <summary>White noise in [-1, 1), from the song's own seeded generator (the same track always renders the same).</summary>
        private float Noise() => (float)(_noise.NextDouble() * 2 - 1);
    }
}

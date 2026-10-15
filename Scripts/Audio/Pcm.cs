using System;

namespace Lordsio.Audio;

/// <summary>Synthesised samples turned into the bytes of an <c>AudioStreamWav</c>.</summary>
internal static class Pcm
{
    /// <summary>
    /// The first <paramref name="count"/> samples (−1..1, times <paramref name="gain"/>) as 16-bit little-endian PCM,
    /// clamped so a stray peak clips instead of wrapping around.
    /// </summary>
    public static byte[] Encode16(float[] samples, int count, float gain = 1f)
    {
        var bytes = new byte[count * 2];
        for (int i = 0; i < count; i++)
        {
            short s = (short)Math.Clamp(samples[i] * gain * short.MaxValue, short.MinValue, short.MaxValue);
            bytes[i * 2] = (byte)s;
            bytes[i * 2 + 1] = (byte)(s >> 8);
        }
        return bytes;
    }
}

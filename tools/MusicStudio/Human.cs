namespace Lordsio.MusicStudio;

/// <summary>
/// Deterministic "randomness" for a human touch: a value that depends only on what it is for (a note's time, pitch,
/// a salt), never on how many were drawn before. A piece rendered at different intensities adds or leaves out parts,
/// and the notes the versions share must still come out identical.
/// </summary>
public static class Human
{
    public static int Hash(double a, double b, int salt)
    {
        ulong h = 1469598103934665603UL ^ (ulong)salt;
        foreach (long v in new[] { BitConverter.DoubleToInt64Bits(a), BitConverter.DoubleToInt64Bits(b) })
        {
            h ^= (ulong)v;
            h *= 1099511628211UL;
            h ^= h >> 29;
        }
        h *= 0x9E3779B97F4A7C15UL;
        return (int)(h >> 32);
    }

    /// <summary>A value in −1..1 for this time, pitch and salt.</summary>
    public static double Signed(double time, double midi, int salt) =>
        (Hash(Math.Round(time * 1000), Math.Round(midi * 100), salt) & 0xFFFFFF) / (double)0x7FFFFF - 1;
}

namespace Lordsio.MusicStudio;

/// <summary>Stereo audio as two float channels (−1..1) at a sample rate.</summary>
public sealed record Stereo(float[] Left, float[] Right, int Rate)
{
    public int Length => Left.Length;
}

/// <summary>Reads and writes RIFF/WAVE files: 16/24/32-bit integer and 32-bit float PCM, mono or stereo.</summary>
public static class Wav
{
    public static Stereo Read(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        if (new string(reader.ReadChars(4)) != "RIFF")
            throw new InvalidDataException($"{path}: not a RIFF file");
        reader.ReadInt32();
        if (new string(reader.ReadChars(4)) != "WAVE")
            throw new InvalidDataException($"{path}: not a WAVE file");

        int format = 0, channels = 0, rate = 0, bits = 0;
        while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
        {
            string id = new(reader.ReadChars(4));
            int size = reader.ReadInt32();
            long next = reader.BaseStream.Position + size + (size & 1);
            if (id == "fmt ")
            {
                format = reader.ReadInt16();
                channels = reader.ReadInt16();
                rate = reader.ReadInt32();
                reader.ReadInt32();
                reader.ReadInt16();
                bits = reader.ReadInt16();
                if (format == -2) // WAVE_FORMAT_EXTENSIBLE (0xFFFE): the real format opens the sub-format GUID
                {
                    reader.ReadInt16();
                    reader.ReadInt16();
                    reader.ReadInt32();
                    format = reader.ReadInt16();
                }
            }
            else if (id == "data")
                return Decode(reader.ReadBytes(size), format, channels, rate, bits, path);
            reader.BaseStream.Position = Math.Min(next, reader.BaseStream.Length);
        }
        throw new InvalidDataException($"{path}: no data chunk");
    }

    private static Stereo Decode(byte[] data, int format, int channels, int rate, int bits, string path)
    {
        if (channels is < 1 or > 2)
            throw new InvalidDataException($"{path}: {channels} channels");
        int bytes = bits / 8, frames = data.Length / (bytes * channels);
        var left = new float[frames];
        var right = new float[frames];
        for (int f = 0; f < frames; f++)
            for (int c = 0; c < channels; c++)
            {
                int at = (f * channels + c) * bytes;
                float v = (format, bits) switch
                {
                    (1, 16) => BitConverter.ToInt16(data, at) / 32768f,
                    (1, 24) => ((data[at] | data[at + 1] << 8 | data[at + 2] << 16) << 8 >> 8) / 8388608f,
                    (1, 32) => BitConverter.ToInt32(data, at) / 2147483648f,
                    (3, 32) => BitConverter.ToSingle(data, at),
                    _ => throw new InvalidDataException($"{path}: format {format}, {bits} bits"),
                };
                if (c == 0)
                    left[f] = v;
                if (c == 1 || channels == 1)
                    right[f] = v;
            }
        return new Stereo(left, right, rate);
    }

    /// <summary>Writes 16-bit stereo PCM, clamping stray peaks.</summary>
    public static void Write(string path, Stereo audio)
    {
        using var writer = new BinaryWriter(File.Create(path));
        int dataSize = audio.Length * 4;
        writer.Write("RIFF"u8);
        writer.Write(36 + dataSize);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)2);
        writer.Write(audio.Rate);
        writer.Write(audio.Rate * 4);
        writer.Write((short)4);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataSize);
        for (int i = 0; i < audio.Length; i++)
        {
            writer.Write(ToShort(audio.Left[i]));
            writer.Write(ToShort(audio.Right[i]));
        }
    }

    private static short ToShort(float v) => (short)Math.Clamp(MathF.Round(v * 32767f), short.MinValue, short.MaxValue);
}

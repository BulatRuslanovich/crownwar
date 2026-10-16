using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lordsio.MusicStudio;

/// <summary>One recorded note of an instrument, as named by its file.</summary>
/// <param name="Midi">The note in the file name (scientific pitch: C4 = 60), before any per-instrument correction.</param>
/// <param name="Dynamic">Loudness rank as named (pp=0 … ff=5, or v1, v2 …).</param>
/// <param name="RoundRobin">Which take of the same note and dynamic (1, 2 …).</param>
public sealed record SampleFile(string Path, int Midi, int Dynamic, int RoundRobin);

/// <summary>
/// The VSCO 2 Community Edition samples by Versilian Studios (CC0, github.com/sgossner/VSCO-2-CE) that the
/// instruments are built from. They are downloaded into a local cache, pinned to one commit so a render is repeatable,
/// and never committed: only the rendered music is.
/// </summary>
public static partial class Vsco
{
    public const string Commit = "440300901dfe9275fd84e0b7763af1f8443ae62e";

    public const string UprightPiano = "Keys/Upright Nr1";
    public const string ViolinArco = "Strings/Solo Violin/Arco Vib";
    public const string ViolinPizz = "Strings/Solo Violin/Pizz";
    public const string BassPizz = "Strings/Solo Contrabass/Pizz";
    public const string BassArco = "Strings/Solo Contrabass/SusVib";
    public const string ViolinSpiccato = "Strings/Solo Violin/spic";
    public const string ViolinTremolo = "Strings/Solo Violin/Trem";
    public const string BassSpiccato = "Strings/Solo Contrabass/Spic";

    public static readonly string[] Folders =
        { UprightPiano, ViolinArco, ViolinPizz, BassPizz, BassArco, ViolinSpiccato, ViolinTremolo, BassSpiccato };

    /// <summary>Downloads every .wav of <see cref="Folders"/> that the cache does not have yet.</summary>
    public static async Task Fetch(string cache)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("lordsio-music-studio", "1.0"));
        string tree = await http.GetStringAsync($"https://api.github.com/repos/sgossner/VSCO-2-CE/git/trees/{Commit}?recursive=1");
        var paths = JsonDocument.Parse(tree).RootElement.GetProperty("tree").EnumerateArray()
            .Where(e => e.GetProperty("type").GetString() == "blob")
            .Select(e => e.GetProperty("path").GetString()!)
            .Where(p => p.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) && Folders.Any(f => p.StartsWith(f + "/", StringComparison.Ordinal)))
            .ToList();

        int fetched = 0;
        foreach (string path in paths)
        {
            string local = System.IO.Path.Combine(cache, path);
            if (File.Exists(local))
                continue;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(local)!);
            string url = $"https://raw.githubusercontent.com/sgossner/VSCO-2-CE/{Commit}/{string.Join('/', path.Split('/').Select(Uri.EscapeDataString))}";
            byte[] bytes = await http.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(local + ".part", bytes);
            File.Move(local + ".part", local);
            Console.WriteLine($"  {path} ({bytes.Length / 1024} KB)");
            fetched++;
        }
        Console.WriteLine($"{paths.Count} samples, {fetched} downloaded now, {paths.Count - fetched} were cached");
    }

    /// <summary>The samples of one folder of the cache, parsed from names like UR1_C4_mf_RR1.wav or BKCtbss_Pizz_A#0_v1_rr2.wav.</summary>
    public static List<SampleFile> List(string cache, string folder)
    {
        string dir = System.IO.Path.Combine(cache, folder);
        if (!Directory.Exists(dir))
            throw new DirectoryNotFoundException($"{dir} is missing: run `dotnet run -- fetch` first");
        var list = new List<SampleFile>();
        foreach (string file in Directory.GetFiles(dir, "*.wav"))
        {
            var m = SampleName().Match(System.IO.Path.GetFileName(file));
            if (!m.Success)
            {
                Console.WriteLine($"  skipped {file}: unexpected name");
                continue;
            }
            int rr = m.Groups["rr"].Success ? int.Parse(m.Groups["rr"].Value) : 1;
            list.Add(new SampleFile(file, Midi(m.Groups["note"].Value), Dynamic(m.Groups["dyn"].Value), rr));
        }
        return list;
    }

    /// <summary>Scientific pitch name (C4, A#0, Db3) to a MIDI note.</summary>
    public static int Midi(string name)
    {
        int pc = "C D EF G A B".IndexOf(char.ToUpperInvariant(name[0]));
        int i = 1;
        if (name[i] == '#') { pc++; i++; }
        else if (name[i] == 'b') { pc--; i++; }
        return 12 * (int.Parse(name[i..]) + 1) + pc;
    }

    private static int Dynamic(string name) => name.ToLowerInvariant() switch
    {
        "pp" => 0,
        "p" => 1,
        "mp" => 2,
        "mf" => 3,
        "f" => 4,
        "ff" => 5,
        var v => int.Parse(v[1..]),
    };

    [GeneratedRegex(@"_(?<note>[A-G][#b]?-?\d)_(?<dyn>pp|p|mp|mf|f|ff|v\d)(?:_rr?(?<rr>\d))?\.wav$", RegexOptions.IgnoreCase)]
    private static partial Regex SampleName();
}

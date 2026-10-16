using System.Globalization;
using Lordsio.MusicStudio;
using Lordsio.MusicStudio.Pieces;

// Dev-only music studio. Commands:
//   fetch           download the VSCO 2 CE samples into cache/
//   analyze         detect the pitch of every sample and compare it with the note in its name
//   check           play notes across each instrument's range and verify the pitch that comes out
//   levels <file.wav> <seconds>  RMS per chunk of that many seconds, and how smoothly the loop's end meets its start
//   render <piece|all> [dir]  render a piece (menu) to dir (default out/) as .wav and .ogg
string root = AppContext.BaseDirectory;
while (!File.Exists(Path.Combine(root, "MusicStudio.csproj")))
    root = Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar)) ?? throw new InvalidOperationException("MusicStudio.csproj not found");
string cache = Path.Combine(root, "cache");

switch (args.FirstOrDefault())
{
    case "fetch":
        await Vsco.Fetch(cache);
        break;
    case "analyze":
        Analyze(cache);
        break;
    case "check":
        return Check(cache) ? 0 : 1;
    case "probe": // probe <file.wav>: how periodic the sound is at each semitone (for suspicious samples)
    {
        var audio = Wav.Read(args[1]);
        foreach (double delay in new[] { 0.05, 0.15, 0.4 })
        {
            var dips = new List<(int Note, double Found, double Dip)>();
            for (int note = 24; note < 96; note++)
            {
                var v = Pitch.Verify(audio, note, delay);
                dips.Add((note, v.Midi, v.Dip));
            }
            Console.WriteLine($"delay {delay}: " + string.Join("  ", dips.OrderBy(d => d.Dip).Take(4)
                .Select(d => string.Create(CultureInfo.InvariantCulture, $"{d.Note}->{d.Found:F2} dip {d.Dip:F2}"))));
        }
        break;
    }
    case "levels":
    {
        var audio = Wav.Read(args[1]);
        double chunk = double.Parse(args[2], CultureInfo.InvariantCulture);
        var mix = new Mix(120, audio.Length / (double)audio.Rate);
        mix.Load(audio.Left, audio.Right);
        for (double t = 0; t < audio.Length / (double)audio.Rate - 0.01; t += chunk)
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {t,6:F1} s  {mix.Rms(t, t + chunk),6:F1} dBFS"));
        int n = audio.Length;
        float jump = Math.Max(Math.Abs(audio.Left[0] - audio.Left[n - 1]), Math.Abs(audio.Right[0] - audio.Right[n - 1]));
        float step = 0;
        for (int i = 1; i < n; i++)
            step = Math.Max(step, Math.Abs(audio.Left[i] - audio.Left[i - 1]));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  seam: last 50 ms {mix.Rms((n - audio.Rate / 20) / (double)audio.Rate):F1} dBFS, first 50 ms {mix.Rms(0, 0.05):F1} dBFS; jump across it {jump:F3} (largest step inside {step:F3})"));
        break;
    }
    case "render":
    {
        var pieces = new Dictionary<string, Func<string, Stereo>> { ["menu"] = Menu.Render };
        string which = args.ElementAtOrDefault(1) ?? "all";
        string dir = args.ElementAtOrDefault(2) ?? Path.Combine(root, "out");
        foreach (var (name, render) in pieces.Where(p => which == "all" || p.Key == which))
        {
            Console.WriteLine($"rendering {name}");
            Save(render(cache), dir, name);
        }
        break;
    }
    default:
        Console.WriteLine("usage: dotnet run -- fetch | analyze | check | render <piece|all> [dir]");
        return 1;
}
return 0;

static void Save(Stereo audio, string dir, string name)
{
    Directory.CreateDirectory(dir);
    string wav = Path.Combine(dir, name + ".wav"), ogg = Path.Combine(dir, name + ".ogg");
    Wav.Write(wav, audio);
    Ogg.Encode(wav, ogg);
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"{wav} ({new FileInfo(wav).Length / 1024} KB), {ogg} ({new FileInfo(ogg).Length / 1024} KB), {audio.Length / (double)audio.Rate:F1} s"));
}

static bool Check(string cache)
{
    var instruments = new (Sampler Sampler, int Low, int High)[]
    {
        // The ranges the pieces use: above B2 the contrabass pizzicato recordings are too noisy to rely on.
        (new Sampler("piano", cache, Vsco.UprightPiano), 33, 88),
        (new Sampler("violin", cache, Vsco.ViolinArco), 55, 91),
        (new Sampler("bass", cache, Vsco.BassPizz, octaveShift: 12), 28, 47),
        (new Sampler("bass arco", cache, Vsco.BassArco, octaveShift: 12), 28, 47),
    };
    bool ok = true;
    foreach (var (sampler, low, high) in instruments)
    {
        double worst = 0;
        int worstNote = 0;
        for (int midi = low; midi <= high; midi++)
            foreach (float velocity in new[] { 0.3f, 0.9f })
            {
                var mix = new Mix(120, 2.5);
                sampler.Play(mix, 0, 2, midi, velocity);
                var (found, dip, octaveUp) = Pitch.Clearest(mix.ToStereo(), midi);
                double error = Math.Abs(found - midi);
                // Clearly periodic at the note, and less so at half its period (else it sounds an octave up).
                bool clear = dip < 0.35 && octaveUp > dip + 0.05;
                if (!(error < 0.3) || !clear)
                    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                        $"    {sampler.Name} note {midi} velocity {velocity}: off by {error:F2}, dip {dip:F2}, octave-up dip {octaveUp:F2}"));
                if (!clear)
                    error = 99;
                if (double.IsNaN(error) || error > worst)
                    (worst, worstNote) = (double.IsNaN(error) ? 99 : error, midi);
            }
        bool pass = worst < 0.3;
        ok &= pass;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{sampler.Name}: notes {low}-{high}, worst pitch error {worst:F2} semitones (note {worstNote}) {(pass ? "OK" : "FAIL")}"));
    }
    return ok;
}

static void Analyze(string cache)
{
    foreach (string folder in Vsco.Folders)
    {
        Console.WriteLine($"== {folder}");
        var diffs = new List<double>();
        foreach (var file in Vsco.List(cache, folder).OrderBy(f => f.Midi).ThenBy(f => f.Dynamic).ThenBy(f => f.RoundRobin))
        {
            var audio = Wav.Read(file.Path);
            double detected = Pitch.DetectMidi(audio);
            double diff = detected - file.Midi;
            if (!double.IsNaN(diff))
                diffs.Add(diff);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  {Path.GetFileName(file.Path),-34} named {file.Midi,3}  detected {detected,7:F2}  diff {diff,7:F2}  {audio.Length / (double)audio.Rate,5:F1}s @{audio.Rate}  onset {Pitch.Onset(audio) * 1000.0 / audio.Rate,4:F0}ms"));
        }
        diffs.Sort();
        if (diffs.Count > 0)
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  median diff {diffs[diffs.Count / 2]:F2} semitones over {diffs.Count} samples"));
    }
}

using System.Diagnostics;

namespace Lordsio.MusicStudio;

/// <summary>Encodes a .wav to Ogg Vorbis with ffmpeg (from PATH, or where winget puts it).</summary>
public static class Ogg
{
    public static void Encode(string wav, string ogg, int quality = 5)
    {
        var info = new ProcessStartInfo(FindFfmpeg(), $"-y -loglevel error -i \"{wav}\" -c:a libvorbis -q:a {quality} \"{ogg}\"")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
        };
        using var process = Process.Start(info)!;
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"ffmpeg failed: {error}");
    }

    private static string FindFfmpeg()
    {
        string exe = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).ToList();
        if (OperatingSystem.IsWindows()) // a fresh install is on the user's PATH before any open shell sees it
            dirs.AddRange((Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "").Split(Path.PathSeparator));
        dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Links"));
        foreach (string dir in dirs.Where(d => d.Length > 0))
        {
            string path = Path.Combine(dir, exe);
            if (File.Exists(path))
                return path;
        }
        throw new FileNotFoundException("ffmpeg not found: install it (winget install Gyan.FFmpeg) or put it on PATH");
    }
}

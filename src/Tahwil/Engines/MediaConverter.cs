using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Tahwil.Core;

namespace Tahwil.Engines;

/// <summary>Audio and video through the bundled ffmpeg.exe.</summary>
public sealed partial class MediaConverter : IConverter
{
    public async Task ConvertAsync(string source, string dest, Target target, ConversionOptions o,
        IProgress<double> progress, CancellationToken ct)
    {
        var args = new List<string> { "-hide_banner", "-nostdin", "-y", "-i", source };
        args.AddRange(OutputArgs(target, o));
        args.AddRange(["-progress", "pipe:1", "-nostats", dest]);

        var psi = new ProcessStartInfo(AppPaths.Ffmpeg)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("ffmpeg did not start");
        double duration = 0;
        var errTail = new Queue<string>();

        var errTask = Task.Run(async () =>
        {
            while (await p.StandardError.ReadLineAsync(ct) is { } line)
            {
                if (duration == 0 && DurationRx().Match(line) is { Success: true } m)
                    duration = TimeSpan.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture).TotalSeconds;
                errTail.Enqueue(line); if (errTail.Count > 8) errTail.Dequeue();
            }
        }, ct);

        var outTask = Task.Run(async () =>
        {
            while (await p.StandardOutput.ReadLineAsync(ct) is { } line)
            {
                if (line.StartsWith("out_time_us=") && duration > 0 &&
                    long.TryParse(line.AsSpan(12), out var us))
                    progress.Report(us / 1e6 / duration);
            }
        }, ct);

        try { await p.WaitForExitAsync(ct); }
        catch (OperationCanceledException) { try { p.Kill(true); } catch { } TryDelete(dest); throw; }
        await Task.WhenAll(errTask, outTask);

        if (p.ExitCode != 0)
        {
            TryDelete(dest);
            throw new InvalidOperationException(string.Join("\n", errTail));
        }
        progress.Report(1);
    }

    /// <summary>Codec settings per output format. Quality 0..100 maps onto each codec's scale.</summary>
    static IEnumerable<string> OutputArgs(Target t, ConversionOptions o)
    {
        double q = (o.Quality ?? 70) / 100.0;
        int abr = o.AudioBitrateK ?? 192;
        string Crf() => ((int)Math.Round(32 - q * 14)).ToString(CultureInfo.InvariantCulture); // 32..18
        IEnumerable<string> Scale() => o.VideoHeight is int h ? ["-vf", $"scale=-2:'min({h},ih)'"] : [];

        if (t.AudioOnly || t.Ext is "mp3" or "wav" or "aac" or "flac" or "ogg")
        {
            IEnumerable<string> a = t.Ext switch
            {
                "mp3" => ["-c:a", "libmp3lame", "-b:a", $"{abr}k"],
                "wav" => ["-c:a", "pcm_s16le"],
                "aac" => ["-c:a", "aac", "-b:a", $"{abr}k", "-f", "adts"],
                "flac" => ["-c:a", "flac"],
                "ogg" => ["-c:a", "libvorbis", "-q:a", ((int)Math.Round(2 + q * 6)).ToString(CultureInfo.InvariantCulture)],
                _ => [],
            };
            return ["-vn", "-map_metadata", "0", .. a];
        }

        return t.Ext switch
        {
            "mp4" or "mov" => [.. Scale(), "-c:v", "libx264", "-preset", "medium", "-crf", Crf(), "-pix_fmt", "yuv420p",
                               "-c:a", "aac", "-b:a", $"{abr}k", "-movflags", "+faststart"],
            "mkv" => [.. Scale(), "-c:v", "libx264", "-preset", "medium", "-crf", Crf(), "-pix_fmt", "yuv420p",
                      "-c:a", "aac", "-b:a", $"{abr}k"],
            "avi" => [.. Scale(), "-c:v", "mpeg4", "-vtag", "xvid", "-q:v", ((int)Math.Round(10 - q * 8)).ToString(CultureInfo.InvariantCulture),
                      "-c:a", "libmp3lame", "-b:a", $"{abr}k"],
            "gif" => ["-vf", GifFilter(o), "-loop", "0"],
            _ => throw new NotSupportedException(t.Ext),
        };
    }

    static string GifFilter(ConversionOptions o)
    {
        int w = o.Width ?? (o.VideoHeight is int h ? h * 16 / 9 : 480);
        int fps = o.Quality is int q ? Math.Clamp(6 + q / 8, 6, 18) : 12;
        return $"fps={fps},scale='min({w},iw)':-2:flags=lanczos,split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=sierra2_4a";
    }

    static void TryDelete(string f) { try { File.Delete(f); } catch { } }

    [GeneratedRegex(@"Duration:\s*(\d+:\d+:\d+(?:\.\d+)?)")]
    private static partial Regex DurationRx();
}

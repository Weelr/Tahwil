using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;

namespace Tahwil.Core;

/// <summary>
/// Checks GitHub Releases at most once a day, downloads a newer setup in the background,
/// and runs it silently after the app closes.
/// </summary>
public static class Updater
{
    public const string Repo = "Weelr/Tahwil";
    const string AssetName = "Tahwil-Setup.exe";

    public static Version Current { get; } = Assembly.GetExecutingAssembly().GetName().Version ?? new(0, 0, 0);
    public static string CurrentText => $"{Current.Major}.{Current.Minor}.{Current.Build}";

    sealed record Release([property: JsonPropertyName("tag_name")] string Tag, [property: JsonPropertyName("assets")] Asset[] Assets);
    sealed record Asset([property: JsonPropertyName("name")] string Name, [property: JsonPropertyName("browser_download_url")] string Url);

    public enum Result { UpToDate, Downloaded, Failed }

    public static async Task<(Result, string?)> CheckAsync(bool force)
    {
        var s = Settings.Load();
        if (!force && DateTime.UtcNow - s.LastUpdateCheck < TimeSpan.FromHours(20)) return (Result.UpToDate, null);
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"Tahwil/{CurrentText}");
            var rel = await http.GetFromJsonAsync<Release>($"https://api.github.com/repos/{Repo}/releases/latest");
            s.LastUpdateCheck = DateTime.UtcNow;
            s.Save();
            if (rel is null || !Version.TryParse(rel.Tag.TrimStart('v'), out var latest) || latest <= Current)
                return (Result.UpToDate, null);

            var asset = rel.Assets.FirstOrDefault(a => a.Name == AssetName);
            if (asset is null) return (Result.UpToDate, null);

            var path = Path.Combine(AppPaths.DataDir, "update", $"Tahwil-Setup-{latest}.exe");
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var tmp = path + ".part";
                await using (var src = await http.GetStreamAsync(asset.Url))
                await using (var dst = File.Create(tmp))
                    await src.CopyToAsync(dst);
                File.Move(tmp, path, true);
            }
            s.PendingInstaller = path;
            s.PendingVersion = latest.ToString(3);
            s.Save();
            return (Result.Downloaded, s.PendingVersion);
        }
        catch (Exception e) { Log.Error("update", e); return (Result.Failed, null); }
    }

    /// <summary>Called on exit: starts the downloaded setup silently (it closes nothing of ours that's still running).</summary>
    public static void ApplyPendingOnExit()
    {
        var s = Settings.Load();
        if (s.PendingInstaller is not { } path) return;
        s.PendingInstaller = null; s.PendingVersion = null; s.Save();
        if (!File.Exists(path)) return;
        try
        {
            Process.Start(new ProcessStartInfo(path, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS")
            { UseShellExecute = true });
        }
        catch (Exception e) { Log.Error("apply update", e); }
    }

    /// <summary>Removes installers left over from earlier updates.</summary>
    public static void CleanOld()
    {
        try
        {
            var dir = Path.Combine(AppPaths.DataDir, "update");
            if (!Directory.Exists(dir)) return;
            var keep = Settings.Load().PendingInstaller;
            foreach (var f in Directory.GetFiles(dir))
                if (!string.Equals(f, keep, StringComparison.OrdinalIgnoreCase)) try { File.Delete(f); } catch { }
        }
        catch { }
    }
}

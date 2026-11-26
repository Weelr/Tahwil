using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Tahwil.Core;

/// <summary>Settings from the Custom dialog. Null means "use the automatic default".</summary>
public sealed class ConversionOptions
{
    /// <summary>0 = smallest file, 100 = best quality.</summary>
    public int? Quality { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public bool KeepRatio { get; set; } = true;
    /// <summary>Video output height (1080, 720, 480…).</summary>
    public int? VideoHeight { get; set; }
    public int? AudioBitrateK { get; set; }
    public string? OutputFolder { get; set; }

    public static ConversionOptions Default => new();
}

public enum JobState { Waiting, Running, Done, Failed, Cancelled }

/// <summary>One file being converted; the progress window binds to it.</summary>
public sealed class ConvertJob : INotifyPropertyChanged
{
    public ConvertJob(string source, Target target, CategoryInfo category)
    {
        Source = source; Target = target; Category = category;
        try { SourceSize = new FileInfo(source).Length; } catch { }
    }

    public string Source { get; }
    public Target Target { get; }
    public CategoryInfo Category { get; }
    public long SourceSize { get; }
    public string FileName => Path.GetFileName(Source);
    public string SourceExt => Formats.ExtOf(Source).ToUpperInvariant();

    JobState _state;
    public JobState State { get => _state; set { _state = value; Raise(); Raise(nameof(StatusText)); } }

    double _progress;
    public double Progress { get => _progress; set { _progress = Math.Clamp(value, 0, 1); Raise(); Raise(nameof(StatusText)); } }

    public string? Output { get; set; }

    string? _error;
    public string? Error { get => _error; set { _error = value; Raise(); } }

    public string StatusText => State switch
    {
        JobState.Waiting => Loc.Waiting,
        JobState.Running => Loc.Percent(Progress),
        JobState.Done => "✓ " + Loc.Done,
        JobState.Failed => Loc.Failed,
        _ => Loc.Cancelled,
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    void Raise([CallerMemberName] string? n = null) =>
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => PropertyChanged?.Invoke(this, new(n)));
}

public interface IConverter
{
    Task ConvertAsync(string source, string dest, Target target, ConversionOptions options,
        IProgress<double> progress, CancellationToken ct);
}

public static class AppPaths
{
    public static string BaseDir => AppContext.BaseDirectory;
    public static string Ffmpeg => Path.Combine(BaseDir, "engines", "ffmpeg.exe");
    public static string Soffice => Path.Combine(BaseDir, "engines", "libreoffice", "program", "soffice.exe");
    public static string DataDir { get; } = Directory.CreateDirectory(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tahwil")).FullName;
    public static string Exe => Environment.ProcessPath ?? Path.Combine(BaseDir, "Tahwil.exe");

    /// <summary>"photo.jpg" next to "photo.heic", or "photo (2).jpg" if taken.</summary>
    public static string OutputPathFor(string source, string ext, string? folder)
    {
        var dir = folder ?? Path.GetDirectoryName(source)!;
        var name = Path.GetFileNameWithoutExtension(source);
        var path = Path.Combine(dir, $"{name}.{ext}");
        for (int i = 2; File.Exists(path) || string.Equals(path, source, StringComparison.OrdinalIgnoreCase); i++)
            path = Path.Combine(dir, $"{name} ({i}).{ext}");
        return path;
    }

    public static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "Tahwil", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(d);
        return d;
    }
}

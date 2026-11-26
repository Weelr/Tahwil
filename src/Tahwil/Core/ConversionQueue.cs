using System.Collections.ObjectModel;
using Tahwil.Engines;

namespace Tahwil.Core;

/// <summary>Runs jobs one after another (images two at a time) and tracks overall progress.</summary>
public sealed class ConversionQueue
{
    public ObservableCollection<ConvertJob> Jobs { get; } = [];
    public event Action? Changed;
    public event Action? Finished;

    readonly CancellationTokenSource _cts = new();
    readonly List<(ConvertJob Job, ConversionOptions Options)> _pending = [];
    readonly DateTime _started = DateTime.UtcNow;
    bool _running;

    public bool IsRunning => _running;
    public int DoneCount => Jobs.Count(j => j.State is JobState.Done or JobState.Failed or JobState.Cancelled);
    public int FailedCount => Jobs.Count(j => j.State == JobState.Failed);
    public double Overall => Jobs.Count == 0 ? 0 : Jobs.Sum(j => j.State == JobState.Running ? j.Progress : j.State == JobState.Waiting ? 0 : 1) / Jobs.Count;
    public ConvertJob? Current => Jobs.FirstOrDefault(j => j.State == JobState.Running) ?? Jobs.LastOrDefault(j => j.State != JobState.Waiting) ?? Jobs.FirstOrDefault();

    public TimeSpan? Remaining
    {
        get
        {
            var o = Overall; var el = DateTime.UtcNow - _started;
            return o < 0.03 || el.TotalSeconds < 1.5 ? null : TimeSpan.FromSeconds(el.TotalSeconds * (1 - o) / o);
        }
    }

    public void Add(ConversionRequest req, ConversionOptions options)
    {
        foreach (var f in req.Files.Where(File.Exists))
        {
            var cat = Formats.CategoryOf(f);
            var target = cat?.Targets.FirstOrDefault(t => t.Ext == req.To && t.AudioOnly == req.AudioOnly)
                         ?? cat?.Targets.FirstOrDefault(t => t.Ext == req.To);
            if (cat is null || target is null) continue;
            var job = new ConvertJob(f, target, cat);
            Jobs.Add(job);
            lock (_pending) _pending.Add((job, options));
        }
        Changed?.Invoke();
        if (!_running) _ = RunAsync();
    }

    public void Cancel() => _cts.Cancel();

    async Task RunAsync()
    {
        _running = true;
        while (true)
        {
            List<(ConvertJob, ConversionOptions)> batch;
            lock (_pending)
            {
                if (_pending.Count == 0) break;
                int n = _pending[0].Job.Category.Id == Category.Image ? Math.Min(2, _pending.Count) : 1;
                batch = _pending.Take(n).ToList();
                _pending.RemoveRange(0, n);
            }
            await Task.WhenAll(batch.Select(b => RunOne(b.Item1, b.Item2)));
        }
        _running = false;
        Changed?.Invoke();
        Finished?.Invoke();
    }

    async Task RunOne(ConvertJob job, ConversionOptions o)
    {
        if (_cts.IsCancellationRequested) { job.State = JobState.Cancelled; return; }
        job.State = JobState.Running;
        Changed?.Invoke();
        string? dest = null;
        try
        {
            dest = AppPaths.OutputPathFor(job.Source, job.Target.Ext, o.OutputFolder);
            var progress = new Progress<double>(p => { job.Progress = p; Changed?.Invoke(); });
            await ConverterFor(job.Category).ConvertAsync(job.Source, dest, job.Target, o, progress, _cts.Token);
            job.Output = dest;
            job.Progress = 1;
            job.State = JobState.Done;
        }
        catch (OperationCanceledException) { job.State = JobState.Cancelled; TryDelete(dest); }
        catch (Exception e) { job.Error = e.Message; job.State = JobState.Failed; TryDelete(dest); Log.Error(job.Source, e); }
        Changed?.Invoke();
    }

    static IConverter ConverterFor(CategoryInfo c) => c.Engine switch
    {
        Engine.Image => new ImageConverter(),
        Engine.Media => new MediaConverter(),
        _ => new DocumentConverter(c.Id),
    };

    static void TryDelete(string? f) { if (f is not null) try { File.Delete(f); } catch { } }
}

/// <summary>What one right-click asked for.</summary>
public sealed record ConversionRequest(string To, bool AudioOnly, bool Custom, List<string> Files)
{
    public string Key => $"{To}|{AudioOnly}|{Custom}";
    /// <summary>True when the Windows 11 menu passed all files at once in a list file.</summary>
    public bool FromList { get; init; }
}

public static class Log
{
    static readonly string File_ = Path.Combine(AppPaths.DataDir, "log.txt");
    public static void Error(string context, Exception e)
    {
        try
        {
            var fi = new FileInfo(File_);
            if (fi.Exists && fi.Length > 512 * 1024) fi.Delete();
            File.AppendAllText(File_, $"[{DateTime.Now:s}] {context}\n{e}\n\n");
        }
        catch { }
    }
    public static void Info(string msg) { try { File.AppendAllText(File_, $"[{DateTime.Now:s}] {msg}\n"); } catch { } }
}

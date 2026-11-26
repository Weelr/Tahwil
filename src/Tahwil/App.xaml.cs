using System.Windows;
using System.Windows.Threading;
using Tahwil.Core;
using Tahwil.UI;

namespace Tahwil;

/// <summary>
/// Entry point. Command lines:
///   convert --to EXT [--audio] [--custom] [--list FILE] [files…]   (from the right-click menu)
///   --register / --unregister                                       (from the installer)
///   --export-menu PATH                                              (build: table for the shell DLL)
///   (no arguments)                                                  (Start menu: about + updates)
/// </summary>
public partial class App : Application
{
    SingleInstance? _instance;
    ConversionQueue? _queue;
    ProgressWindow? _window;
    readonly List<ConversionRequest> _inbox = [];
    readonly DispatcherTimer _debounce = new();
    bool _busyWithDialog;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, ex) => { Log.Error("ui", ex.Exception); ex.Handled = true; };
        var args = e.Args;
        try
        {
            switch (args.FirstOrDefault())
            {
                case "--register": Shutdown(ShellRegistration.Register()); return;
                case "--unregister": Shutdown(ShellRegistration.Unregister()); return;
                case "--export-menu": File.WriteAllText(args[1], Formats.ExportMenuTable()); Shutdown(0); return;
                case "convert": StartConvert(Cli.Parse(args.Skip(1).ToArray())); return;
                default:
                    Theme.Apply(Resources);
                    var about = new AboutWindow();
                    about.Closed += (_, _) => Quit();
                    about.Show();
                    return;
            }
        }
        catch (Exception ex) { Log.Error("startup " + string.Join(' ', args), ex); Shutdown(1); }
    }

    void StartConvert(ConversionRequest req)
    {
        _instance = new SingleInstance();
        if (!_instance.IsPrimary && SingleInstance.Send(req)) { Shutdown(0); return; }

        Theme.Apply(Resources);
        _instance.Received += r => Dispatcher.BeginInvoke(() => Enqueue(r, 400));
        _instance.Listen();
        _debounce.Tick += (_, _) => { _debounce.Stop(); ProcessInbox(); };
        // The classic menu starts one process per file, so give the others a moment to check in.
        Enqueue(req, req.FromList ? 50 : 800);

        Updater.CleanOld();
        _ = Updater.CheckAsync(force: false);
    }

    void Enqueue(ConversionRequest r, int waitMs)
    {
        _inbox.Add(r);
        _debounce.Stop();
        _debounce.Interval = TimeSpan.FromMilliseconds(waitMs);
        _debounce.Start();
    }

    void ProcessInbox()
    {
        if (_busyWithDialog) { _debounce.Start(); return; }
        var groups = _inbox.GroupBy(r => r.Key)
            .Select(g => g.First() with { Files = g.SelectMany(r => r.Files).Distinct(StringComparer.OrdinalIgnoreCase).ToList() })
            .ToList();
        _inbox.Clear();

        foreach (var req in groups)
        {
            var files = req.Files.Where(f => File.Exists(f) && Formats.CategoryOf(f) is not null).ToList();
            if (files.Count == 0) continue;

            var request = req with { Files = files };
            var options = ConversionOptions.Default;
            if (req.Custom)
            {
                _busyWithDialog = true;
                var dlg = new CustomWindow(files, req.To, req.AudioOnly);
                var ok = dlg.ShowDialog() == true && dlg.Chosen is not null;
                _busyWithDialog = false;
                if (!ok) continue;
                request = request with { To = dlg.Chosen!.Ext, AudioOnly = dlg.Chosen.AudioOnly };
                options = dlg.Options;
            }
            EnsureWindow().Add(request, options);
        }

        if (_window is null && _inbox.Count == 0) Quit();
    }

    ConversionQueue EnsureWindow()
    {
        if (_queue is not null) return _queue;
        _queue = new ConversionQueue();
        _window = new ProgressWindow(_queue);
        _window.Closed += (_, _) => Quit();
        _window.Show();
        return _queue;
    }

    async void Quit()
    {
        if (_queue is { IsRunning: true })
        {
            _queue.Cancel();
            for (int i = 0; i < 20 && _queue.IsRunning; i++) await Task.Delay(100);
        }
        _instance?.Dispose();
        Updater.ApplyPendingOnExit();
        Shutdown(0);
    }
}

static class Cli
{
    public static ConversionRequest Parse(string[] a)
    {
        string to = ""; bool audio = false, custom = false, fromList = false;
        var files = new List<string>();
        for (int i = 0; i < a.Length; i++)
        {
            switch (a[i])
            {
                case "--to" when i + 1 < a.Length: to = a[++i].ToLowerInvariant(); break;
                case "--audio": audio = true; break;
                case "--custom": custom = true; break;
                case "--list" when i + 1 < a.Length:
                    var list = a[++i];
                    fromList = true;
                    try { files.AddRange(File.ReadAllLines(list).Where(l => l.Length > 0)); File.Delete(list); }
                    catch (Exception e) { Log.Error("list", e); }
                    break;
                default: files.Add(a[i]); break;
            }
        }
        return new ConversionRequest(to, audio, custom, files) { FromList = fromList };
    }
}

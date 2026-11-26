using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Tahwil.Core;

namespace Tahwil.UI;

/// <summary>The small corner window: one bar for the whole batch, details on demand.</summary>
public partial class ProgressWindow : Window
{
    readonly ConversionQueue _queue;
    readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMilliseconds(120) };
    readonly DispatcherTimer _autoClose = new() { Interval = TimeSpan.FromSeconds(8) };
    bool _dirty;

    public ProgressWindow(ConversionQueue queue)
    {
        InitializeComponent();
        Theme.Decorate(this);
        _queue = queue;
        JobList.ItemsSource = queue.Jobs;
        DetailsText.Text = Loc.ShowDetails;
        CloseBtn.Content = Loc.Close;
        OpenBtn.Content = Loc.OpenFolder;

        queue.Changed += () => _dirty = true;
        queue.Finished += () => Dispatcher.BeginInvoke(OnFinished);
        _refresh.Tick += (_, _) => { if (_dirty) { _dirty = false; Render(); } };
        _refresh.Start();
        _autoClose.Tick += (_, _) => { if (!IsMouseOver && Details.Visibility != Visibility.Visible) Close(); };

        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) try { DragMove(); } catch { } };
        SizeChanged += (_, _) => Place();
        Loaded += (_, _) => { Place(); Render(); };
    }

    void Place()
    {
        var wa = SystemParameters.WorkArea;
        Top = wa.Bottom - ActualHeight - 16;
        Left = Loc.IsArabic ? wa.Left + 16 : wa.Right - ActualWidth - 16;
    }

    void Render()
    {
        var jobs = _queue.Jobs;
        var cur = _queue.Current;
        Overall.Value = _queue.Overall;
        CountText.Text = Loc.FilesProgress(Math.Min(_queue.DoneCount + (_queue.IsRunning ? 1 : 0), jobs.Count), jobs.Count);
        if (_queue.IsRunning && cur is not null)
        {
            // More files can arrive after a batch finished; go back to the running look.
            _autoClose.Stop();
            DoneActions.Visibility = Visibility.Collapsed;
            Overall.Foreground = (Brush)FindResource("BarFill");
            SetBadge(cur.Category.Id, cur.SourceExt);
            TitleText.Text = Loc.Converting(cur.FileName);
            var parts = new List<string> { Loc.ToFormat(cur.Target.Ext.ToUpperInvariant()), Loc.Percent(_queue.Overall) };
            if (_queue.Remaining is { } r) parts.Add(Loc.Remaining(r));
            SubText.Text = string.Join(" · ", parts);
        }
    }

    void SetBadge(Category c, string ext)
    {
        Badge.Background = Theme.BadgeFor(c);
        BadgeText.Text = ext;
        BadgeText.Visibility = Visibility.Visible;
        BadgeIcon.Visibility = Visibility.Collapsed;
    }

    void OnFinished()
    {
        Render();
        int ok = _queue.Jobs.Count(j => j.State == JobState.Done), bad = _queue.FailedCount;
        Overall.Value = 1;
        BadgeText.Visibility = Visibility.Collapsed;
        BadgeIcon.Visibility = Visibility.Visible;
        CountText.Text = Loc.FilesProgress(_queue.Jobs.Count, _queue.Jobs.Count);
        if (bad == 0)
        {
            Badge.Background = (Brush)FindResource("Ok");
            BadgeIcon.Text = ""; // check
            TitleText.Text = ok == 1 ? Loc.OneDone : Loc.AllDone(ok);
            var exts = _queue.Jobs.Select(j => j.Target.Ext.ToUpperInvariant()).Distinct().ToList();
            SubText.Text = _queue.Jobs.Count == 1 ? Path.GetFileName(_queue.Jobs[0].Output) : Loc.ToFormat(string.Join(", ", exts));
            Overall.Foreground = (Brush)FindResource("Ok");
            _autoClose.Start();
        }
        else
        {
            Badge.Background = (Brush)FindResource("Bad");
            BadgeIcon.Text = ""; // error
            TitleText.Text = Loc.SomeFailed(ok, bad);
            SubText.Text = _queue.Jobs.First(j => j.State == JobState.Failed).Error?.Split('\n').LastOrDefault(l => l.Trim().Length > 0) ?? "";
            Overall.Foreground = (Brush)FindResource("Bad");
            ShowDetails(true);
        }
        OpenBtn.Visibility = ok > 0 ? Visibility.Visible : Visibility.Collapsed;
        DoneActions.Visibility = Visibility.Visible;
    }

    void OnToggleDetails(object s, RoutedEventArgs e) => ShowDetails(Details.Visibility != Visibility.Visible);

    void ShowDetails(bool show)
    {
        Details.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        DetailsText.Text = show ? Loc.HideDetails : Loc.ShowDetails;
        Chevron.Text = show ? "" : "";
    }

    void OnClose(object s, RoutedEventArgs e)
    {
        if (_queue.IsRunning) _queue.Cancel();
        Close();
    }

    void OnOpenFolder(object s, RoutedEventArgs e)
    {
        var first = _queue.Jobs.FirstOrDefault(j => j.Output is not null)?.Output;
        if (first is null) return;
        Process.Start("explorer.exe", $"/select,\"{first}\"");
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _refresh.Stop(); _autoClose.Stop();
        base.OnClosed(e);
    }
}

/// <summary>Height → fully rounded corners (WPF draws odd shapes if the radius exceeds half the height).</summary>
public sealed class Pill : IValueConverter
{
    public static readonly Pill Instance = new();
    public object Convert(object v, Type t, object p, CultureInfo c) => new CornerRadius((double)v / 2);
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class CategoryBrush : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c) => Theme.BadgeFor((Category)v);
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class StateBrush : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c)
    {
        var app = Application.Current;
        return (JobState)v switch
        {
            JobState.Done => app.FindResource("Ok"),
            JobState.Failed or JobState.Cancelled => app.FindResource("Bad"),
            _ => p is "text" ? app.FindResource("Sub") : app.FindResource("BarFill"),
        };
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

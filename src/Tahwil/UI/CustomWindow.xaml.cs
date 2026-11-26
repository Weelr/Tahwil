using System.Windows;
using System.Windows.Controls;
using ImageMagick;
using Tahwil.Core;
using Tahwil.Engines;

namespace Tahwil.UI;

/// <summary>"Custom…": pick the format, quality, size and where to save.</summary>
public partial class CustomWindow : Window
{
    readonly IReadOnlyList<string> _files;
    readonly CategoryInfo _cat;
    readonly IReadOnlyList<Target> _targets;
    readonly double? _ratio; // width / height of the first image
    bool _syncing;
    string? _folder;

    public Target? Chosen { get; private set; }
    public ConversionOptions Options { get; } = new();

    public CustomWindow(IReadOnlyList<string> files, string? preferredExt, bool audioOnly)
    {
        InitializeComponent();
        Theme.Decorate(this);
        _files = files;
        _cat = Formats.CategoryOf(files[0])!;
        _targets = Formats.TargetsFor(files);

        WinTitle.Text = Loc.CustomTitle;
        Heading.Text = Loc.Selected(files.Count, Path.GetFileName(files[0]));
        SizeText.Text = Loc.CurrentSize(Loc.Size(files.Sum(f => new FileInfo(f).Length)));
        FormatLabel.Text = Loc.Format;
        QualityLabel.Text = Loc.Quality;
        BestText.Text = Loc.BestQuality; BalancedText.Text = Loc.Balanced; SmallText.Text = Loc.Smallest;
        DimLabel.Text = Loc.Dimensions; WLabel.Text = Loc.Width; HLabel.Text = Loc.Height;
        KeepRatio.Content = Loc.KeepRatio;
        ResLabel.Text = Loc.Resolution;
        BitrateLabel.Text = Loc.AudioBitrate;
        NoOptionsText.Text = Loc.NoOptions;
        EngineText.Text = DocumentConverter.HasOffice(_cat.Id) ? Loc.EngineOffice : Loc.EngineLibre;
        SaveLabel.Text = Loc.SaveLocation;
        SaveNext.Content = Loc.NextToOriginal; SaveOther.Content = Loc.OtherFolder;
        Remember.Content = Loc.RememberSettings;
        CancelBtn.Content = Loc.Cancel; OkBtn.Content = Loc.Convert;

        // format chips
        foreach (var t in _targets)
        {
            var chip = new RadioButton
            {
                Style = (Style)FindResource("Chip"), GroupName = "fmt", Tag = t,
                Content = t.AudioOnly ? $"♪ {t.Ext.ToUpperInvariant()}" : t.Ext.ToUpperInvariant(),
                ToolTip = t.Label,
            };
            chip.Checked += (_, _) => { Chosen = t; UpdateSections(); };
            Chips.Children.Add(chip);
        }

        AddChips(ResChips, "res", [(Loc.Original, null), ("1080p", 1080), ("720p", 720), ("480p", 480)], v => Options.VideoHeight = v, 0);
        AddChips(BitrateChips, "abr", [("128k", 128), ("192k", 192), ("256k", 256), ("320k", 320)], v => Options.AudioBitrateK = v, 1);

        if (_cat.Id == Category.Image && files.Count == 1)
        {
            try { var info = new MagickImageInfo(files[0]); WBox.Text = info.Width.ToString(); HBox.Text = info.Height.ToString(); _ratio = (double)info.Width / info.Height; }
            catch { }
        }

        RestoreRemembered();
        var pick = Chips.Children.OfType<RadioButton>().FirstOrDefault(c => ((Target)c.Tag).Ext == preferredExt && ((Target)c.Tag).AudioOnly == audioOnly)
                   ?? Chips.Children.OfType<RadioButton>().FirstOrDefault();
        if (pick is not null) pick.IsChecked = true;
        UpdateSections();    }

    static void AddChips(Panel panel, string group, (string Label, int? Value)[] items, Action<int?> set, int defaultIndex)
    {
        foreach (var (label, value) in items)
        {
            var c = new RadioButton { Style = (Style)Application.Current.FindResource("Chip"), GroupName = group, Content = label, Tag = value };
            c.Checked += (_, _) => set(value);
            panel.Children.Add(c);
        }
        ((RadioButton)panel.Children[defaultIndex]).IsChecked = true;
    }

    void RestoreRemembered()
    {
        if (!Settings.Load().Remembered.TryGetValue(_cat.Key, out var r)) return;
        if (r.Quality is int q) QualitySlider.Value = q;
        Select(ResChips, r.VideoHeight);
        Select(BitrateChips, r.AudioBitrateK);
        KeepRatio.IsChecked = r.KeepRatio;
    }

    static void Select(Panel p, int? v)
    {
        foreach (var c in p.Children.OfType<RadioButton>()) if ((int?)c.Tag == v) c.IsChecked = true;
    }

    void UpdateSections()
    {
        var ext = Chosen?.Ext;
        bool video = _cat.Id == Category.Video && Chosen is { AudioOnly: false };
        bool audioOut = ext is "mp3" or "aac" or "ogg" or "wav" or "flac" && (Chosen?.AudioOnly == true || _cat.Id == Category.Audio);
        bool lossyImage = _cat.Id == Category.Image && ext is "jpg" or "webp" or "heic";

        QualityPanel.Visibility = Show(lossyImage || video || ext == "ogg");
        SizePanel.Visibility = Show(_cat.Id == Category.Image && ext != "ico");
        ResPanel.Visibility = Show(video);
        BitratePanel.Visibility = Show((video && ext != "gif") || (audioOut && ext is "mp3" or "aac"));
        NoOptionsPanel.Visibility = Show(_cat.Engine == Engine.Office || (_cat.Id == Category.Image && !lossyImage && ext == "ico") || ext is "wav" or "flac");
        EngineText.Visibility = Show(_cat.Engine == Engine.Office);
        UpdateQualityText();
    }

    static Visibility Show(bool b) => b ? Visibility.Visible : Visibility.Collapsed;

    void OnQualityChanged(object s, RoutedPropertyChangedEventArgs<double> e) => UpdateQualityText();
    void UpdateQualityText() { if (QualityValue is not null) QualityValue.Text = Loc.Percent(QualitySlider.Value / 100); }

    void OnWidthChanged(object s, TextChangedEventArgs e) => Sync(WBox, HBox, r => 1 / r);
    void OnHeightChanged(object s, TextChangedEventArgs e) => Sync(HBox, WBox, r => r);

    void Sync(TextBox from, TextBox to, Func<double, double> factor)
    {
        if (_syncing || _ratio is not double r || KeepRatio.IsChecked != true || !int.TryParse(from.Text, out var v)) return;
        _syncing = true;
        to.Text = Math.Max(1, (int)Math.Round(v * factor(r))).ToString();
        _syncing = false;
    }

    void OnPickFolder(object s, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = Path.GetDirectoryName(_files[0]) };
        if (dlg.ShowDialog(this) == true)
        {
            _folder = dlg.FolderName;
            FolderText.Text = _folder;
            FolderText.Visibility = Visibility.Visible;
        }
        else if (_folder is null) SaveNext.IsChecked = true;
    }

    void OnOk(object s, RoutedEventArgs e)
    {
        if (Chosen is null) return;
        if (QualityPanel.Visibility == Visibility.Visible) Options.Quality = (int)QualitySlider.Value;
        if (SizePanel.Visibility == Visibility.Visible)
        {
            Options.KeepRatio = KeepRatio.IsChecked == true;
            if (int.TryParse(WBox.Text, out var w) && w > 0) Options.Width = w;
            if (int.TryParse(HBox.Text, out var h) && h > 0) Options.Height = h;
            // With the ratio kept, one side is enough and avoids rounding drift.
            if (Options.KeepRatio && Options.Width is not null) Options.Height = null;
        }
        if (ResPanel.Visibility != Visibility.Visible) Options.VideoHeight = null;
        if (BitratePanel.Visibility != Visibility.Visible) Options.AudioBitrateK = null;
        Options.OutputFolder = SaveOther.IsChecked == true ? _folder : null;

        if (Remember.IsChecked == true)
        {
            var st = Settings.Load();
            st.Remembered[_cat.Key] = new ConversionOptions
            {
                Quality = (int)QualitySlider.Value, VideoHeight = Options.VideoHeight,
                AudioBitrateK = Options.AudioBitrateK, KeepRatio = KeepRatio.IsChecked == true,
            };
            st.Save();
        }
        DialogResult = true;
    }

    void OnCancel(object s, RoutedEventArgs e) => DialogResult = false;
}

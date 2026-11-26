using System.Windows;
using Tahwil.Core;

namespace Tahwil.UI;

/// <summary>Opened from the Start menu: version, how to use, and a manual update check.</summary>
public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        Theme.Decorate(this);
        NameText.Text = Loc.AppName;
        VersionText.Text = Loc.Version(Updater.CurrentText);
        HintText.Text = Loc.AboutHint;
        UpdateBtn.Content = Loc.CheckUpdates;
        var pending = Settings.Load().PendingVersion;
        if (pending is not null) UpdateText.Text = Loc.UpdateReady(pending);
    }

    async void OnCheck(object s, RoutedEventArgs e)
    {
        UpdateBtn.IsEnabled = false;
        UpdateText.Text = Loc.Checking;
        var (result, version) = await Updater.CheckAsync(force: true);
        UpdateText.Text = result switch
        {
            Updater.Result.Downloaded => Loc.UpdateReady(version!),
            Updater.Result.UpToDate => Loc.UpToDate,
            _ => Loc.UpdateFailed,
        };
        UpdateBtn.IsEnabled = true;
    }

    void OnClose(object s, RoutedEventArgs e) => Close();
}

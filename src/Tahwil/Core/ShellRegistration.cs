using Microsoft.Win32;
using Windows.Management.Deployment;

namespace Tahwil.Core;

/// <summary>
/// Adds "Convert to" to the right-click menu: a sparse package that loads TahwilShell.dll shows it in the
/// Windows 11 menu and under "Show more options"; plain registry verbs are the fallback.
/// </summary>
public static class ShellRegistration
{
    const string PackageName = "Weelr.Tahwil";
    const string Classes = @"Software\Classes";
    const string VerbKey = "Tahwil";
    const string MenuPrefix = "Tahwil.Menu.";

    public static int Register()
    {
        File.WriteAllText(Path.Combine(AppPaths.BaseDir, "menu.txt"), Formats.ExportMenuTable());
        // Off the UI thread: awaiting WinRT here would deadlock on the dispatcher.
        var ok = Task.Run(RegisterPackageAsync).GetAwaiter().GetResult();
        // The packaged command also shows under "Show more options", so the registry verbs
        // are only a fallback for when the package can't be registered (e.g. admin prompt declined).
        if (ok) UnregisterClassic(); else RegisterClassic();
        CleanOldShellDirs();
        return 0;
    }

    public static int Unregister()
    {
        UnregisterClassic();
        Task.Run(UnregisterPackageAsync).GetAwaiter().GetResult();
        return 0;
    }

    // ---------- classic menu ----------

    static void RegisterClassic()
    {
        UnregisterClassic();
        var exe = AppPaths.Exe;
        using var root = Registry.CurrentUser.CreateSubKey(Classes);
        foreach (var cat in Formats.All)
        foreach (var src in cat.Sources)
        {
            var targets = Formats.TargetsFor([$"x.{src}"]);
            using (var menu = root.CreateSubKey($@"{MenuPrefix}{src}\shell"))
            {
                int i = 0; bool audioSep = false;
                foreach (var t in targets)
                {
                    using var k = menu.CreateSubKey($"{++i:00}_{t.Ext}{(t.AudioOnly ? "_a" : "")}");
                    k.SetValue("MUIVerb", t.Label);
                    if (t.AudioOnly && !audioSep) { k.SetValue("CommandFlags", 0x20, RegistryValueKind.DWord); audioSep = true; }
                    using var cmd = k.CreateSubKey("command");
                    cmd.SetValue("", $"\"{exe}\" convert --to {t.Ext}{(t.AudioOnly ? " --audio" : "")} \"%1\"");
                }
                using var c = menu.CreateSubKey("99_custom");
                c.SetValue("MUIVerb", Loc.IsArabic ? "مخصص…" : "Custom…");
                c.SetValue("CommandFlags", 0x20, RegistryValueKind.DWord);
                using var cc = c.CreateSubKey("command");
                cc.SetValue("", $"\"{exe}\" convert --custom --to {targets.FirstOrDefault()?.Ext} \"%1\"");
            }
            using var verb = root.CreateSubKey($@"SystemFileAssociations\.{src}\shell\{VerbKey}");
            verb.SetValue("MUIVerb", Loc.IsArabic ? "تحويل إلى" : "Convert to");
            verb.SetValue("Icon", $"\"{exe}\",0");
            verb.SetValue("ExtendedSubCommandsKey", MenuPrefix + src);
            verb.SetValue("MultiSelectModel", "Player");
        }
    }

    static void UnregisterClassic()
    {
        using var root = Registry.CurrentUser.OpenSubKey(Classes, true);
        if (root is null) return;
        foreach (var name in root.GetSubKeyNames().Where(n => n.StartsWith(MenuPrefix)))
            root.DeleteSubKeyTree(name, false);
        using var sfa = root.OpenSubKey("SystemFileAssociations", true);
        if (sfa is null) return;
        foreach (var ext in sfa.GetSubKeyNames())
            sfa.DeleteSubKeyTree($@"{ext}\shell\{VerbKey}", false);
    }

    // ---------- Windows 11 menu (sparse package) ----------

    static async Task<bool> RegisterPackageAsync()
    {
        var msix = Path.Combine(AppPaths.BaseDir, "TahwilShell.msix");
        if (!File.Exists(msix)) { Log.Info("no msix, skipping modern menu"); return false; }
        try
        {
            var pm = new PackageManager();
            var opts = new AddPackageOptions
            {
                ExternalLocationUri = new Uri(AppPaths.BaseDir),
                ForceUpdateFromAnyVersion = true,
            };
            var r = await pm.AddPackageByUriAsync(new Uri(msix), opts);
            if (r.ExtendedErrorCode is { } err) { Log.Error("add package", err); return false; }
            return true;
        }
        catch (Exception e) { Log.Error("add package", e); return false; }
    }

    static async Task UnregisterPackageAsync()
    {
        try
        {
            var pm = new PackageManager();
            foreach (var p in pm.FindPackagesForUser(string.Empty).Where(p => p.Id.Name == PackageName).ToList())
                await pm.RemovePackageAsync(p.Id.FullName);
        }
        catch (Exception e) { Log.Error("remove package", e); }
    }

    /// <summary>Explorer keeps the old shell DLL loaded, so each version lives in its own folder; drop the stale ones.</summary>
    static void CleanOldShellDirs()
    {
        var shell = Path.Combine(AppPaths.BaseDir, "shell");
        if (!Directory.Exists(shell)) return;
        foreach (var d in Directory.GetDirectories(shell))
            if (Path.GetFileName(d) != Updater.CurrentText)
                try { Directory.Delete(d, true); } catch { }
    }
}

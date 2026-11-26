using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using Tahwil.Core;

namespace Tahwil.UI;

/// <summary>Follows the Windows light/dark setting and gives windows Windows 11 corners and shadow.</summary>
public static class Theme
{
    public static bool IsDark { get; private set; }

    public static void Apply(ResourceDictionary res)
    {
        IsDark = ReadDark();
        var p = IsDark
            ? new { Bg = "#202228", Card = "#2A2D34", Stroke = "#3A3E48", Text = "#F1F2F5", Sub = "#A3A8B4", Accent = "#60A5FA", AccentText = "#0B1220", Ok = "#4ADE80", Bad = "#F87171", Track = "#3A3E48", Hover = "#323848" }
            : new { Bg = "#F9F9FB", Card = "#FFFFFF", Stroke = "#E3E5EA", Text = "#1B1D22", Sub = "#5F6470", Accent = "#2563EB", AccentText = "#FFFFFF", Ok = "#16A34A", Bad = "#DC2626", Track = "#E7E9EE", Hover = "#EEF2FF" };
        foreach (var prop in p.GetType().GetProperties())
            res[prop.Name] = Brush((string)prop.GetValue(p)!);
    }

    static SolidColorBrush Brush(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    static bool ReadDark()
    {
        using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
    }

    /// <summary>Call from the window constructor.</summary>
    public static void Decorate(Window w)
    {
        w.FlowDirection = Loc.Flow;
        w.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            int dark = IsDark ? 1 : 0, round = 2 /* DWMWCP_ROUND */;
            DwmSetWindowAttribute(hwnd, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref dark, sizeof(int));
            DwmSetWindowAttribute(hwnd, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref round, sizeof(int));
        };
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Badge color per category, same as the mockups.</summary>
    public static Brush BadgeFor(Category c) => Brush(c switch
    {
        Category.Document => "#2B6CD4",
        Category.Sheet => "#1F8A4C",
        Category.Slides => "#D2581F",
        Category.Image => "#0F9D8A",
        Category.Audio => "#E07A10",
        _ => "#7C3AED",
    });
}

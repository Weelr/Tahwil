using System.Globalization;

namespace Tahwil.Core;

/// <summary>Arabic or English UI strings, following the Windows display language.</summary>
public static class Loc
{
    /// <summary>Follows the Windows display language.</summary>
    public static bool IsArabic { get; } = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";

    public static System.Windows.FlowDirection Flow =>
        IsArabic ? System.Windows.FlowDirection.RightToLeft : System.Windows.FlowDirection.LeftToRight;

    static string T(string ar, string en) => IsArabic ? ar : en;

    public static string AppName => T("تحويل", "Tahwil");
    public static string Converting(string name) => T($"تحويل {name}", $"Converting {name}");
    public static string ToFormat(string fmt) => T($"إلى {fmt}", $"to {fmt}");
    public static string FilesProgress(int done, int total) => T($"{Num(done)} من {Num(total)} ملفات", $"{done} of {total} files");
    public static string Remaining(TimeSpan t) => t.TotalSeconds < 60
        ? T($"باقي {Num((int)Math.Ceiling(t.TotalSeconds))} ثانية", $"{Math.Ceiling(t.TotalSeconds)}s left")
        : T($"باقي {Num((int)Math.Ceiling(t.TotalMinutes))} دقيقة", $"{Math.Ceiling(t.TotalMinutes)} min left");
    public static string Preparing => T("جاري التجهيز…", "Preparing…");
    public static string Waiting => T("بالانتظار", "Waiting");
    public static string Done => T("تم", "Done");
    public static string Failed => T("ما زبط", "Failed");
    public static string Cancelled => T("انلغى", "Cancelled");
    public static string ShowDetails => T("عرض التفاصيل", "Show details");
    public static string HideDetails => T("إخفاء التفاصيل", "Hide details");
    public static string OpenFolder => T("فتح المجلد", "Open folder");
    public static string Cancel => T("إلغاء", "Cancel");
    public static string Close => T("إغلاق", "Close");
    public static string AllDone(int n) => T($"خلص تحويل {Num(n)} ملفات", $"Converted {n} files");
    public static string OneDone => T("خلص التحويل", "Conversion finished");
    public static string SomeFailed(int ok, int bad) => T($"تم {Num(ok)} وما زبط {Num(bad)}", $"{ok} done, {bad} failed");

    // Custom dialog
    public static string CustomTitle => T("تحويل مخصص", "Custom conversion");
    public static string Selected(int n, string what) => n == 1 ? what : T($"{Num(n)} ملفات مختارة", $"{n} files selected");
    public static string CurrentSize(string s) => T($"الحجم الحالي: {s}", $"Current size: {s}");
    public static string Format => T("الصيغة", "Format");
    public static string Quality => T("الجودة", "Quality");
    public static string BestQuality => T("أعلى جودة", "Best quality");
    public static string Balanced => T("متوازن", "Balanced");
    public static string Smallest => T("أصغر حجم", "Smallest size");
    public static string Dimensions => T("الأبعاد", "Dimensions");
    public static string Width => T("العرض", "Width");
    public static string Height => T("الارتفاع", "Height");
    public static string KeepRatio => T("حافظ على النسبة", "Keep aspect ratio");
    public static string Resolution => T("الدقة", "Resolution");
    public static string Original => T("الأصلية", "Original");
    public static string AudioBitrate => T("جودة الصوت", "Audio bitrate");
    public static string SaveLocation => T("مكان الحفظ", "Save to");
    public static string NextToOriginal => T("جنب الملف الأصلي", "Next to the original");
    public static string OtherFolder => T("مجلد ثاني…", "Another folder…");
    public static string RememberSettings => T("تذكر الإعدادات", "Remember settings");
    public static string Convert => T("تحويل", "Convert");
    public static string NoOptions => T("ما فيه إعدادات إضافية لهالصيغة.", "This format has no extra settings.");
    public static string EngineOffice => T("بيستخدم الأوفيس المنزّل عندك", "Uses your installed Microsoft Office");
    public static string EngineLibre => T("بيستخدم المحول المدمج", "Uses the built-in converter");

    // About window
    public static string Version(string v) => T($"الإصدار {v}", $"Version {v}");
    public static string AboutHint => T("اضغط رايت كليك على أي ملف واختر \"تحويل إلى\".", "Right-click any file and choose \"Convert to\".");
    public static string CheckUpdates => T("تحقق من التحديثات", "Check for updates");
    public static string Checking => T("جاري التحقق…", "Checking…");
    public static string UpToDate => T("عندك آخر إصدار", "You have the latest version");
    public static string UpdateReady(string v) => T($"نزل الإصدار {v} وبيتثبت لما تقفل البرنامج", $"Version {v} downloaded, installs when you close the app");
    public static string UpdateFailed => T("ما قدرت أتحقق الحين", "Couldn't check right now");

    public static string Size(long bytes)
    {
        double v = bytes; string[] ar = ["بايت", "كيلو", "ميجا", "جيجا"]; string[] en = ["B", "KB", "MB", "GB"];
        int i = 0; while (v >= 1024 && i < 3) { v /= 1024; i++; }
        var s = v.ToString(i == 0 ? "0" : "0.#", CultureInfo.InvariantCulture);
        return IsArabic ? $"{Num(s)} {ar[i]}" : $"{s} {en[i]}";
    }

    /// <summary>Arabic-Indic digits for Arabic UI.</summary>
    public static string Num(object n)
    {
        var s = Str(n);
        if (!IsArabic) return s;
        var chars = s.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (chars[i] is >= '0' and <= '9') chars[i] = (char)('٠' + (chars[i] - '0'));
            else if (chars[i] == '.') chars[i] = '٫';
        }
        return new string(chars);
    }

    static string Str(object n) => System.Convert.ToString(n, CultureInfo.InvariantCulture) ?? "";
    public static string Percent(double p) => IsArabic ? $"{Num((int)Math.Round(p * 100))}٪" : $"{Math.Round(p * 100)}%";
}

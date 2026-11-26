namespace Tahwil.Core;

public enum Category { Document, Sheet, Slides, Image, Audio, Video }

public enum Engine { Office, Image, Media }

/// <summary>One output format offered in the menu for a category.</summary>
/// <param name="Ext">Output extension without the dot.</param>
/// <param name="AudioOnly">True for "extract audio" entries under video.</param>
public sealed record Target(string Ext, string Ar, string En, bool AudioOnly = false)
{
    public string Label => Loc.IsArabic ? Ar : En;
}

public sealed record CategoryInfo(Category Id, string Key, Engine Engine, string[] Sources, Target[] Targets, string Ar, string En)
{
    public string Label => Loc.IsArabic ? Ar : En;
}

/// <summary>The single source of truth for which formats convert to which.
/// The shell extension reads an exported copy of this table (menu.txt).</summary>
public static class Formats
{
    public static readonly CategoryInfo[] All =
    [
        new(Category.Document, "doc", Engine.Office,
            ["docx", "doc", "docm", "odt", "rtf", "txt", "pdf"],
            [
                new("docx", "مستند Word", "Word document"),
                new("pdf", "مستند PDF", "PDF document"),
                new("txt", "نص TXT", "Plain text (TXT)"),
                new("rtf", "نص منسق RTF", "Rich text (RTF)"),
                new("odt", "مستند ODT", "OpenDocument (ODT)"),
            ], "مستند", "Document"),

        new(Category.Sheet, "sheet", Engine.Office,
            ["xlsx", "xls", "xlsm", "ods", "csv"],
            [
                new("xlsx", "جدول Excel", "Excel workbook"),
                new("csv", "جدول CSV", "CSV table"),
                new("pdf", "مستند PDF", "PDF document"),
            ], "جدول", "Spreadsheet"),

        new(Category.Slides, "slides", Engine.Office,
            ["pptx", "ppt", "pptm", "odp", "pps", "ppsx"],
            [
                new("pptx", "عرض PowerPoint", "PowerPoint presentation"),
                new("pdf", "مستند PDF", "PDF document"),
            ], "عرض", "Presentation"),

        new(Category.Image, "image", Engine.Image,
            ["jpg", "jpeg", "png", "webp", "heic", "heif", "bmp", "gif", "ico", "tif", "tiff", "avif"],
            [
                new("jpg", "صورة JPG", "JPG image"),
                new("png", "صورة PNG", "PNG image"),
                new("webp", "صورة WEBP", "WEBP image"),
                new("heic", "صورة HEIC", "HEIC image"),
                new("bmp", "صورة BMP", "BMP image"),
                new("gif", "صورة GIF", "GIF image"),
                new("ico", "أيقونة ICO", "Icon (ICO)"),
                new("pdf", "مستند PDF", "PDF document"),
            ], "صورة", "Image"),

        new(Category.Audio, "audio", Engine.Media,
            ["mp3", "wav", "aac", "m4a", "flac", "ogg", "oga", "opus", "wma", "aiff", "aif"],
            [
                new("mp3", "صوت MP3", "MP3 audio"),
                new("wav", "صوت WAV", "WAV audio"),
                new("aac", "صوت AAC", "AAC audio"),
                new("flac", "صوت FLAC", "FLAC audio"),
                new("ogg", "صوت OGG", "OGG audio"),
            ], "صوت", "Audio"),

        new(Category.Video, "video", Engine.Media,
            ["mp4", "mkv", "avi", "mov", "webm", "wmv", "flv", "m4v", "mpg", "mpeg", "3gp", "ts"],
            [
                new("mp4", "فيديو MP4", "MP4 video"),
                new("mkv", "فيديو MKV", "MKV video"),
                new("avi", "فيديو AVI", "AVI video"),
                new("mov", "فيديو MOV", "MOV video"),
                new("gif", "صورة متحركة GIF", "Animated GIF"),
                new("mp3", "استخراج الصوت MP3", "Extract audio (MP3)", AudioOnly: true),
                new("wav", "استخراج الصوت WAV", "Extract audio (WAV)", AudioOnly: true),
                new("aac", "استخراج الصوت AAC", "Extract audio (AAC)", AudioOnly: true),
                new("flac", "استخراج الصوت FLAC", "Extract audio (FLAC)", AudioOnly: true),
                new("ogg", "استخراج الصوت OGG", "Extract audio (OGG)", AudioOnly: true),
            ], "فيديو", "Video"),
    ];

    static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["jpeg"] = "jpg", ["heif"] = "heic", ["tiff"] = "tif", ["m4a"] = "aac", ["oga"] = "ogg", ["aif"] = "aiff",
    };

    public static string ExtOf(string path) => Path.GetExtension(path).TrimStart('.').ToLowerInvariant();

    /// <summary>Normalizes an extension so "jpeg" and "jpg" count as the same format.</summary>
    public static string Canonical(string ext) => Aliases.TryGetValue(ext, out var c) ? c : ext.ToLowerInvariant();

    public static CategoryInfo? CategoryOf(string path)
    {
        var ext = ExtOf(path);
        return All.FirstOrDefault(c => c.Sources.Contains(ext));
    }

    public static CategoryInfo Get(Category id) => All.First(c => c.Id == id);

    public static CategoryInfo? ByKey(string key) => All.FirstOrDefault(c => c.Key == key);

    /// <summary>Targets that every selected file can convert to, taken from the first file's category.
    /// The format the files already share is left out.</summary>
    public static IReadOnlyList<Target> TargetsFor(IReadOnlyList<string> files)
    {
        var cats = files.Select(CategoryOf).ToList();
        if (cats.Count == 0 || cats.Any(c => c is null)) return [];
        var primary = cats[0]!;
        var exts = files.Select(f => Canonical(ExtOf(f))).Distinct().ToList();
        var sameExt = exts.Count == 1 ? exts[0] : null;
        return primary.Targets
            // Same output extension is enough across categories (video "extract MP3" + audio "MP3").
            .Where(t => cats.All(c => c!.Targets.Any(x => x.Ext == t.Ext)))
            .Where(t => t.Ext != sameExt || t.AudioOnly)
            .ToList();
    }

    /// <summary>Writes the table the shell extension reads. Lines:
    /// L|key|ar|en, C|cat|src,src, T|cat|ext|audioOnly|ar|en</summary>
    public static string ExportMenuTable()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("L|title|تحويل إلى|Convert to");
        sb.AppendLine("L|custom|مخصص…|Custom…");
        foreach (var c in All)
        {
            sb.AppendLine($"C|{c.Key}|{string.Join(',', c.Sources)}");
            foreach (var t in c.Targets)
                sb.AppendLine($"T|{c.Key}|{t.Ext}|{(t.AudioOnly ? 1 : 0)}|{t.Ar}|{t.En}");
        }
        sb.AppendLine("A|" + string.Join(',', Aliases.Select(a => $"{a.Key}={a.Value}")));
        return sb.ToString();
    }
}

using System.Windows.Media.Imaging;
using ImageMagick;
using Tahwil.Core;

namespace Tahwil.Engines;

/// <summary>Images through Magick.NET, with the Windows HEIF encoder as a fallback for HEIC output.</summary>
public sealed class ImageConverter : IConverter
{
    static readonly Guid HeifContainer = new("E1E62521-6787-405B-A339-500715B5763F");

    public Task ConvertAsync(string source, string dest, Target target, ConversionOptions o,
        IProgress<double> progress, CancellationToken ct) => Task.Run(() =>
    {
        using var frames = new MagickImageCollection(source);
        progress.Report(0.4);
        ct.ThrowIfCancellationRequested();

        bool animated = frames.Count > 1 && target.Ext is "gif" or "webp";
        if (animated) frames.Coalesce();
        else while (frames.Count > 1) frames.RemoveAt(frames.Count - 1);

        foreach (var img in frames.Cast<MagickImage>())
        {
            img.AutoOrient();
            Resize(img, o);
            Prepare(img, target.Ext, o);
        }
        progress.Report(0.7);
        ct.ThrowIfCancellationRequested();

        if (target.Ext == "heic") WriteHeic((MagickImage)frames[0], dest, o);
        else if (animated) { frames.Optimize(); frames.Write(dest, FormatOf(target.Ext)); }
        else frames[0].Write(dest, FormatOf(target.Ext));
        progress.Report(1);
    }, ct);

    static void Resize(MagickImage img, ConversionOptions o)
    {
        if (o.Width is null && o.Height is null) return;
        var g = new MagickGeometry((uint)(o.Width ?? 0), (uint)(o.Height ?? 0)) { IgnoreAspectRatio = !o.KeepRatio && o.Width is not null && o.Height is not null };
        img.Resize(g);
    }

    static void Prepare(MagickImage img, string ext, ConversionOptions o)
    {
        img.Quality = (uint)Math.Clamp(o.Quality ?? (ext == "webp" ? 85 : 90), 1, 100);
        switch (ext)
        {
            case "jpg" or "bmp" or "pdf":
                if (img.HasAlpha) { img.BackgroundColor = MagickColors.White; img.Alpha(AlphaAction.Remove); }
                break;
            case "ico":
                if (img.Width > 256 || img.Height > 256) img.Resize(new MagickGeometry(256, 256));
                img.Settings.SetDefine(MagickFormat.Icon, "auto-resize", "256,128,64,48,32,16");
                break;
        }
    }

    static MagickFormat FormatOf(string ext) => ext switch
    {
        "jpg" => MagickFormat.Jpeg, "png" => MagickFormat.Png, "webp" => MagickFormat.WebP,
        "bmp" => MagickFormat.Bmp, "gif" => MagickFormat.Gif, "ico" => MagickFormat.Icon,
        "pdf" => MagickFormat.Pdf, "heic" => MagickFormat.Heic,
        _ => throw new NotSupportedException(ext),
    };

    /// <summary>Magick.NET can't always encode HEIC, so fall back to the Windows HEIF codec.</summary>
    static void WriteHeic(MagickImage img, string dest, ConversionOptions o)
    {
        try { img.Write(dest, MagickFormat.Heic); if (new FileInfo(dest).Length > 0) return; }
        catch (MagickException) { }

        using var png = new MemoryStream();
        img.Write(png, MagickFormat.Png);
        png.Position = 0;
        var frame = BitmapFrame.Create(png, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        BitmapEncoder enc;
        try { enc = BitmapEncoder.Create(HeifContainer); }
        catch (Exception e) { throw new NotSupportedException("HEIC encoder missing (install HEIF Image Extensions)", e); }
        enc.Frames.Add(frame);
        using var fs = File.Create(dest);
        enc.Save(fs);
    }
}

using System.Diagnostics;
using System.Runtime.InteropServices;
using Tahwil.Core;

namespace Tahwil.Engines;

/// <summary>Documents, spreadsheets and presentations: Microsoft Office when installed, else the bundled LibreOffice.</summary>
public sealed class DocumentConverter : IConverter
{
    public static bool HasOffice(Category c) => Type.GetTypeFromProgID(ProgIdFor(c)) is not null;

    static string ProgIdFor(Category c) => c switch
    {
        Category.Sheet => "Excel.Application",
        Category.Slides => "PowerPoint.Application",
        _ => "Word.Application",
    };

    readonly Category _category;
    public DocumentConverter(Category category) => _category = category;

    public async Task ConvertAsync(string source, string dest, Target target, ConversionOptions o,
        IProgress<double> progress, CancellationToken ct)
    {
        progress.Report(0.1);
        if (HasOffice(_category))
        {
            try { await RunSta(() => Office.Convert(_category, source, dest, target.Ext)); progress.Report(1); return; }
            catch (Exception e) when (e is COMException or InvalidCastException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
            {
                Debug.WriteLine($"Office failed, falling back: {e.Message}");
            }
        }
        await LibreOffice.ConvertAsync(_category, source, dest, target.Ext, progress, ct);
        progress.Report(1);
    }

    static Task RunSta(Action action)
    {
        var tcs = new TaskCompletionSource();
        var t = new Thread(() => { try { action(); tcs.SetResult(); } catch (Exception e) { tcs.SetException(e); } });
        t.SetApartmentState(ApartmentState.STA);
        t.IsBackground = true;
        t.Start();
        return tcs.Task;
    }

    /// <summary>Late-bound Office automation, so no Office assemblies are needed at build time.</summary>
    static class Office
    {
        public static void Convert(Category c, string src, string dest, string ext)
        {
            switch (c)
            {
                case Category.Document: Word(src, dest, ext); break;
                case Category.Sheet: Excel(src, dest, ext); break;
                default: PowerPoint(src, dest, ext); break;
            }
        }

        static dynamic Create(string progId) => Activator.CreateInstance(Type.GetTypeFromProgID(progId)!)!;

        static void Word(string src, string dest, string ext)
        {
            dynamic app = Create("Word.Application");
            try
            {
                app.Visible = false; app.DisplayAlerts = 0;
                dynamic doc = app.Documents.Open(src, false, true, false);
                try
                {
                    int fmt = ext switch { "docx" => 16, "pdf" => 17, "txt" => 7, "rtf" => 6, "odt" => 23, _ => throw new NotSupportedException(ext) };
                    if (ext == "txt") doc.SaveAs2(dest, fmt, Type.Missing, Type.Missing, false, Type.Missing, Type.Missing,
                                                  Type.Missing, Type.Missing, Type.Missing, Type.Missing, 65001);
                    else doc.SaveAs2(dest, fmt);
                }
                finally { doc.Close(false); Marshal.FinalReleaseComObject(doc); }
            }
            finally { app.Quit(false); Marshal.FinalReleaseComObject(app); }
        }

        static void Excel(string src, string dest, string ext)
        {
            dynamic app = Create("Excel.Application");
            try
            {
                app.Visible = false; app.DisplayAlerts = false;
                dynamic wb = app.Workbooks.Open(src, 0, true);
                try
                {
                    if (ext == "pdf") wb.ExportAsFixedFormat(0, dest);
                    else wb.SaveAs(dest, ext == "csv" ? 62 : 51);
                }
                finally { wb.Close(false); Marshal.FinalReleaseComObject(wb); }
            }
            finally { app.Quit(); Marshal.FinalReleaseComObject(app); }
        }

        static void PowerPoint(string src, string dest, string ext)
        {
            dynamic app = Create("PowerPoint.Application");
            try
            {
                dynamic pres = app.Presentations.Open(src, -1, 0, 0); // ReadOnly, Untitled=false, WithWindow=false
                try { pres.SaveAs(dest, ext == "pdf" ? 32 : 24); }
                finally { pres.Close(); Marshal.FinalReleaseComObject(pres); }
            }
            finally { app.Quit(); Marshal.FinalReleaseComObject(app); }
        }
    }

    /// <summary>Headless soffice.exe from the bundled LibreOffice.</summary>
    static class LibreOffice
    {
        static readonly SemaphoreSlim OneAtATime = new(1, 1);

        static string Filter(Category c, string ext) => (c, ext) switch
        {
            (_, "pdf") => c switch { Category.Sheet => "pdf:calc_pdf_Export", Category.Slides => "pdf:impress_pdf_Export", _ => "pdf:writer_pdf_Export" },
            (_, "docx") => "docx:MS Word 2007 XML",
            (_, "txt") => "txt:Text (encoded):UTF8",
            (_, "rtf") => "rtf:Rich Text Format",
            (_, "odt") => "odt:writer8",
            (_, "xlsx") => "xlsx:Calc MS Excel 2007 XML",
            (_, "csv") => "csv:Text - txt - csv (StarCalc):44,34,76,1",
            (_, "pptx") => "pptx:Impress MS PowerPoint 2007 XML",
            _ => throw new NotSupportedException(ext),
        };

        public static async Task ConvertAsync(Category c, string src, string dest, string ext,
            IProgress<double> progress, CancellationToken ct)
        {
            if (!File.Exists(AppPaths.Soffice)) throw new FileNotFoundException("LibreOffice engine missing", AppPaths.Soffice);

            await OneAtATime.WaitAsync(ct);
            var outDir = AppPaths.TempDir();
            try
            {
                var profile = new Uri(Path.Combine(AppPaths.DataDir, "lo-profile")).AbsoluteUri;
                var psi = new ProcessStartInfo(AppPaths.Soffice) { UseShellExecute = false, CreateNoWindow = true };
                foreach (var a in new[] { "--headless", "--norestore", "--nolockcheck", "--nodefault", "--nologo",
                                          $"-env:UserInstallation={profile}" })
                    psi.ArgumentList.Add(a);
                if (Formats.ExtOf(src) == "pdf") psi.ArgumentList.Add("--infilter=writer_pdf_import");
                psi.ArgumentList.Add("--convert-to"); psi.ArgumentList.Add(Filter(c, ext));
                psi.ArgumentList.Add("--outdir"); psi.ArgumentList.Add(outDir);
                psi.ArgumentList.Add(src);

                using var p = Process.Start(psi)!;
                progress.Report(0.3);
                // soffice gives no progress; creep the bar so the user sees it's alive.
                var started = DateTime.UtcNow;
                while (!p.HasExited)
                {
                    try { await Task.Delay(250, ct); }
                    catch (OperationCanceledException) { try { p.Kill(true); } catch { } throw; }
                    var secs = (DateTime.UtcNow - started).TotalSeconds;
                    progress.Report(0.3 + 0.6 * (1 - Math.Exp(-secs / 6)));
                }

                var produced = Directory.EnumerateFiles(outDir).FirstOrDefault()
                    ?? throw new InvalidOperationException("LibreOffice produced no output");
                File.Move(produced, dest);
            }
            finally
            {
                OneAtATime.Release();
                try { Directory.Delete(outDir, true); } catch { }
            }
        }
    }
}

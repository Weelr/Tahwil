<div dir="rtl">

# تحويل

برنامج صغير لويندوز ١١ يضيف **"تحويل إلى"** في قائمة الرايت كليك. تختار الملف (أو أكثر من ملف) وتختار الصيغة، والملف الجديد ينحفظ جنب الأصلي.

- **المستندات:** Word و PDF و TXT و RTF و ODT
- **الجداول والعروض:** Excel و CSV و PowerPoint و PDF
- **الصور:** PNG و JPG و WEBP و HEIC و ICO و BMP و GIF و PDF
- **الصوت:** MP3 و WAV و AAC و FLAC و OGG
- **الفيديو:** MP4 و MKV و AVI و MOV و GIF، واستخراج الصوت بس

خيار **"مخصص…"** يخليك تحدد الجودة والأبعاد والدقة ومكان الحفظ.

## التثبيت

نزّل `Tahwil-Setup.exe` من صفحة الإصدارات وشغّله. ما يحتاج تنزّل أي شي ثاني، والبرنامج يحدّث نفسه تلقائياً.

المستندات تتحول بالأوفيس إذا كان منزّل، وإذا مو منزّل يستخدم محول مدمج.

</div>

---

# Tahwil

A small Windows 11 tool that adds **Convert to** to the right-click menu for documents, spreadsheets, presentations, images, audio and video. Select one or many files, pick a format, and the result is saved next to the original. **Custom…** lets you set quality, size, resolution and destination.

Download `Tahwil-Setup.exe` from Releases. It bundles everything it needs (FFmpeg, ImageMagick, LibreOffice) and updates itself from this repository's latest release. Documents use Microsoft Office when it is installed, otherwise the bundled LibreOffice.

## Build

```powershell
.\build\build.ps1
```

Needs the .NET 10 SDK, Zig (C++ compiler for the shell extension), `makeappx.exe` from the Windows SDK and Inno Setup. The script downloads FFmpeg and LibreOffice into `.cache`.

| Folder | What |
| --- | --- |
| `src/Tahwil` | WPF app: converters, progress window, Custom dialog, updater, menu registration |
| `src/TahwilShell` | Windows 11 context-menu handler (`IExplorerCommand`) |
| `packaging` | Sparse package manifest and Inno Setup script |
| `build` | Build, icon and engine scripts |

## Licenses

Tahwil bundles [FFmpeg](https://ffmpeg.org) (GPL build), [Magick.NET](https://github.com/dlemstra/Magick.NET) and [LibreOffice](https://www.libreoffice.org) (MPL 2.0), each under its own license.

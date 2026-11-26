# Draws the app icon (blue-violet tile with two arrows) into PNGs and a multi-size .ico.
param([string]$Out = "$PSScriptRoot\..\src\Tahwil\Assets")
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase
New-Item -ItemType Directory -Force $Out | Out-Null

function Render-Icon([int]$size) {
    $dv = New-Object System.Windows.Media.DrawingVisual
    $dc = $dv.RenderOpen()
    $s = $size / 256.0
    $grad = New-Object System.Windows.Media.LinearGradientBrush(
        [System.Windows.Media.Color]::FromRgb(0x25, 0x63, 0xEB), [System.Windows.Media.Color]::FromRgb(0x7C, 0x3A, 0xED), 45)
    $r = 58 * $s
    $dc.DrawRoundedRectangle($grad, $null, (New-Object System.Windows.Rect(0, 0, $size, $size)), $r, $r)
    $pen = New-Object System.Windows.Media.Pen([System.Windows.Media.Brushes]::White, [Math]::Max(1.6, 20 * $s))
    $pen.StartLineCap = 'Round'; $pen.EndLineCap = 'Round'; $pen.LineJoin = 'Round'
    $geo = ([System.Windows.Media.Geometry]::Parse('M 70,98 L 180,98 M 148,66 L 182,98 L 148,130 M 186,158 L 76,158 M 108,126 L 74,158 L 108,190')).Clone()
    $geo.Transform = New-Object System.Windows.Media.ScaleTransform($s, $s)
    $dc.DrawGeometry($null, $pen, $geo)
    $dc.Close()
    $bmp = New-Object System.Windows.Media.Imaging.RenderTargetBitmap($size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bmp.Render($dv)
    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bmp))
    $ms = New-Object System.IO.MemoryStream
    $enc.Save($ms)
    return ,$ms.ToArray()
}

[IO.File]::WriteAllBytes("$Out\Tahwil-256.png", (Render-Icon 256))
foreach ($p in @(@('Square44', 44), @('Square150', 150), @('StoreLogo', 50))) {
    [IO.File]::WriteAllBytes("$Out\$($p[0]).png", (Render-Icon $p[1]))
}

# ICO with PNG-compressed entries
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = $sizes | ForEach-Object { , (Render-Icon $_) }
$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $d = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $bw.Write([byte]$d); $bw.Write([byte]$d); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$images[$i].Length); $bw.Write([UInt32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $bw.Write($img) }
$bw.Flush()
[IO.File]::WriteAllBytes("$Out\Tahwil.ico", $ms.ToArray())
Write-Host "icons -> $Out"


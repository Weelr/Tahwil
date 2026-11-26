# Downloads ffmpeg and LibreOffice and lays them out as build\engines\{ffmpeg.exe, libreoffice\}.
# LibreOffice is trimmed to what headless conversion needs (no help, dictionaries, UI translations).
param(
    [string]$Cache = "$PSScriptRoot\..\.cache",
    [string]$Out = "$PSScriptRoot\engines"
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$FfmpegUrl = 'https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n8.1-latest-win64-gpl-8.1.zip'
$LoVersion = '26.2.7'
$LoUrl = "https://download.documentfoundation.org/libreoffice/stable/$LoVersion/win/x86_64/LibreOffice_${LoVersion}_Win_x86-64.msi"

New-Item -ItemType Directory -Force $Cache, $Out | Out-Null
$Cache = (Resolve-Path $Cache).Path; $Out = (Resolve-Path $Out).Path

function Get-File($url, $path) {
    if (Test-Path $path) { return }
    Write-Host "download $url"
    & curl.exe -L --fail -s -o "$path.part" $url
    if ($LASTEXITCODE) { throw "download failed: $url" }
    Move-Item "$path.part" $path
}

# ---- ffmpeg ----
if (-not (Test-Path "$Out\ffmpeg.exe")) {
    Get-File $FfmpegUrl "$Cache\ffmpeg.zip"
    $tmp = "$Cache\ffmpeg-x"
    if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
    Expand-Archive "$Cache\ffmpeg.zip" $tmp
    Copy-Item (Get-ChildItem $tmp -Recurse -Filter ffmpeg.exe | Select-Object -First 1).FullName "$Out\ffmpeg.exe"
    Remove-Item $tmp -Recurse -Force
}

# ---- LibreOffice ----
$lo = "$Out\libreoffice"
if (-not (Test-Path "$lo\program\soffice.exe")) {
    Get-File $LoUrl "$Cache\lo-$LoVersion.msi"
    $x = "$Cache\lo-x"
    if (Test-Path $x) { Remove-Item $x -Recurse -Force }
    $p = Start-Process msiexec.exe -ArgumentList '/a', "`"$Cache\lo-$LoVersion.msi`"", '/qn', "TARGETDIR=`"$x`"" -Wait -PassThru
    if ($p.ExitCode) { throw "msiexec /a failed: $($p.ExitCode)" }

    New-Item -ItemType Directory -Force $lo | Out-Null
    Move-Item "$x\program", "$x\share", "$x\presets" $lo
    # VC++ runtime next to soffice so it runs on machines without it
    Copy-Item "$x\System64\*.dll" "$lo\program\" -Force
    # bundled fonts (metric-compatible Calibri/Cambria etc.) are picked up from share\fonts
    New-Item -ItemType Directory -Force "$lo\share\fonts\truetype" | Out-Null
    Copy-Item "$x\Fonts\*" "$lo\share\fonts\truetype\" -Force
    Remove-Item $x -Recurse -Force

    # trim
    Remove-Item "$lo\share\extensions\*" -Recurse -Force
    Remove-Item "$lo\program\resource\*" -Recurse -Force -Exclude 'en*'
    Get-ChildItem "$lo\share\config" -Filter 'images_*.zip' | Where-Object Name -ne 'images_colibre.zip' | Remove-Item -Force
    Get-ChildItem "$lo\share\registry\res" -Filter 'registry_*.xcd' -ErrorAction SilentlyContinue |
        Where-Object Name -notmatch 'registry_en' | Remove-Item -Force
    Remove-Item "$lo\share\gallery", "$lo\share\tipoftheday", "$lo\share\wizards" -Recurse -Force -ErrorAction SilentlyContinue
}

$mb = (Get-ChildItem $Out -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
Write-Host ("engines ready: {0:N0} MB -> {1}" -f $mb, $Out)

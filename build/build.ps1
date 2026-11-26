# Builds Tahwil-Setup.exe:  app (self-contained .NET) + shell DLL + sparse package + ffmpeg + LibreOffice.
#   .\build\build.ps1 [-Version 1.2.3]
# Tools are found on PATH, or in %USERPROFILE%\devtools (local machine), or via env vars ZIG / MAKEAPPX / ISCC / DOTNET.
param([string]$Version)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path "$PSScriptRoot\..").Path
$out = "$root\build\out"
$stage = "$out\app"

function Find-Tool($envName, $exe, [string[]]$candidates) {
    $v = [Environment]::GetEnvironmentVariable($envName)
    if ($v -and (Test-Path $v)) { return $v }
    # local devtools first: the dotnet on PATH may be a runtime without the SDK
    foreach ($c in $candidates) { $hit = Get-Item $c -ErrorAction SilentlyContinue | Select-Object -Last 1; if ($hit) { return $hit.FullName } }
    $cmd = Get-Command $exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    throw "$exe not found (set $envName)"
}
$dev = "$env:USERPROFILE\devtools"
$dotnet = Find-Tool DOTNET dotnet.exe @("$dev\dotnet\dotnet.exe")
$zig = Find-Tool ZIG zig.exe @("$dev\zig\*\zig.exe")
$makeappx = Find-Tool MAKEAPPX makeappx.exe @("$dev\sdkbt\bin\*\x64\makeappx.exe", "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\makeappx.exe")
$iscc = Find-Tool ISCC ISCC.exe @("$dev\inno\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe")

if (-not $Version) { $Version = ([xml](Get-Content "$root\src\Tahwil\Tahwil.csproj")).SelectSingleNode('//Version').InnerText }
Write-Host "== Tahwil $Version"

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null

# 1. icons and engines
if (-not (Test-Path "$root\src\Tahwil\Assets\Tahwil.ico")) { & "$PSScriptRoot\make-icons.ps1" }
& "$PSScriptRoot\fetch-engines.ps1"

# 2. app
$env:DOTNET_CLI_TELEMETRY_OPTOUT = 1; $env:DOTNET_NOLOGO = 1
& $dotnet publish "$root\src\Tahwil\Tahwil.csproj" -c Release -o $stage -p:Version=$Version
if ($LASTEXITCODE) { throw 'dotnet publish failed' }

# 3. shell extension, in a per-version folder (Explorer keeps the old one loaded during updates)
$shellDir = "$stage\shell\$Version"
New-Item -ItemType Directory -Force $shellDir | Out-Null
& $zig c++ -shared -Os -target x86_64-windows-gnu -std=c++17 -fno-exceptions -DUNICODE -D_UNICODE `
    -o "$shellDir\TahwilShell.dll" "$root\src\TahwilShell\TahwilShell.cpp" "$root\src\TahwilShell\TahwilShell.def" `
    -lole32 -lshell32 -lshlwapi -luuid
if ($LASTEXITCODE) { throw 'shell DLL build failed' }
Remove-Item "$shellDir\*.pdb", "$shellDir\*.lib" -ErrorAction SilentlyContinue

# 4. menu table for the DLL, straight from the app's Formats
& "$stage\Tahwil.exe" --export-menu "$stage\menu.txt" | Out-Null
for ($i = 0; $i -lt 50 -and -not (Test-Path "$stage\menu.txt"); $i++) { Start-Sleep -Milliseconds 100 }
if (-not (Test-Path "$stage\menu.txt")) { throw 'menu export failed' }

# 5. sparse package (unsigned)
$pkg = "$out\pkg"
New-Item -ItemType Directory -Force "$pkg\Assets" | Out-Null
(Get-Content "$root\packaging\AppxManifest.xml" -Raw).Replace('$VERSION$', $Version) | Set-Content "$pkg\AppxManifest.xml" -Encoding utf8
Copy-Item "$root\src\Tahwil\Assets\Square44.png", "$root\src\Tahwil\Assets\Square150.png", "$root\src\Tahwil\Assets\StoreLogo.png" "$pkg\Assets\"
New-Item -ItemType Directory -Force "$stage\Assets" | Out-Null
Copy-Item "$pkg\Assets\*" "$stage\Assets\"
& $makeappx pack /o /nv /d $pkg /p "$stage\TahwilShell.msix" | Out-Null
if ($LASTEXITCODE) { throw 'makeappx failed' }
# sign with the CN=Weelr key: TAHWIL_PFX / TAHWIL_PFX_PW, or the local devtools copy
$pfx = if ($env:TAHWIL_PFX) { $env:TAHWIL_PFX } else { "$dev\tahwil-signing.pfx" }
$pw = if ($env:TAHWIL_PFX_PW) { $env:TAHWIL_PFX_PW } else { Get-Content "$dev\tahwil-signing.pw" -ErrorAction SilentlyContinue }
if (-not (Test-Path $pfx) -or -not $pw) { throw 'signing key missing (TAHWIL_PFX / TAHWIL_PFX_PW)' }
$signtool = Join-Path (Split-Path $makeappx) 'signtool.exe'
& $signtool sign /q /fd SHA256 /f $pfx /p $pw "$stage\TahwilShell.msix"
if ($LASTEXITCODE) { throw 'signtool failed' }
Copy-Item "$root\packaging\Tahwil.cer" "$stage\Tahwil.cer"

# 6. engines next to the app
New-Item -ItemType Directory -Force "$stage\engines" | Out-Null
Copy-Item "$PSScriptRoot\engines\*" "$stage\engines\" -Recurse

# 7. installer
& $iscc /Q "/DAppVersion=$Version" "/DSrc=$stage" "/DOut=$out" "$root\packaging\Tahwil.iss"
if ($LASTEXITCODE) { throw 'ISCC failed' }
$setup = Get-Item "$out\Tahwil-Setup.exe"
Write-Host ("== {0} ({1:N0} MB)" -f $setup.FullName, ($setup.Length / 1MB))

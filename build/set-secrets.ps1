# One-time: stores the package-signing key as repository secrets so GitHub Actions can sign the build.
# Reads the key from %USERPROFILE%\devtools (never committed).
param([string]$Repo = 'Weelr/Tahwil')
$ErrorActionPreference = 'Stop'
$dev = "$env:USERPROFILE\devtools"
$gh = if (Get-Command gh -ErrorAction SilentlyContinue) { 'gh' } else { "$dev\gh\bin\gh.exe" }
[Convert]::ToBase64String([IO.File]::ReadAllBytes("$dev\tahwil-signing.pfx")) | & $gh secret set TAHWIL_PFX_B64 --repo $Repo
Get-Content "$dev\tahwil-signing.pw" -Raw | & $gh secret set TAHWIL_PFX_PW --repo $Repo
& $gh secret list --repo $Repo

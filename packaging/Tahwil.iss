; One-file installer for Tahwil. Built by build\build.ps1:
;   ISCC /DAppVersion=1.2.3 /DSrc=<staged app folder> /DOut=<output folder> Tahwil.iss
; Installs per user (no admin prompt), so silent self-updates need no elevation.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
; Thumbprint of packaging\Tahwil.cer
#define CertThumbprint "AC7F5649E118B95430A8A318F27849F51466721C"

[Setup]
AppId={{B6F3E1A2-6C1D-4F8E-9A57-3D2C8E4B7A10}
AppName=Tahwil
AppVersion={#AppVersion}
AppVerName=Tahwil {#AppVersion}
AppPublisher=Weelr
AppPublisherURL=https://github.com/Weelr/Tahwil
DefaultDirName={localappdata}\Programs\Tahwil
PrivilegesRequired=lowest
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
DisableWelcomePage=yes
OutputDir={#Out}
OutputBaseFilename=Tahwil-Setup
SetupIconFile=..\src\Tahwil\Assets\Tahwil.ico
UninstallDisplayIcon={app}\Tahwil.exe
UninstallDisplayName=Tahwil
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
WizardStyle=modern
ShowLanguageDialog=auto
; Only our own exe needs closing; Explorer keeps the shell DLL, which lives in a per-version folder.
CloseApplications=yes
CloseApplicationsFilter=Tahwil.exe,ffmpeg.exe,soffice.exe,soffice.bin
RestartApplications=no

[Languages]
Name: "ar"; MessagesFile: "compiler:Languages\Arabic.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
ar.Registering=جاري إضافة "تحويل إلى" لقائمة الرايت كليك…
en.Registering=Adding "Convert to" to the right-click menu…

[Files]
Source: "{#Src}\*"; DestDir: "{app}"; Excludes: "shell\*"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#Src}\shell\*"; DestDir: "{app}\shell"; Flags: onlyifdoesntexist recursesubdirs createallsubdirs

[InstallDelete]
; old engines and app files from a previous version (the shell folders are cleaned by --register)
Type: filesandordirs; Name: "{app}\engines"

[Icons]
Name: "{userprograms}\Tahwil"; Filename: "{app}\Tahwil.exe"

[Run]
; First install only: trust the package-signing certificate machine-wide (one admin prompt).
; Windows 11's new context menu needs a signed package; updates reuse the trust and stay silent.
Filename: "{sys}\certutil.exe"; Parameters: "-f -addstore TrustedPeople ""{app}\Tahwil.cer"""; Verb: runas; StatusMsg: "{cm:Registering}"; Flags: shellexec runhidden waituntilterminated; Check: not CertTrusted
Filename: "{app}\Tahwil.exe"; Parameters: "--register"; StatusMsg: "{cm:Registering}"; Flags: runhidden waituntilterminated

[UninstallRun]
Filename: "{app}\Tahwil.exe"; Parameters: "--unregister"; Flags: runhidden waituntilterminated; RunOnceId: "TahwilUnregister"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
Type: filesandordirs; Name: "{localappdata}\Tahwil"

[Code]
function CertTrusted: Boolean;
begin
  Result := RegKeyExists(HKLM, 'SOFTWARE\Microsoft\SystemCertificates\TrustedPeople\Certificates\{#CertThumbprint}');
end;

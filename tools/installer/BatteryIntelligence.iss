; Battery Intelligence — Inno Setup script
; Produces a single self-contained, machine-wide Setup.exe that runs on any x64
; Windows 10 (17763+) / 11 laptop with NOTHING pre-installed — the .NET 10
; runtime and the Windows App SDK are bundled by the self-contained publish.
;
; Build:
;   1. dotnet publish src/BatteryIntelligence.App/BatteryIntelligence.App.csproj \
;        -c Release -r win-x64 --self-contained true \
;        -p:WindowsAppSDKSelfContained=true -p:PublishReadyToRun=false \
;        --output dist/self-contained
;   2. "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" tools\installer\BatteryIntelligence.iss
;
; Output: dist\BatteryIntelligence-Setup-<version>.exe
;
; Notes
; - Machine-wide install (PrivilegesRequired=admin): one UAC prompt at install,
;   installs to C:\Program Files, and the entry shows in BOTH the classic
;   Control Panel > Programs and Features AND Settings > Apps. The app itself
;   still runs with no elevation (spec S23).
; - The installer and the uninstaller both close a running BatteryIntelligence.exe
;   first (it minimises to the tray, so a plain close is not enough — see the
;   [Code] section). Without this, the running exe locks its own files and the
;   uninstall silently leaves the folder and tray icon behind.
; - The uninstaller removes only what it installed, plus the whole {app} folder.
;   It never touches %LOCALAPPDATA%\BatteryIntelligence (the database + settings)
;   — spec §58: uninstall must not destroy user history.

#define AppName "Battery Intelligence"
#define AppVersion "1.0.0"
#define AppPublisher "Naeem Ahmad"
#define AppPublisherURL "https://battery-intelligence.netlify.app"
#define AppExeName "BatteryIntelligence.exe"
#define SourceDir "..\..\dist\self-contained"

[Setup]
AppId={{DD0D792A-2ED7-46DC-9CB2-DB402C9DE4DA}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppPublisherURL}
AppSupportURL={#AppPublisherURL}
AppUpdatesURL={#AppPublisherURL}
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoCopyright=Copyright (C) 2026 Naeem Ahmad. MIT licensed.
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
OutputDir=..\..\dist
OutputBaseFilename=BatteryIntelligence-Setup-{#AppVersion}
SetupIconFile=..\..\src\BatteryIntelligence.App\Assets\app.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
LicenseFile=..\..\LICENSE
AppReadmeFile={app}\README.md
; We close the app ourselves in [Code] (taskkill /F — it cannot be closed
; gracefully, it hides to tray instead), so leave Restart Manager out of it:
; its "please close this application" page can't succeed here and only confuses.
CloseApplications=no
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion
Source: "..\..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\CHANGELOG.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

; Belt-and-braces: after the tracked files are removed, delete anything left in
; the program folder (a lock that cleared late, a stray temp file). This is the
; Program Files install dir only — user data lives in %LOCALAPPDATA% and is never
; under {app}.
[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
{ BatteryIntelligence.exe intercepts a normal window close and minimises to the
  notification area instead (MainWindow.OnAppWindowClosing), so Restart Manager /
  a graceful WM_CLOSE will not end it. We taskkill it — first politely, then /F —
  before touching files, on both install (upgrade over a running copy) and
  uninstall. }
procedure StopRunningApp;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM {#AppExeName}',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(1200);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#AppExeName}',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(600);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopRunningApp;
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    StopRunningApp;
end;

// The app itself owns "Start when I sign in" (Settings -> an HKCU\...\Run entry).
// The installer deliberately does not manage that, to avoid fighting the app.

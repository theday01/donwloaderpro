; ============================================================
;  EagleVStream v2.1 PRO — Inno Setup Script
;  Developer : Hamza Saadi (EagleShadow)
;  Website   : https://eagleshadow.technology
;  Target OS : Windows 7 SP1 / 8 / 10 / 11  (x64 + x86)
; ============================================================

#define AppName        "EagleVStream v2.1"
#define AppVersion     "2.1"
#define AppPublisher   "Hamza Saadi — EagleShadow"
#define AppURL         "https://eagleshadow.technology"
#define AppExeName     "VideoDownloaderUI.exe"
#define AppDescription "Fast & High Quality Video & Audio Downloader"
#define AppYear        "2026"

; ── Output settings ─────────────────────────────────────────
#define OutputDir      "installer_output"

[Setup]
; ── Identity ─────────────────────────────────────────────────
AppId={{A3F7D821-4C2B-4E9A-8B1F-5D6E3C9A2B7F}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} v{#AppVersion} PRO
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}
AppCopyright=Copyright © {#AppYear} {#AppPublisher}
VersionInfoDescription={#AppDescription}
VersionInfoVersion={#AppVersion}.0.0

; ── Install paths ────────────────────────────────────────────
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
AllowNoIcons=yes
DisableProgramGroupPage=no
DisableDirPage=no

; ── Output ───────────────────────────────────────────────────
OutputDir={#OutputDir}
OutputBaseFilename=EagleVStream_v{#AppVersion}_PRO_Setup
SetupIconFile=logo.ico
UninstallDisplayIcon={app}\logo.ico
UninstallDisplayName={#AppName} v{#AppVersion} PRO

; ── Compression ──────────────────────────────────────────────
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
LZMANumBlockThreads=4

; ── Wizard appearance ────────────────────────────────────────
WizardStyle=modern
WizardSizePercent=120
WizardResizable=yes
ShowLanguageDialog=auto

; ── Permissions ──────────────────────────────────────────────
; Allow installation both for all-users and current-user
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

; ── Architecture ─────────────────────────────────────────────
; Support both 32-bit and 64-bit Windows
ArchitecturesAllowed=x86 x64
ArchitecturesInstallIn64BitMode=x64

; ── Minimum OS ───────────────────────────────────────────────
MinVersion=6.1sp1

; ── Misc ─────────────────────────────────────────────────────
CloseApplications=yes
CloseApplicationsFilter=*{#AppExeName}*
RestartApplications=no
ChangesAssociations=no

; ── Digital signing (uncomment & fill if you have a cert) ────
; SignTool=signtool sign /fd SHA256 /td SHA256 /tr http://timestamp.digicert.com $f
; SignedUninstaller=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "arabic";  MessagesFile: "compiler:Languages\Arabic.isl"

; ════════════════════════════════════════════════════════════
;  TASKS  (shortcuts / options shown to user)
; ════════════════════════════════════════════════════════════
[Tasks]
Name: "desktopicon";     Description: "Create a &Desktop shortcut";      GroupDescription: "Additional icons:"; Flags: unchecked
Name: "quicklaunchicon"; Description: "Create a &Quick Launch shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked; OnlyBelowVersion: 6.1
Name: "startupicon";     Description: "Launch {#AppName} at &Windows startup"; GroupDescription: "Startup:"; Flags: unchecked

; ════════════════════════════════════════════════════════════
;  FILES
;  ─────────────────────────────────────────────────────────
;  FOLDER STRUCTURE REQUIRED BEFORE COMPILING:
;
;  project_root/
;  ├── EagleVStream_Setup.iss          ← this file
;  ├── logo.ico                        ← app icon
;  ├── logo.png
;  ├── developer.png
;  ├── downloader.py
;  │
;  ├── app/                            ← compiled WPF output
;  │   ├── VideoDownloaderUI.exe
;  │   ├── VideoDownloaderUI.dll
;  │   ├── VideoDownloaderUI.runtimeconfig.json
;  │   └── ... (all build output files)
;  │
;  └── redist/                         ← dependency installers
;      ├── dotnet6-desktop-runtime-win-x64.exe
;      ├── dotnet6-desktop-runtime-win-x86.exe
;      ├── python-3.12.9-amd64.exe
;      ├── python-3.12.9-win32.exe     (optional, for 32-bit Windows)
;      ├── ffmpeg/                     ← extracted FFmpeg binaries
;      │   ├── ffmpeg.exe
;      │   ├── ffprobe.exe
;      │   └── ffplay.exe (optional)
;      └── yt-dlp.exe                  ← standalone yt-dlp binary
; ════════════════════════════════════════════════════════════
[Files]

; ── Main Application ─────────────────────────────────────────
Source: "app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; ── Python script & assets ───────────────────────────────────
Source: "downloader.py";   DestDir: "{app}"; Flags: ignoreversion
Source: "logo.png";        DestDir: "{app}"; Flags: ignoreversion
Source: "developer.png";   DestDir: "{app}"; Flags: ignoreversion
Source: "logo.ico";        DestDir: "{app}"; Flags: ignoreversion

; ── Bundled FFmpeg (always installed alongside app) ──────────
Source: "redist\ffmpeg\ffmpeg.exe";  DestDir: "{app}"; Flags: ignoreversion
Source: "redist\ffmpeg\ffprobe.exe"; DestDir: "{app}"; Flags: ignoreversion

; ── Standalone yt-dlp binary ─────────────────────────────────
Source: "redist\yt-dlp.exe"; DestDir: "{app}"; Flags: ignoreversion

; ── .NET 6 Desktop Runtime installers (extracted if needed) ──
Source: "redist\dotnet6-desktop-runtime-win-x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: IsWin64
Source: "redist\dotnet6-desktop-runtime-win-x86.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: not IsWin64

; ── Python installer (extracted if needed) ────────────────────
Source: "redist\python-3.12.9-amd64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: IsWin64
Source: "redist\python-3.12.9-win32.exe";  DestDir: "{tmp}"; Flags: deleteafterinstall; Check: not IsWin64

; ════════════════════════════════════════════════════════════
;  SHORTCUTS
; ════════════════════════════════════════════════════════════
[Icons]
; Start Menu
Name: "{group}\{#AppName}";                  Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\logo.ico"; WorkingDir: "{app}"
Name: "{group}\Uninstall {#AppName}";        Filename: "{uninstallexe}"

; Desktop (optional)
Name: "{autodesktop}\{#AppName}";            Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\logo.ico"; WorkingDir: "{app}"; Tasks: desktopicon

; Quick Launch (optional, Windows XP/Vista/7)
Name: "{userappdata}\Microsoft\Internet Explorer\Quick Launch\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: quicklaunchicon

; Startup (optional)
Name: "{userstartup}\{#AppName}";            Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: startupicon

; ════════════════════════════════════════════════════════════
;  RUN AFTER INSTALL
; ════════════════════════════════════════════════════════════
[Run]
; ── yt-dlp: install via pip as well (keeps it up-to-date) ────
Filename: "python"; Parameters: "-m pip install --upgrade yt-dlp --quiet"; \
    WorkingDir: "{app}"; Flags: runhidden; \
    StatusMsg: "Installing yt-dlp Python package..."; \
    Check: PythonAvailable

; ── Launch app after install (optional) ──────────────────────
Filename: "{app}\{#AppExeName}"; \
    Description: "Launch {#AppName} now"; \
    Flags: nowait postinstall skipifsilent; \
    WorkingDir: "{app}"

; ════════════════════════════════════════════════════════════
;  UNINSTALL — clean leftover files & settings (optional)
; ════════════════════════════════════════════════════════════
[UninstallDelete]
Type: filesandordirs; Name: "{app}"

; ════════════════════════════════════════════════════════════
;  REGISTRY
; ════════════════════════════════════════════════════════════
[Registry]
; Add install path to system PATH so ffmpeg.exe & yt-dlp.exe
; can be found anywhere on the machine.
Root: HKLM; Subkey: "SYSTEM\CurrentControlSet\Control\Session Manager\Environment"; \
    ValueType: expandsz; ValueName: "Path"; \
    ValueData: "{olddata};{app}"; \
    Check: IsAdminInstallMode and (not RegValueExists(HKLM, 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment', 'Path') or (Pos(ExpandConstant('{app}'), GetEnv('PATH')) = 0))

Root: HKCU; Subkey: "Environment"; \
    ValueType: expandsz; ValueName: "Path"; \
    ValueData: "{olddata};{app}"; \
    Check: (not IsAdminInstallMode) and (Pos(ExpandConstant('{app}'), GetEnv('PATH')) = 0)

; ════════════════════════════════════════════════════════════
;  PASCAL SCRIPT — Custom dependency checks & installs
; ════════════════════════════════════════════════════════════
[Code]

// ─── Helper: registry read ────────────────────────────────────────────────────
function RegValueExists(RootKey: Integer; SubKey, ValueName: string): Boolean;
var
  Dummy: string;
begin
  Result := RegQueryStringValue(RootKey, SubKey, ValueName, Dummy);
end;

// ─── Check .NET 6 Desktop Runtime ────────────────────────────────────────────
function DotNetRuntimeInstalled: Boolean;
var
  Version: string;
begin
  // Check for .NET 6.0 Desktop Runtime in registry
  Result := RegQueryStringValue(HKLM,
    'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedhost',
    'Version', Version);
  if not Result then
    Result := RegQueryStringValue(HKLM,
      'SOFTWARE\WOW6432Node\dotnet\Setup\InstalledVersions\x86\sharedhost',
      'Version', Version);
  // Fallback: check well-known folder
  if not Result then
    Result := DirExists(ExpandConstant('{pf}\dotnet\shared\Microsoft.WindowsDesktop.App\6.0'));
  if not Result then
    Result := DirExists('C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App\6.0');
  if not Result then
    Result := DirExists('C:\Program Files (x86)\dotnet\shared\Microsoft.WindowsDesktop.App\6.0');
end;

// ─── Check Python 3 ──────────────────────────────────────────────────────────
function PythonAvailable: Boolean;
var
  ResultCode: Integer;
begin
  // Try running python --version silently
  Result := Exec('python', '--version', '', SW_HIDE, ewWaitUntilTerminated, ResultCode)
            and (ResultCode = 0);
  if not Result then
    Result := Exec('python3', '--version', '', SW_HIDE, ewWaitUntilTerminated, ResultCode)
              and (ResultCode = 0);
  // Also check registry for Python install
  if not Result then
    Result := RegKeyExists(HKLM, 'SOFTWARE\Python\PythonCore') or
              RegKeyExists(HKCU, 'SOFTWARE\Python\PythonCore');
end;

// ─── Check yt-dlp ────────────────────────────────────────────────────────────
function YtDlpInstalled: Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec('yt-dlp', '--version', '', SW_HIDE, ewWaitUntilTerminated, ResultCode)
            and (ResultCode = 0);
  // Also checks if our bundled copy exists
  if not Result then
    Result := FileExists(ExpandConstant('{app}\yt-dlp.exe'));
end;

// ─── Install .NET 6 Desktop Runtime ──────────────────────────────────────────
function InstallDotNetRuntime: Boolean;
var
  InstallerPath: string;
  ResultCode:    Integer;
begin
  if IsWin64 then
    InstallerPath := ExpandConstant('{tmp}\dotnet6-desktop-runtime-win-x64.exe')
  else
    InstallerPath := ExpandConstant('{tmp}\dotnet6-desktop-runtime-win-x86.exe');

  if not FileExists(InstallerPath) then
  begin
    MsgBox('ERROR: .NET 6 Runtime installer not found at: ' + InstallerPath, mbError, MB_OK);
    Result := False;
    Exit;
  end;

  // /install /quiet /norestart = silent install
  Result := Exec(InstallerPath, '/install /quiet /norestart', '', SW_SHOW,
                 ewWaitUntilTerminated, ResultCode);
  Result := Result and (ResultCode = 0);
end;

// ─── Install Python 3 ────────────────────────────────────────────────────────
function InstallPython: Boolean;
var
  InstallerPath: string;
  ResultCode:    Integer;
begin
  if IsWin64 then
    InstallerPath := ExpandConstant('{tmp}\python-3.12.9-amd64.exe')
  else
    InstallerPath := ExpandConstant('{tmp}\python-3.12.9-win32.exe');

  if not FileExists(InstallerPath) then
  begin
    MsgBox('ERROR: Python installer not found at: ' + InstallerPath, mbError, MB_OK);
    Result := False;
    Exit;
  end;

  // InstallAllUsers=0 → per-user  |  PrependPath=1 → add to PATH automatically
  Result := Exec(InstallerPath,
    '/quiet InstallAllUsers=1 PrependPath=1 Include_pip=1 Include_launcher=1 SimpleInstall=1',
    '', SW_SHOW, ewWaitUntilTerminated, ResultCode);
  Result := Result and (ResultCode = 0);
end;

// ─── Install yt-dlp via pip ──────────────────────────────────────────────────
function InstallYtDlpViaPip: Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec('python', '-m pip install --upgrade yt-dlp --quiet', '',
                 SW_HIDE, ewWaitUntilTerminated, ResultCode);
  if not Result then
    Result := Exec('python3', '-m pip install --upgrade yt-dlp --quiet', '',
                   SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

// ─── MAIN: InitializeSetup — called before wizard appears ────────────────────
function InitializeSetup: Boolean;
begin
  Result := True; // always proceed; we handle deps in NextButtonClick
end;

// ─── MAIN: PrepareToInstall — called after user clicks Install ───────────────
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  NeedsDotNet, NeedsPython: Boolean;
  MsgText: string;
begin
  Result     := '';
  NeedsRestart := False;

  NeedsDotNet := not DotNetRuntimeInstalled;
  NeedsPython := not PythonAvailable;

  // Notify user what we're about to install
  if NeedsDotNet or NeedsPython then
  begin
    MsgText := 'The installer detected missing dependencies and will now install them automatically:' + #13#10 + #13#10;
    if NeedsDotNet then
      MsgText := MsgText + '  ✔  Microsoft .NET 6.0 Desktop Runtime' + #13#10;
    if NeedsPython then
      MsgText := MsgText + '  ✔  Python 3.12 (with pip)' + #13#10;
    MsgText := MsgText + #13#10 + 'This may take a few minutes. Please wait...';
    MsgBox(MsgText, mbInformation, MB_OK);
  end;

  // Install .NET 6 if missing
  if NeedsDotNet then
  begin
    if not InstallDotNetRuntime then
    begin
      Result := '.NET 6 Desktop Runtime installation FAILED. Please install it manually from: https://dotnet.microsoft.com/download/dotnet/6.0';
      Exit;
    end;
  end;

  // Install Python if missing
  if NeedsPython then
  begin
    if not InstallPython then
    begin
      Result := 'Python 3 installation FAILED. Please install it manually from: https://www.python.org/downloads/';
      Exit;
    end;
  end;
end;

// ─── After Install: run pip install yt-dlp ───────────────────────────────────
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    // yt-dlp.exe is already bundled in {app}, so this just ensures
    // the Python package is also available for the script.
    if PythonAvailable then
      InstallYtDlpViaPip;
  end;
end;

// ─── Uninstall page: ask before removing settings ────────────────────────────
function InitializeUninstall: Boolean;
begin
  Result := MsgBox(
    'Are you sure you want to uninstall EagleVStream v2.1 PRO?' + #13#10 + #13#10 +
    'Your settings and download history in AppData will NOT be deleted.',
    mbConfirmation, MB_YESNO) = IDYES;
end;

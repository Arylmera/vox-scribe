; Murmur — Windows installer.
;
; Build:  dotnet publish src\Murmur.App -c Release -r win-x64 --self-contained true `
;                        -p:PublishSingleFile=true -o publish
;         iscc installer\murmur.iss
;
; Per-user on purpose: no UAC prompt, and the app already keeps everything per-user
; (settings, transcripts and the speech model live under %LOCALAPPDATA%\Murmur). With
; PrivilegesRequired=lowest, {autopf} resolves to %LOCALAPPDATA%\Programs.

; The product is Vox-Scribe; the executable and the %LOCALAPPDATA%\Murmur data directory
; keep their upstream names so settings, transcripts and the model survive the rebrand.
#define MyAppName "Vox-Scribe"
#define MyAppVersion "0.1.0"
#define MyAppExeName "Murmur.App.exe"

; Where the published build lives, relative to this script. CI overrides it:
;   ISCC.exe installer\murmur.iss /DPublishDir=..\artifacts\publish
#ifndef PublishDir
  #define PublishDir "..\publish"
#endif

[Setup]
; Never change this AppId: it is how upgrades find the existing install.
AppId={{7E2F60A1-58D2-4C8E-9B7B-6D1F2A0C4E55}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=Murmur
DefaultDirName={autopf}\{#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=Output
OutputBaseFilename=VoxScribe-Setup-{#MyAppVersion}
SetupIconFile=..\src\Murmur.App\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "autostart"; Description: "Start {#MyAppName} when Windows starts"; GroupDescription: "Startup:"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Debug symbols and XML doc files stay out of the install.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb,*.xml"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; The tray-resident hotkey app is only useful if it is actually running, so autostart is
; offered as a checked task. HKCU Run — matches the per-user install.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
    ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"""; \
    Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; \
    Flags: nowait postinstall skipifsilent

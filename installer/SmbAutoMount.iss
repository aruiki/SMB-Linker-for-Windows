; Inno Setup 6 script for SmbAutoMount
; Build:  ISCC.exe /DAppVersion=1.0.0 /DArch=x64 installer\SmbAutoMount.iss
; (build.ps1 publishes the app first and passes these values)

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef Arch
  #define Arch "x64"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish\win-" + Arch
#endif

#define AppName "SmbAutoMount"
#define AppExe "SmbAutoMount.exe"
#define AppUrl "https://github.com/aruiki/SmbAutoMount"

[Setup]
AppId={{6B0F5B8E-3C1D-4E3A-9B71-5A2E0C4D7F21}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=SmbAutoMount contributors
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
; Per-user install: no administrator rights, no UAC prompt.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
OutputDir=..\dist
OutputBaseFilename={#AppName}-Setup-{#AppVersion}-{#Arch}
SetupIconFile=..\src\SmbAutoMount\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
LicenseFile=..\LICENSE
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
MinVersion=10.0.17763
#if Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
japanese.StartupTask=Windows のサインイン時に SmbAutoMount を開始する（推奨）
english.StartupTask=Start SmbAutoMount when I sign in to Windows (recommended)
japanese.DesktopTask=デスクトップにショートカットを作成する
english.DesktopTask=Create a desktop shortcut
japanese.LaunchApp=SmbAutoMount を起動する
english.LaunchApp=Launch SmbAutoMount

[Tasks]
Name: "startup"; Description: "{cm:StartupTask}"
Name: "desktopicon"; Description: "{cm:DesktopTask}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Same value the app writes from its "Start with Windows" switch.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "SmbAutoMount"; \
    ValueData: """{app}\{#AppExe}"" --background"; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Disconnect drives and remove saved passwords / Explorer customisations created by the app.
Filename: "{app}\{#AppExe}"; Parameters: "--cleanup"; Flags: waituntilterminated runhidden; RunOnceId: "Cleanup"

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\SmbAutoMount"

[Code]
procedure StopApp();
var
  Code: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM {#AppExe} /F', '', SW_HIDE, ewWaitUntilTerminated, Code);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopApp();
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  StopApp();
  Result := True;
end;

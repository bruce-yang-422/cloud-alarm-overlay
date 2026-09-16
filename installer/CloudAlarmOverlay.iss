; Run installer/build-installer.ps1 to publish and compile with Inno Setup 7 or 6.
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppName "Cloud Alarm Overlay"
#define AppExeName "CloudAlarmOverlay.App.exe"

[Setup]
AppId={{7B51AC48-37D4-47B2-A792-AB073811E64D}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} v{#AppVersion}
DefaultDirName={localappdata}\Programs\CloudAlarmOverlay
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\artifacts\installer
OutputBaseFilename=CloudAlarmOverlay-v{#AppVersion}-Setup-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
UninstallDisplayIcon={app}\{#AppExeName}
VersionInfoDescription=Cloud Alarm Overlay Installer
VersionInfoProductName={#AppName}
VersionInfoVersion={#AppVersion}

[Tasks]
Name: "desktopicon"; Description: "建立桌面捷徑"; GroupDescription: "附加捷徑："; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
; The WPF tray application starts in the interactive session after this user signs in.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "CloudAlarmOverlay"; ValueData: """{app}\{#AppExeName}"""; Flags: uninsdeletevalue; Check: ShouldRegisterAutoStart

[Run]
Filename: "{app}\{#AppExeName}"; Description: "啟動 Cloud Alarm Overlay"; Flags: nowait postinstall skipifsilent
; Uninstall deliberately retains the user's AppData database.

[Code]
function ShouldRegisterAutoStart: Boolean;
var
  Enabled: Cardinal;
begin
  Result := not RegQueryDWordValue(HKEY_CURRENT_USER, 'Software\CloudAlarmOverlay', 'AutoStartEnabled', Enabled) or (Enabled <> 0);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'CloudAlarmOverlay');
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\CloudAlarmOverlay', 'AutoStartEnabled');
    RegDeleteKeyIfEmpty(HKEY_CURRENT_USER, 'Software\CloudAlarmOverlay');
  end;
end;

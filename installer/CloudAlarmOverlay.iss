; Run installer/build-installer.ps1 to publish and compile with Inno Setup 7 or 6.
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif
#ifndef AppVersion
  #define AppVersion "1.2.0"
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
SetupIconFile=..\src\CloudAlarmOverlay.App\Assets\Brand\cloud_alarm_app.ico
DisableWelcomePage=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; PrepareToInstall terminates only this installation's process without a password
; or Restart Manager's close-applications confirmation page.
CloseApplications=no
RestartApplications=no
UninstallDisplayIcon={app}\{#AppExeName}
VersionInfoDescription=Cloud Alarm Overlay Installer
VersionInfoProductName={#AppName}
VersionInfoVersion={#AppVersion}

[Languages]
Name: "chinesetraditional"; MessagesFile: "compiler:Languages\ChineseTraditional.isl"

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
#include "CloseInstalledApp.iss"

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := CloseInstalledApp(ExpandConstant('{app}\{#AppExeName}'));
end;

procedure InitializeWizard();
var
  AboutPage: TWizardPage;
  Heading, Description: TNewStaticText;
begin
  AboutPage := CreateCustomPage(wpWelcome, '認識 Cloud Alarm Overlay', '雲端鬧鐘警示・安裝前說明');

  Heading := TNewStaticText.Create(AboutPage);
  Heading.Parent := AboutPage.Surface;
  Heading.Left := ScaleX(8);
  Heading.Top := ScaleY(16);
  Heading.AutoSize := False;
  Heading.WordWrap := True;
  Heading.Width := AboutPage.SurfaceWidth - ScaleX(16);
  Heading.Font.Style := [fsBold];
  Heading.Font.Size := 14;
  Heading.Caption := 'Cloud Alarm Overlay｜雲端鬧鐘警示';
  Heading.AdjustHeight;

  Description := TNewStaticText.Create(AboutPage);
  Description.Parent := AboutPage.Surface;
  Description.Left := ScaleX(8);
  Description.Top := Heading.Top + Heading.Height + ScaleY(20);
  Description.AutoSize := False;
  Description.WordWrap := True;
  Description.Width := AboutPage.SurfaceWidth - ScaleX(16);
  Description.Caption :=
    '這是一套 Windows 提醒工具，可同步公司 Google Sheets 的任務，也能建立本機提醒。' + #13#10#13#10 +
    '提供一般提醒、重要提醒、緊急提醒與強制通知，並可查看歷史紀錄及使用番茄鐘。' + #13#10#13#10 +
    '首次開啟時，請依公司提供的名單填入裝置代碼與顯示名稱；同步來源由管理者設定。' + #13#10#13#10 +
    '關閉主視窗後，程式仍會留在右下角系統匣持續提醒。' + #13#10#13#10 +
    '按下「安裝」後，安裝程式會直接強制關閉執行中的舊版，不需輸入結束密碼。請先儲存正在編輯的內容。安裝完成後可勾選啟動程式，既有任務與設定會保留。';
  Description.AdjustHeight;
end;

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

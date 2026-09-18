# Cloud Alarm Overlay 常用 CLI 指令

以下指令以 **Windows PowerShell** 為例，請先切換到專案根目錄：

```powershell
Set-Location 'D:\Projects\csharp_dev\cloud_alarm_overlay'
```

## 1. 確認開發環境

專案由 `global.json` 鎖定 .NET SDK 10.0.401：

```powershell
dotnet --version
dotnet --info
```

`dotnet --version` 應顯示 `10.0.401`。

若 PowerShell 不允許執行本機腳本，可只對目前終端機暫時開放：

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
```

關閉這個 PowerShell 視窗後，這項暫時設定就會失效。

## 2. 第一次下載套件

```powershell
dotnet restore CloudAlarmOverlay.sln --locked-mode
```

套件版本由各專案的 `packages.lock.json` 鎖定。只有更新套件版本時才應重新產生 lock file。

## 3. 建置程式

Debug 建置：

```powershell
dotnet build CloudAlarmOverlay.sln --no-restore
```

Release 建置：

```powershell
dotnet build CloudAlarmOverlay.sln -c Release --no-restore
```

如果出現 DLL 被 `CloudAlarmOverlay.App` 占用，請先從 Windows 系統匣右鍵程式圖示，選擇「結束程式」，再重新建置。

查看程式是否仍在執行：

```powershell
Get-Process CloudAlarmOverlay.App -ErrorAction SilentlyContinue
```

## 4. 執行程式

建置後執行：

```powershell
dotnet run --project src/CloudAlarmOverlay.App --no-restore
```

略過重複建置、直接執行現有 Debug 輸出：

```powershell
dotnet run --project src/CloudAlarmOverlay.App --no-build
```

關閉主視窗只會縮到系統匣。需要停止程式時，請從系統匣右鍵選擇「結束程式」。

## 5. 執行測試

執行全部測試：

```powershell
dotnet test CloudAlarmOverlay.sln --no-restore
```

只執行資料層測試：

```powershell
dotnet test tests/CloudAlarmOverlay.Data.Tests/CloudAlarmOverlay.Data.Tests.csproj --no-restore
```

只執行 WPF／應用程式測試：

```powershell
dotnet test tests/CloudAlarmOverlay.App.Tests/CloudAlarmOverlay.App.Tests.csproj --no-restore
```

列出所有 App 測試名稱：

```powershell
dotnet test tests/CloudAlarmOverlay.App.Tests/CloudAlarmOverlay.App.Tests.csproj --no-restore --list-tests
```

若目前執行中的程式鎖住一般建置輸出，可將測試輸出隔離到 `artifacts/test-build`：

```powershell
dotnet test CloudAlarmOverlay.sln --no-restore `
  -p:BaseOutputPath="$PWD/artifacts/test-build/"
```

`artifacts/test-build` 是可重新產生的測試中繼資料，測試完成後不必交付。

## 6. 產生免安裝版本

v1.0.0 已發布且有電腦安裝；以下一版 `1.2.0` 為例。每次正式更新都必須使用比 `version.json` 目前版本更高的新版本號，已發布的 GitHub Release 標籤與安裝檔不可覆蓋。若資料庫結構需要變更，新增遷移版本，不要改寫已安裝版本使用的 `V1.sql`。

建立 win-x64 self-contained 發布資料夾：

```powershell
.\scripts\publish.ps1 -Version 1.2.0
```

輸出位置：

```text
artifacts\publish\
```

這是完整免安裝版，必須整個資料夾一起交付，不能只複製其中的主程式 EXE。

## 7. 建立 Inno Setup 安裝 EXE

自動執行發布、尋找 Inno Setup 7／6，並建立安裝包：

```powershell
.\installer\build-installer.ps1 -Version 1.2.0
```

輸出位置：

```text
artifacts\installer\CloudAlarmOverlay-v1.2.0-Setup-x64.exe
```

如果 `artifacts\publish` 已是同一版本，可略過重新發布：

```powershell
.\installer\build-installer.ps1 -Version 1.2.0 -SkipPublish
```

如果自動搜尋不到 Inno Setup，可指定 ISCC：

```powershell
.\installer\build-installer.ps1 -Version 1.2.0 `
  -Iscc 'C:\Program Files\Inno Setup 7\ISCC.exe'
```

打包腳本會拒絕小於或等於 `version.json` 已發布版本的號碼，也會檢查發布資料夾中的程式版本，避免 `-SkipPublish` 混用舊檔。先完成打包與上傳，驗證安裝檔後再更新 `version.json`。

本機若使用 Inno Setup 7 遇到 `EndUpdateResource failed (110)`，此次已驗證可使用 `artifacts/tools/InnoSetup6` 內的可攜式 Inno Setup 6.7.3（含繁體中文語系檔）完成封裝：

```powershell
.\installer\build-installer.ps1 -Version 1.2.0 `
  -Iscc '.\artifacts\tools\InnoSetup6\ISCC.exe'
```

此工具目錄只保留在本機，不納入 Git；其他電腦請指定自己安裝的編譯器路徑。

例如：

```powershell
.\installer\build-installer.ps1 -Version 1.2.0
```

## 8. 核對安裝包

查看檔案大小與版本：

```powershell
$installer = Get-Item '.\artifacts\installer\CloudAlarmOverlay-v1.2.0-Setup-x64.exe'
$version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($installer.FullName)
$installer | Select-Object FullName, Length, LastWriteTime
$version | Select-Object FileVersion, ProductVersion
```

計算 SHA-256：

```powershell
Get-FileHash `
  '.\artifacts\installer\CloudAlarmOverlay-v1.2.0-Setup-x64.exe' `
  -Algorithm SHA256
```

啟動安裝程式：

```powershell
Start-Process '.\artifacts\installer\CloudAlarmOverlay-v1.2.0-Setup-x64.exe'
```

### 發布 GitHub 版本資訊

在 GitHub Releases 建立新標籤（例如 `v1.2.0`），上傳 `artifacts/installer/` 中對應的 EXE，不覆蓋 `v1.0.0`。驗證下載檔的 SHA-256 後，更新專案根目錄的 `version.json`：`latestVersion`、`downloadUrl`、`sha256` 與發布說明都要對應這次的新版本。

```powershell
Get-Content .\version.json | ConvertFrom-Json | Select-Object latestVersion, downloadUrl
Get-FileHash .\artifacts\installer\CloudAlarmOverlay-v1.2.0-Setup-x64.exe -Algorithm SHA256
```

將 `version.json` 提交並推送到 `main` 後，程式使用的更新資訊網址不變：

```text
https://raw.githubusercontent.com/bruce-yang-422/cloud-alarm-overlay/main/version.json
```

## 9. 查看本機資料與日誌

開啟程式資料目錄：

```powershell
explorer.exe "$env:APPDATA\CloudAlarmOverlay"
```

查看今天最新 100 行執行日誌：

```powershell
Get-Content "$env:APPDATA\CloudAlarmOverlay\logs\runtime-$(Get-Date -Format yyyyMMdd).log" `
  -Tail 100
```

程式內的「重置所有資料與設定」會在管理者確認後，於下次啟動清除此資料目錄。不要在程式執行中手動刪除資料庫。

## 10. 常用完整流程

日常開發驗證：

```powershell
dotnet restore CloudAlarmOverlay.sln --locked-mode
dotnet build CloudAlarmOverlay.sln --no-restore
dotnet test CloudAlarmOverlay.sln --no-build --no-restore
dotnet run --project src/CloudAlarmOverlay.App --no-build
```

正式封裝：

```powershell
dotnet test CloudAlarmOverlay.sln --no-restore
.\installer\build-installer.ps1 -Version 1.2.0
Get-FileHash `
  '.\artifacts\installer\CloudAlarmOverlay-v1.2.0-Setup-x64.exe' `
  -Algorithm SHA256
```

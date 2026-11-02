# Cloud Alarm Overlay 常用 CLI 指令

以下指令以 Windows PowerShell 為例，從專案根目錄執行。一般 .NET 開發不需要 Node.js；只有修改任務產生器資源或執行其瀏覽器測試時才需要。

```powershell
Set-Location 'D:\Projects\csharp_dev\cloud_alarm_overlay'
```

## 1. 確認環境與版本

```powershell
dotnet --version
dotnet --info
Get-Content .\global.json
[xml]$projectProps = Get-Content .\Directory.Build.props
$projectProps.Project.PropertyGroup.Version
Get-Content .\version.json -Raw | ConvertFrom-Json |
  Select-Object latestVersion, downloadUrl, sha256
```

目前 `global.json` 固定使用 **.NET SDK 10.0.403**，不允許自動切換其他版本。專案版本由 `Directory.Build.props` 決定；`version.json` 記錄已公開提供下載的版本，兩者在準備下一版時可以不同。

WPF 建置與桌面測試須在 Windows 執行；安裝檔封裝另需本機 Inno Setup 7 或 6。

若 PowerShell 阻擋本機腳本，可只對目前終端機暫時開放：

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
```

關閉該終端機後，這項暫時設定即失效。

## 2. 還原、建置與執行

首次下載或日常還原使用鎖定模式：

```powershell
dotnet restore CloudAlarmOverlay.sln --locked-mode
dotnet build CloudAlarmOverlay.sln --no-restore
dotnet run --project src/CloudAlarmOverlay.App --no-build --no-restore
```

Release 建置與執行：

```powershell
dotnet build CloudAlarmOverlay.sln -c Release --no-restore
dotnet run --project src/CloudAlarmOverlay.App -c Release --no-build --no-restore
```

刻意調整套件或專案版本後，可執行一般還原以更新 `packages.lock.json`，再檢查差異；不應手動修改套件完整性雜湊。

```powershell
dotnet restore CloudAlarmOverlay.sln
git diff -- ':(glob)**/packages.lock.json'
```

若出現 DLL 被占用，請先從 Windows 系統匣結束程式。關閉主視窗只會縮至系統匣。

```powershell
Get-Process CloudAlarmOverlay.App -ErrorAction SilentlyContinue
```

Google 登入建置設定見[貢獻指南](../CONTRIBUTING.md#開發環境)。本機 OAuth 檔案為 `google-desktop-oauth.local.json`，不得提交憑證或授權資料。

## 3. 執行測試

先完成上一節的 Release 建置，再執行全部測試：

```powershell
dotnet test CloudAlarmOverlay.sln -c Release --no-build --no-restore
```

只執行資料層或桌面程式測試：

```powershell
dotnet test tests/CloudAlarmOverlay.Data.Tests/CloudAlarmOverlay.Data.Tests.csproj -c Release --no-build --no-restore
dotnet test tests/CloudAlarmOverlay.App.Tests/CloudAlarmOverlay.App.Tests.csproj -c Release --no-build --no-restore
```

列出測試，或只執行管理設定 JSON 相關案例：

```powershell
dotnet test tests/CloudAlarmOverlay.App.Tests/CloudAlarmOverlay.App.Tests.csproj -c Release --no-build --no-restore --list-tests
dotnet test tests/CloudAlarmOverlay.Data.Tests/CloudAlarmOverlay.Data.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~Admin_json'
```

保留 TRX 結果：

```powershell
dotnet test CloudAlarmOverlay.sln -c Release --no-build --no-restore `
  --logger 'trx;LogFilePrefix=release' --results-directory artifacts/test-results
```

若要隔離測試輸出，還原與測試使用同一個 `BaseOutputPath`：

```powershell
$testOutput = Join-Path (Get-Location) 'artifacts/test-build/'
dotnet restore CloudAlarmOverlay.sln -p:BaseOutputPath="$testOutput" --locked-mode
dotnet test CloudAlarmOverlay.sln -c Release --no-restore -p:BaseOutputPath="$testOutput"
```

部分 Google 登入與任務產生器主機測試會啟動本機 HTTP 監聽器。受限執行環境若禁止監聽，須在允許本機回呼的環境驗證；遇到失敗時先查看錯誤原因，不應直接將整批測試視為通過。

## 4. 任務產生器資源與瀏覽器測試

HTML 位於根目錄 `task_builder_tailwind.html`，隨附 CSS、字型與 JavaScript 位於 `task-builder-assets/`。

```powershell
Push-Location .\tools\task-builder
try {
  npm ci --ignore-scripts --no-audit --no-fund
  node build.cjs
  node --test offline.test.cjs
}
finally {
  Pop-Location
}
```

需安裝 Node.js／npm 與 Microsoft Edge。測試以無介面瀏覽器驗證 HTTP 與直接開啟 HTML、離線操作、提醒等級限制、重複規則及 CSV／Excel 匯出。

修改 HTML 的 Tailwind class 後，須重新產生並提交 `task-builder-assets/tailwind.css`；一般 .NET 建置不會自動產生 CSS。修改後也須重新封裝，已安裝的程式才會取得新版頁面。詳細資源版本見 [任務產生器資源說明](../task-builder-assets/README.md)。

## 5. 準備下一個版本

目前已發布 **v1.4.5**。以下以尚未發布的 **1.4.6** 示範下一版流程；執行前請先確認遠端發布狀態，並將範例改成實際準備發布的版本。已發布標籤及安裝檔不得覆寫。

```powershell
$releaseVersion = '1.4.6'
$releasedVersion = (Get-Content .\version.json -Raw | ConvertFrom-Json).latestVersion
if ([version]$releaseVersion -le [version]$releasedVersion) {
  throw '封裝版本必須高於已發布版本。'
}
```

將 `Directory.Build.props` 的 `<Version>` 改為相同版本，新增對應的 `docs/v<版本>發布說明.md`，並更新 README。發布說明寫使用者可見的新增、改善、修正與升級須知，不需放入內部測試紀錄或本機路徑。

```powershell
[xml]$projectProps = Get-Content .\Directory.Build.props
if ([version]$projectProps.Project.PropertyGroup.Version -ne [version]$releaseVersion) {
  throw '請先更新 Directory.Build.props 的版本。'
}
dotnet restore CloudAlarmOverlay.sln
dotnet build CloudAlarmOverlay.sln -c Release --no-restore
dotnet test CloudAlarmOverlay.sln -c Release --no-build --no-restore
```

每個必要步驟成功後才繼續。資料庫結構若需變更，新增遷移，不改寫已發布版本使用的遷移檔。

## 6. 發布資料夾與安裝檔

以下沿用上一節的 `$releaseVersion`。

只產生 Windows x64 自包含發布資料夾：

```powershell
.\scripts\publish.ps1 -Version $releaseVersion
```

輸出為 `artifacts/publish/`，包含 .NET 執行階段。整個資料夾須一起配送，不能只複製主程式 EXE；執行此腳本不會產生安裝檔。

自動發布並建立 Inno Setup 安裝檔：

```powershell
.\installer\build-installer.ps1 -Version $releaseVersion
```

如果剛才的發布資料夾已是相同版本，可省略重新發布：

```powershell
.\installer\build-installer.ps1 -Version $releaseVersion -SkipPublish
```

找不到編譯器時指定路徑：

```powershell
.\installer\build-installer.ps1 -Version $releaseVersion `
  -Iscc 'C:\Program Files\Inno Setup 7\ISCC.exe'
```

腳本會拒絕小於或等於 `version.json` 的版本，並檢查發布主程式的檔案版本。預設流程產生 Release／win-x64 安裝包；`-SkipPublish` 只檢查版號，不能證明內容是否為最新來源，修改程式或資源後應重新發布。

## 7. 檢查產物與建立校驗檔

```powershell
$installerPath = Join-Path (Get-Location) "artifacts/installer/CloudAlarmOverlay-v$releaseVersion-Setup-x64.exe"
$installerItem = Get-Item -LiteralPath $installerPath
$installerItem | Select-Object Name, Length, LastWriteTime
$installerItem.VersionInfo | Select-Object FileVersion, ProductVersion
(Get-Item artifacts/publish/CloudAlarmOverlay.App.exe).VersionInfo |
  Select-Object FileVersion, ProductVersion

$installerHash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
"$installerHash  $($installerItem.Name)" |
  Set-Content -LiteralPath "$installerPath.sha256" -Encoding utf8
Get-Content -LiteralPath "$installerPath.sha256"
```

封裝腳本會顯示 SHA-256，**不會自動建立 `.sha256` 檔**；上面的指令用來產生交付用校驗檔。另須在測試電腦驗證安裝、升級與啟動；編譯成功不代表已完成安裝驗收。

## 8. 發布 GitHub Release 與更新 version.json

1. 提交並推送要發布的原始碼與文件，在 GitHub Releases 選擇對應提交及新標籤 `v<版本>`。
2. 將 `docs/v<版本>發布說明.md` 內容貼入發布說明，上傳對應的安裝 EXE 與 `.sha256` 檔。
3. 發布後，下載公開安裝檔並核對 SHA-256。
4. 核對成功後，才更新 `version.json` 的 `latestVersion`、`downloadUrl`、`sha256`、`releaseNote`，提交並推送至 `main`。
5. 同一次提交更新 `index.html` 的版本號：頁面標題、社群分享標題、結構化資料的版本與連結、首屏標籤；若重新擷取截圖，也更新截圖說明的版本。

列出 `index.html` 目前標示的版本，確認沒有遺漏：

```powershell
Select-String -Path .\index.html -Pattern '\d+\.\d+\.\d+' -AllMatches |
  ForEach-Object { $_.Matches.Value } | Group-Object | Select-Object Name, Count
```

公開下載驗證範例（沿用上一節變數）：

```powershell
$downloadUrl = "https://github.com/bruce-yang-422/cloud-alarm-overlay/releases/download/v$releaseVersion/CloudAlarmOverlay-v$releaseVersion-Setup-x64.exe"
New-Item -ItemType Directory -Force -Path artifacts/release-verification | Out-Null
$downloadPath = Join-Path (Get-Location) "artifacts/release-verification/CloudAlarmOverlay-v$releaseVersion-Setup-x64.exe"
Invoke-WebRequest -Uri $downloadUrl -OutFile $downloadPath
$downloadHash = (Get-FileHash -LiteralPath $downloadPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($downloadHash -ne $installerHash) { throw '公開下載檔與本機安裝檔的 SHA-256 不一致。' }
```

`releaseNote` 使用適合程式內閱讀的改版重點、升級提醒及完整 Release 網址；JSON 換行以 `\n` 表示，不加入註解。更新後確認四個欄位與已發布版本一致：

```powershell
Get-Content .\version.json -Raw | ConvertFrom-Json |
  Select-Object latestVersion, downloadUrl, sha256, releaseNote
git diff --check
git diff -- version.json
```

程式的更新來源網址維持不變：

```text
https://raw.githubusercontent.com/bruce-yang-422/cloud-alarm-overlay/main/version.json
```

尚未上傳產物時，`version.json` 必須保留目前公開版本，避免使用者收到無法下載的更新通知。

## 9. 管理員設定 JSON

檢查範本是否為有效 JSON：

```powershell
Get-Content .\cao-settings-template.json -Raw | ConvertFrom-Json
```

這只檢查 JSON 語法；欄位、數值及鎖定規則仍須由程式驗證。實際操作在「管理者專區 → 備份與移轉 → 管理設定 JSON」：先「匯入並預覽…」，確認後才套用。完整格式見 [管理員 JSON 設定匯入與匯出](管理員JSON設定匯入.md)。

`cao-settings.local.json` 為本機實際設定，含連線資訊，已被 Git 忽略；請勿將密碼或 OAuth 憑證加入設定 JSON。

## 10. 查看本機資料與日誌

```powershell
explorer.exe "$env:APPDATA\CloudAlarmOverlay"
Get-Content "$env:APPDATA\CloudAlarmOverlay\logs\runtime-$(Get-Date -Format yyyyMMdd).log" -Tail 100
```

資料庫為 `%AppData%\CloudAlarmOverlay\cloud_alarm_overlay.db`。執行日誌預設保留 30 天，可設為 1–365 天；沒有當日日誌時，先確認程式是否已啟動。

備份、還原與重置請使用程式介面。不要在程式執行時手動刪除或覆蓋資料庫；Google 授權不包含在 `.calbak` 備份中，移轉後需重新登入。

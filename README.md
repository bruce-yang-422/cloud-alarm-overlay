<img src="src/CloudAlarmOverlay.App/Assets/Brand/cloud_alarm_icon.png" alt="Cloud Alarm Overlay Logo" width="120">

# Cloud Alarm Overlay

[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![Windows](https://img.shields.io/badge/platform-Windows-0078D4?logo=windows&logoColor=white)](#系統需求)
[![WPF](https://img.shields.io/badge/UI-WPF-5C2D91)](https://learn.microsoft.com/dotnet/desktop/wpf/)
[![MIT License](https://img.shields.io/github/license/bruce-yang-422/cloud-alarm-overlay?color=green)](LICENSE)
[![Last commit](https://img.shields.io/github/last-commit/bruce-yang-422/cloud-alarm-overlay)](https://github.com/bruce-yang-422/cloud-alarm-overlay/commits/main/)

Cloud Alarm Overlay 是常駐 Windows 系統匣的桌面提醒程式。你可以安排本機任務，也可以從公開的 Google Sheets CSV 同步公司或團隊任務。提醒、簽收與操作紀錄儲存在這台電腦。

## 主要功能

- **任務提醒**：建立一次性或重複的本機任務，支援工作日、假日略過與農曆日期規則。
- **分級通知**：依提醒等級顯示不同的通知效果；最高級通知需要輸入畫面上的數字確認碼。
- **Google Sheets 同步**：讀取 Sheet A 的任務、假日、人員與農曆對照，以及 Sheet B 的團隊任務。雲端任務在程式內唯讀，可複製成本機任務再修改。
- **番茄鐘**：專注與休息計時、音效設定，以及每日進度和歷史紀錄。
- **查詢與匯出**：查詢任務提醒、簽收與番茄鐘紀錄，並匯出 CSV。
- **管理者專區**：設定同步來源、通知政策、開機啟動與結束程式密碼保護，查看系統／稽核紀錄，執行備份與還原。
- **系統匣常駐**：關閉主視窗後仍持續執行提醒；從系統匣選單可重新開啟或結束程式。

## 系統需求

- 執行環境：Windows。專案提供 **win-x64** 安裝包建置腳本。
- 從原始碼執行：安裝 [.NET SDK 10.0.401](https://dotnet.microsoft.com/download/dotnet/10.0)；版本由 [global.json](global.json) 固定。
- 自行製作安裝包：另外安裝 Inno Setup 6 或 7。安裝包採 self-contained 發布，使用者電腦不需要 .NET SDK。

## 下載與安裝

從 [GitHub Releases](https://github.com/bruce-yang-422/cloud-alarm-overlay/releases/latest) 下載安裝檔；目前的 Windows x64 安裝檔為 [CloudAlarmOverlay-v1.0.0-Setup-x64.exe](https://github.com/bruce-yang-422/cloud-alarm-overlay/releases/download/v1.0.0/CloudAlarmOverlay-v1.0.0-Setup-x64.exe)。安裝後從開始功能表啟動程式。

## 快速開始

在 Windows PowerShell 執行：

```powershell
git clone https://github.com/bruce-yang-422/cloud-alarm-overlay.git
Set-Location cloud-alarm-overlay
dotnet restore CloudAlarmOverlay.sln --locked-mode
dotnet run --project src/CloudAlarmOverlay.App
```

首次啟動時，設定這台電腦的裝置代碼與顯示名稱。之後可從首頁新增任務，或在「我的任務」編輯、複製和啟用／停用本機任務。「歷史紀錄」可查詢提醒及簽收結果。

新資料庫會建立預設管理者帳號 `admin`、密碼 `12345`。請登入「管理者專區」，在「備份與還原」變更密碼。管理者登入閒置 15 分鐘後會自動失效。

關閉主視窗只會縮到系統匣。要停止提醒，請在系統匣圖示按右鍵，選擇「結束程式」；若已開啟密碼保護，需先完成驗證。

## Google Sheets 同步

程式透過 **公開 CSV 連結**讀取 Google Sheets，不需要 Google Sheets API、OAuth 或本機金鑰。首次啟動不會自動連線，也不會匯入範例任務。

1. 依 [Sheet 範例](Sheet範例/) 建立兩份試算表：Sheet A 包含 `Tasks`、`Holidays`、`Employees`、`LunarCalendar`；Sheet B 包含 `Tasks`。
2. 確認要同步的工作表可由公開 CSV 連結讀取，取得各試算表的 Spreadsheet ID 與分頁 GID。
3. 登入「管理者專區」→「同步來源」，填入 ID／GID，儲存並同步。Sheet A 與 Sheet B 可個別測試連線。

同步間隔可設為 30–60 秒，預設 45 秒。網路中斷時，已快取的雲端任務仍可排程；本機任務不受影響。程式**不會將任務、簽收或其他本機資料回寫到 Google Sheets**。

## 軟體更新

管理者登入後，到「設定」→「外觀與資料」，將「公開 version.json 連結」設為以下網址，再按「儲存更新來源」：

```text
https://raw.githubusercontent.com/bruce-yang-422/cloud-alarm-overlay/main/version.json
```

程式會讀取 GitHub 上的 [version.json](version.json) 比對版本；有新版時，「開啟下載頁」會前往對應的 GitHub Release 安裝檔。更新資訊與安裝檔都由 GitHub 提供。

## 本機資料與備份

程式使用 SQLite 儲存資料，預設路徑：

```text
%AppData%\CloudAlarmOverlay\cloud_alarm_overlay.db
%AppData%\CloudAlarmOverlay\logs\runtime-yyyyMMdd.log
```

「管理者專區」→「備份與還原」可建立或匯入 `.calbak` 備份；「設定」→「外觀與資料」可切換主題、檢查更新或重置本機資料。匯入備份後請重新啟動程式。

## 開發與封裝

```powershell
dotnet build CloudAlarmOverlay.sln
dotnet test CloudAlarmOverlay.sln --no-build
.\scripts\publish.ps1 -Version 1.0.0
.\installer\build-installer.ps1 -Version 1.0.0
```

發布檔輸出至 `artifacts/publish/`，安裝包輸出至 `artifacts/installer/`。若建置時 DLL 被占用，先從系統匣結束正在執行的程式。更多指令見 [常用 CLI 指令](常用CLI指令.md)。

| 路徑 | 用途 |
| --- | --- |
| `src/CloudAlarmOverlay.App` | WPF 介面、通知視窗與系統匣 |
| `src/CloudAlarmOverlay.Core` | 任務模型、排程與 CSV 解析 |
| `src/CloudAlarmOverlay.Data` | SQLite 資料與備份還原 |
| `src/CloudAlarmOverlay.BackgroundServices` | 同步與提醒背景工作 |
| `src/CloudAlarmOverlay.Infrastructure` | HTTP、路徑與系統服務 |
| `tests/` | 自動化測試 |
| `Sheet範例/` | Google Sheets CSV 範例 |

## 授權

本專案採用 [MIT License](LICENSE)。內建 Emoji 素材的原始授權與著作權聲明見 [Emoji LICENSE](src/CloudAlarmOverlay.App/Assets/Emoji/LICENSE.txt)。

# 貢獻指南

## 回報問題

請先搜尋 [Issues](https://github.com/bruce-yang-422/cloud-alarm-overlay/issues)，確認問題或需求尚未有人回報。錯誤回報請提供程式版本、Windows 版本、重現步驟、預期結果與實際結果，並移除日誌或截圖中的私人資料。

## 開發環境

| 項目 | 需求 |
| --- | --- |
| 原始碼建置 | .NET 10 SDK；指定版本見 [global.json](global.json)。 |
| 安裝套件建置 | Inno Setup 6 或 7。 |

```powershell
git clone https://github.com/bruce-yang-422/cloud-alarm-overlay.git
Set-Location cloud-alarm-overlay
dotnet restore CloudAlarmOverlay.sln --locked-mode
dotnet run --project src/CloudAlarmOverlay.App
```

Google 登入建置須提供桌面 OAuth 設定；本機檔案名稱為 `google-desktop-oauth.local.json`，應包含桌面用戶端的 `installed` 設定並保留於 Git 忽略清單。

## 測試

```powershell
dotnet build CloudAlarmOverlay.sln -c Release --no-restore
dotnet test CloudAlarmOverlay.sln -c Release --no-build --no-restore
```

WPF 測試須在 Windows 執行。若 DLL 被占用，請先從系統匣結束相關程式。其他測試範圍見[常用 CLI 指令](docs/常用CLI指令.md#3-執行測試)。

## 專案結構

| 路徑 | 用途 |
| --- | --- |
| `src/CloudAlarmOverlay.App/` | WPF 介面、通知視窗與系統匣。 |
| `src/CloudAlarmOverlay.Core/` | 任務模型、排程、重複規則與 CSV 解析。 |
| `src/CloudAlarmOverlay.Data/` | SQLite、資料庫遷移及備份還原。 |
| `src/CloudAlarmOverlay.BackgroundServices/` | 同步與提醒背景工作。 |
| `src/CloudAlarmOverlay.Infrastructure/` | 網路、檔案路徑與系統服務。 |
| `tests/` | 資料層及 WPF 自動化測試。 |
| `installer/` | Inno Setup 腳本與封裝工具。 |
| `tools/task-builder/` | 任務產生器資源建置與離線測試。 |
| `data/calendar/` | 內建農曆與假日資料。 |
| `docs/` | 使用手冊、技術文件及發布說明。 |
| `site-assets/screenshots/` | 網站與 README 使用的程式實際截圖，包含亮色與暗色展示圖。 |
| `Sheet範例/` | Google Sheets 公開 CSV 範例。 |

## 提交 Pull Request

請說明變更目的、使用者可見行為及驗證結果；功能變更應更新相關文件，並加入適當的回歸測試。請遵循現有程式風格，避免提交建置產物、個人資料、服務帳戶私鑰或本機連線設定。

## 封裝與發布

封裝、校驗檔與發布流程見[常用 CLI 指令](docs/常用CLI指令.md#5-準備下一個版本)。

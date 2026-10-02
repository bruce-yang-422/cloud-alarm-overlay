<img src="src/CloudAlarmOverlay.App/Assets/Brand/cloud_alarm_icon.png" alt="Cloud Alarm Overlay Logo" width="120">

# Cloud Alarm Overlay

[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![Windows](https://img.shields.io/badge/platform-Windows-0078D4?logo=windows&logoColor=white)](#系統需求)
[![WPF](https://img.shields.io/badge/UI-WPF-5C2D91)](https://learn.microsoft.com/dotnet/desktop/wpf/)
[![MIT License](https://img.shields.io/github/license/bruce-yang-422/cloud-alarm-overlay?color=green)](LICENSE)
[![Last commit](https://img.shields.io/github/last-commit/bruce-yang-422/cloud-alarm-overlay)](https://github.com/bruce-yang-422/cloud-alarm-overlay/commits/main/)
[![Release](https://img.shields.io/github/v/release/bruce-yang-422/cloud-alarm-overlay)](https://github.com/bruce-yang-422/cloud-alarm-overlay/releases/latest)

Cloud Alarm Overlay 是常駐 Windows 系統匣的桌面提醒程式，把個人任務、團隊通知、倒數計時、番茄鐘與健康提醒放在同一個地方。任務可以在本機建立，也可以從 Google Sheets 同步公司或團隊的指派；提醒、確認與操作紀錄都儲存在這台電腦。

**[下載 v1.4.1 Windows 版](https://github.com/bruce-yang-422/cloud-alarm-overlay/releases/download/v1.4.1/CloudAlarmOverlay-v1.4.1-Setup-x64.exe)** · [程式介紹](https://remind.stack-base.com/) · [版本紀錄](https://github.com/bruce-yang-422/cloud-alarm-overlay/releases)

## 主要功能

### 任務與提醒

- **本機任務**：建立一次性或重複任務，支援每天、工作日、每週複選星期、每月指定日期與月份、每年國曆／農曆日期，並可略過假日。重複任務依規則計算下一次提醒。
- **任務月曆**：「我的任務」可切換清單或月曆。日期格顯示農曆、假日與節氣，跨日活動以橫條跨越多天；右側「當日工作」列出當天活動與日常任務，雙擊即可查看、編輯或複製。
- **四種提醒等級**：一般、重要、緊急與強制通知各有不同呈現方式（見[下表](#四種提醒模式)）。忙碌時可「稍後提醒」延後 5～30 分鐘。
- **任務預覽與匯入匯出**：雙擊任務名稱查看詳細內容，備註支援 Markdown 與 emoji。本機任務可用 CSV 範本匯入、匯出，匯入前先預覽。
- **歷史紀錄**：查詢提醒、確認與番茄鐘紀錄，可篩選並匯出 CSV。

### 計時與健康

- **倒數／正數**：記錄考試、旅行、紀念日等目標日期，或累計某件事開始了多久。支援重複與提醒，可釘選到首頁，並分享為 1:1 或 2:3 圖片。
- **番茄鐘**：專注與休息計時、音效設定，以及每日進度與歷史統計。
- **健康工具**：喝水、久坐、螢幕休息與伸展四種提醒，可設定間隔、時段與工作日，並配合番茄鐘休息自動對齊或延後。預設關閉。詳見[健康工具](docs/健康工具.md)。

### 首頁資訊

- **今日概況**：顯示下一個提醒、今日進度、首頁釘選（2～5 張）與需要處理的問題。
- **天氣與警特報**：選擇所在縣市與鄉鎮後，顯示目前溫度、降雨與明日預報，並列出生效中的氣象署警特報與地震報告。天氣資料來自 [Open-Meteo](https://open-meteo.com/en/docs)（CC BY 4.0，免費 API 限非商業用途）；警特報說明見[氣象署警特報](docs/氣象署警特報.md)。

### 同步與管理

- **Google Sheets 同步**：讀取公開試算表的團隊任務、假日、人員與農曆資料。雲端任務在程式內唯讀，可複製成本機任務再修改。
- **Google 帳號整合**：登入 Google 帳號後，可同步私人 Sheets 任務與 Google 日曆。此功能尚未對所有使用者開放，詳見 [Google 帳號與同步設定](docs/Google帳號與同步設定.md)。
- **任務產生器**：以表單選擇通知對象，產生 Sheet 資料列或匯出 Excel，方便管理者派發任務。
- **管理者專區**：設定共用試算表、使用者政策、密碼保護、開機啟動與更新來源，查看系統與稽核紀錄，並執行備份與還原。
- **外觀**：淺色、暗色或跟隨系統，搭配預設、櫻花粉、若竹綠、薰衣草紫、夕陽橘、極簡銀白六種色彩風格。
- **系統匣常駐**：關閉主視窗後仍持續提醒，從系統匣圖示可重新開啟或結束程式。

### 四種提醒模式

| 模式 | 呈現與確認方式 |
| --- | --- |
| 一般提醒 | 右下角小視窗，10 秒後自動關閉；滑鼠移入會暫停倒數，也可手動確認。 |
| 重要提醒 | 右下角置頂視窗，閱讀後按「我已閱讀，確認」。 |
| 緊急提醒 | 畫面中央的大型置頂視窗，閱讀後按鈕確認。管理者可關閉緊急提醒的延後功能。 |
| 強制通知 | 全螢幕置頂通知，須輸入畫面上的 9 位數字確認碼；由公司或系統雲端任務指派。 |

## 系統需求

- **作業系統**：Windows x64。安裝包已包含執行所需的 .NET 元件，不需另外安裝。
- **網路**：Google 同步、天氣、警特報與更新檢查需要網路。沒有網路時本機任務照常提醒，任務產生器也能離線使用。
- **開發**：[.NET SDK 10.0.401](https://dotnet.microsoft.com/download/dotnet/10.0)（由 [global.json](global.json) 固定）；製作安裝包另需 Inno Setup 6 或 7。

## 下載、安裝與升級

目前版本為 **v1.4.1**：[下載 CloudAlarmOverlay-v1.4.1-Setup-x64.exe](https://github.com/bruce-yang-422/cloud-alarm-overlay/releases/download/v1.4.1/CloudAlarmOverlay-v1.4.1-Setup-x64.exe)。安裝檔大小為 61,704,497 bytes，SHA-256：

```text
dde5070d4b210c17b44cde75b991cf9a9af4510056cc68ac789fa38c44899ec3
```

執行安裝檔，依繁體中文安裝精靈完成設定，再從開始功能表啟動程式。Windows SmartScreen 出現警告時的處理方式見[安裝教學](docs/安裝教學.md)。

- **升級**：任何舊版都可直接覆蓋安裝，不必先移除或逐版安裝。首次啟動會自動升級本機資料庫，保留任務、設定與歷史紀錄。建議先建立備份並儲存正在編輯的內容。
- **關閉舊程式**：按下「安裝」後，安裝器會自動結束正在執行的舊版，不需要輸入結束密碼；無法關閉時會停止安裝並說明原因。
- **版本紀錄**：各版本的新功能與修正見 [GitHub Releases](https://github.com/bruce-yang-422/cloud-alarm-overlay/releases)。

## 第一次使用

1. **選擇使用方式**：首次啟動時選擇「個人使用」或「公司使用」。個人使用會自動建立裝置代碼，只需確認顯示名稱；公司使用請填入管理者提供的裝置代碼與顯示名稱，才能收到指派給你的任務。也可以直接匯入 `.calbak` 備份檔。
2. **建立任務**：在「我的任務」新增提醒，設定內容、時間與重複規則。
3. **調整外觀**：到「設定 → 外觀與視窗」選擇明暗模式與色彩風格；「首頁與天氣」可設定所在地區。
4. **連接團隊任務**（選用）：由管理者設定 Google Sheets 同步來源，見下方說明。

兩種使用方式的差異：

| | 個人使用 | 公司使用 |
| --- | --- | --- |
| 管理者專區 | 直接開放，不需密碼 | 需輸入管理者帳密，登入 10 分鐘後自動登出 |
| 結束程式 | 預設不需密碼，可另設專用結束密碼 | 沿用公司密碼保護 |

公司使用的預設管理者帳號為 `admin`、密碼 `12345`，請盡快到「管理者專區 → 安全與存取」變更。使用方式只能在首次設定時選擇；匯入設定或備份不會改變。

關閉主視窗只會縮到系統匣。要停止提醒，請在系統匣圖示按右鍵選「結束程式」。

## Google Sheets 同步

程式透過**公開 CSV 連結**讀取 Google Sheets，不需要 API 金鑰。首次啟動不會自動連線，也不會匯入範例任務。

1. 依 [Sheet 範例](Sheet範例/) 建立兩份試算表：Sheet A 包含 `Tasks`、`Holidays`、`Employees`、`LunarCalendar`；Sheet B 包含 `Tasks`。
2. 確認工作表可由公開連結讀取，記下各試算表的 Spreadsheet ID 與分頁 GID。
3. 到「管理者專區 → 共用試算表」填入 ID 與 GID，測試連線後儲存並同步。

同步間隔可設為 30～60 秒（預設 45 秒）。網路中斷時，已快取的雲端任務仍會照常提醒。程式**不會把任務、確認紀錄或任何本機資料寫回 Google Sheets**。

`Recurrence` 欄位的每月規則：`Monthly:10` 代表每月 10 日；`Monthly:10:1,3,5,7,9,11` 代表奇數月的 10 日。不存在的日期會略過該月；規則含逗號時，CSV 中須以雙引號包住。

內建的農曆與假日資料涵蓋 2026–2027 年，安裝後即可使用；也可在「管理者專區 → 農曆與假日」開啟 GitHub 自動更新。格式說明見[日曆 JSON 說明](data/calendar/README.md)。

## 任務產生器

從側邊欄底部或系統匣選單開啟「任務產生器」，程式會用預設瀏覽器開啟本機表單：

1. 填寫提醒內容、時間、等級與重複規則。
2. 依部門、職稱或人員選擇通知對象與排除對象。
3. 預覽後複製資料列或匯出 Excel，再貼到 Google Sheets。

表單隨安裝包提供，不需安裝 Python 或 Node.js，也**不會自動修改試算表**。已設定 Sheet B 時，會一併開啟對應的 Tasks 分頁。

## 軟體更新

管理者到「管理者專區 → 啟動與更新」，將更新網址設為：

```text
https://raw.githubusercontent.com/bruce-yang-422/cloud-alarm-overlay/main/version.json
```

設定後，程式啟動時會比對 [version.json](version.json)，有新版時跳出通知。所有使用者都能在「設定 → 軟體更新」查看更新說明並下載安裝檔。

## 本機資料與備份

資料以 SQLite 儲存在：

```text
%AppData%\CloudAlarmOverlay\cloud_alarm_overlay.db
%AppData%\CloudAlarmOverlay\logs\runtime-yyyyMMdd.log
```

- **備份與還原**：「管理者專區 → 備份與移轉」可建立或匯入 `.calbak` 備份，匯入後請重新啟動程式。Google 授權不會包含在備份中，換電腦後需重新登入。
- **設定匯入匯出**：管理者可將設定匯出為 JSON 套用到其他電腦，見[管理員 JSON 設定匯入](docs/管理員JSON設定匯入.md)。
- **執行日誌**：預設保留 30 天，可在管理者專區調整為 1～365 天。
- **重置**：「管理者專區 → 清理與重置」可清除所有本機資料並回到首次設定。

## 開發與封裝

### 從原始碼執行

```powershell
git clone https://github.com/bruce-yang-422/cloud-alarm-overlay.git
Set-Location cloud-alarm-overlay
dotnet restore CloudAlarmOverlay.sln --locked-mode
dotnet run --project src/CloudAlarmOverlay.App
```

### 建置與測試

```powershell
dotnet build CloudAlarmOverlay.sln
dotnet test CloudAlarmOverlay.sln --no-build
```

### 製作安裝包

先在 [Directory.Build.props](Directory.Build.props) 提高版本號，再執行：

```powershell
.\installer\build-installer.ps1 -Version 1.4.2
```

腳本會先以 self-contained 方式發布，再用 Inno Setup 封裝；版本號必須高於 `version.json` 中已發布的版本。需要指定編譯器時加上 `-Iscc 'ISCC.exe 的完整路徑'`。發布檔輸出至 `artifacts/publish/`，安裝包輸出至 `artifacts/installer/`。

發布流程：上傳 GitHub Release → 下載核對 SHA-256 → 更新 `version.json`。請勿覆蓋已發布的安裝檔。

任務產生器的表單位於根目錄 `task_builder_tailwind.html`，本機 API 位於 `TaskBuilderHostService.cs`；修改後需重新封裝才會生效。若建置時 DLL 被占用，請先從系統匣結束程式。更多指令見[常用 CLI 指令](docs/常用CLI指令.md)。

### 專案結構

| 路徑 | 用途 |
| --- | --- |
| `src/CloudAlarmOverlay.App` | WPF 介面、通知視窗與系統匣（程式進入點） |
| `src/CloudAlarmOverlay.Core` | 任務模型、排程、重複規則與 CSV 解析 |
| `src/CloudAlarmOverlay.Data` | SQLite 資料、資料庫遷移與備份還原 |
| `src/CloudAlarmOverlay.BackgroundServices` | 同步與提醒背景工作 |
| `src/CloudAlarmOverlay.Infrastructure` | HTTP、路徑與系統服務 |
| `tests/` | 自動化測試（資料層與 WPF） |
| `installer/` | Inno Setup 安裝腳本與打包腳本 |
| `scripts/` | 發布與維護腳本 |
| `data/calendar/` | 內建農曆與假日 JSON |
| `docs/` | 使用說明、安裝教學與問題追蹤 |
| `Sheet範例/` | Google Sheets CSV 範例 |

## 授權

本專案採用 [MIT License](LICENSE)。內建 Emoji 素材的授權與著作權聲明見 [Emoji LICENSE](src/CloudAlarmOverlay.App/Assets/Emoji/LICENSE.txt)。

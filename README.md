# Cloud Alarm Overlay

C# / WPF / MVVM / .NET 10。已完成 **Milestone 2**：在本機提醒與公開 CSV 同步基礎上，加入管理者登入、兩層權限、設定鎖定、稽核紀錄及 Employees 通知例外。

驗收與測試結果請見 [驗收清單.md](驗收清單.md)，常用建置與封裝命令見 [常用 CLI 指令](常用CLI指令.md)。

## 啟動與預覽

需要 Windows 與 .NET SDK **10.0.401**（由 [global.json](global.json) 鎖定）。Visual Studio 安裝「.NET 桌面開發」工作負載，開啟 [CloudAlarmOverlay.sln](CloudAlarmOverlay.sln)，將 CloudAlarmOverlay.App 設為啟始專案。

```powershell
dotnet restore CloudAlarmOverlay.sln --locked-mode
dotnet build CloudAlarmOverlay.sln
dotnet test CloudAlarmOverlay.sln --no-build
dotnet run --project src/CloudAlarmOverlay.App
```

Python 的 `.venv` 不影響此 WPF 程式，不必先關閉。若舊程式仍在執行，先從系統匣結束，避免建置時檔案被鎖定。

1. 首次啟動：填入 **IT 提供的裝置代碼**與顯示名稱，建立本機識別。
2. 首頁：按「新增任務」，設定幾分鐘後的時間並儲存；到期後會顯示通知。
3. 「通知效果預覽」可以直接測試四種通知，不會寫入簽收紀錄。最高級需輸入畫面上的確認碼。
4. 我的任務：選取列後可編輯、複製、啟用／停用或刪除；雲端任務顯示鎖頭，只能複製成本機任務。
5. 歷史紀錄：查詢實際提醒與簽收結果，也可匯出 CSV。
6. 關閉主視窗後仍會在系統匣執行；雙擊系統匣圖示重新開啟，右鍵「結束程式」才會停止提醒。

本機可選低級、中級、高級；依主規劃書第 18 節，最高級任務由 IT 在雲端指派。最高級的「效果預覽」仍可使用。

## 公開試算表同步

初始來源留空，不會自動連線或建立範例任務。需要雲端同步時：

- 由「管理者登入」驗證後，前往「同步來源」，填入 Sheet A / B 的 Spreadsheet ID 與分頁 GID，再按「儲存並同步」。
- CSV 欄位解析見 [CsvSheetParser.cs](src/CloudAlarmOverlay.Core/Services/CsvSheetParser.cs)；版本庫僅保留 `SheetA_Tasks.csv`、`SheetA_Employees.csv`、`SheetA_Holidays.csv`、`SheetA_LunarCalendar.csv` 與 `SheetB_Tasks.csv` 五份範例，正式資料及本機連線設定由 IT 另行管理。
- Sheet A 包含 Tasks、Holidays、Employees、LunarCalendar 四個分頁；Sheet B 包含 Tasks。
- 同步間隔可設 30–60 秒，預設 45 秒。只使用公開 CSV 的 HTTP GET，不使用 Google Sheets API、OAuth 或本機金鑰。
- 第 1 列為欄名，第 2 列固定為中文說明，第 3 列起為資料。分頁解析失敗會保留原快取；首頁和管理者系統紀錄可查看原因。
- 來源停用或網路中斷後，已快取任務仍可排程；本機任務不受同步影響。
- Employees 用於姓名／裝置／部門對象過濾，並依姓名優先、裝置代碼其次套用等級上限與 RequireAckOverride。最高級仍需確認碼。

## 本機資料

```text
%AppData%\CloudAlarmOverlay\cloud_alarm_overlay.db
%AppData%\CloudAlarmOverlay\logs\runtime-yyyyMMdd.log
```

SQLite 會自動由 V1／V2 升級至 V3，保留已有資料。V3 增加系統事件表與稽核索引。沒有簽收資料上傳或雲端回寫。

## 專案分工

| 專案 | 職責 |
| --- | --- |
| App | WPF 頁面、ViewModel、通知視窗、Tray、DI 組裝、啟停 |
| Core | Model、介面、本機任務驗證、CSV 解析、對象與重複規則 |
| Data | SQLite 版本升級、Repository、提醒與簽收狀態 |
| BackgroundServices | 資料庫初始化、SyncWorker、AlarmWorker |
| Infrastructure | AppData 路徑、公開 CSV HTTP 用戶端 |

尚未建立管理者的資料庫會在啟動時建立預設帳號 `admin`／密碼 `12345`（以 PBKDF2 雜湊保存）；已建立的管理者帳密不會覆蓋。15 分鐘無程式內操作會自動登出。閃爍與靜音可鎖定，音效不可鎖定。

番茄鐘已補上：側欄「番茄鐘」可開始專注、暫停、休息與設定音效；「歷史紀錄 → 番茄鐘」可查詢與匯出。

備份還原、主題切換、更新檢查與資料重置皆已提供。Milestone 3 未實作。

詳細驗證與待辦事項見 [驗收清單.md](驗收清單.md)。

UI 外觀以目前 WPF 實作為準，操作以鍵盤及一般滑鼠優先。

通知配色由程式依等級呈現，不提供使用者外觀設定。四級通知音效採左右兩欄，可獨立開關及試聽。
任務的補充說明與備註可跨欄展開，兩項內容都會顯示於通知。

任務歷史支援名稱、日期、來源、結果篩選，包含摘要統計、15 筆分頁及完整篩選結果 CSV 匯出。

## 本機維護（2026-09-16）
「設定 → 外觀與資料」提供主題、視窗行為、更新檢查及資料重置；管理員專區「備份與還原」提供 .calbak 備份／還原及變更管理者密碼。首次設定亦可匯入備份。
還原需完成目前通知並結束番茄鐘；還原後重新啟動，讓全部身分及計時設定重新載入。
預設 admin／12345 保留；可在管理員專區「備份與還原」驗證舊密碼並更換密碼。
更新連結由管理者填入公開 HTTPS version.json，未提供正式連結前不會宣稱已檢查遠端版本。
發布：執行 `scripts/publish.ps1` 產出 `artifacts/publish`；執行 `installer/build-installer.ps1` 可自動使用 Inno Setup 7／6 產生版本化安裝 EXE。

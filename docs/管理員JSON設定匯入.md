# 管理員 JSON 設定匯入與匯出

本文件依目前原始碼的管理者介面與 JSON 驗證規則撰寫。JSON 用於移轉共用試算表來源、通知政策、更新網址及日誌保留設定；任務與歷史資料請使用 `.calbak` 備份。

## 匯入設定

1. 開啟「管理者專區」；公司使用模式須先登入管理者。
2. 前往「備份與移轉 → 管理設定移轉 → 管理設定 JSON」，按「下載 JSON 範本」。也可使用儲存庫根目錄的 [cao-settings-template.json](../cao-settings-template.json)。
3. 編輯範本，將 Sheet ID 與各分頁 GID 換成實際值；不需要變更的設定群組請整組省略。
4. 按「匯入並預覽…」，選取檔案。程式會先驗證，再列出將套用的設定項目。
5. 確認預覽後按「套用設定」。取消預覽不會寫入；套用成功後無須再次按儲存。
6. 若變更試算表來源，前往「共用試算表」測試連線，並按「立即同步」確認資料。

預覽列出設定項目名稱，並非逐欄的新舊值比較。未提供的群組維持原值；驗證或套用失敗時整份設定不會部分儲存。

若同步來源已鎖定，包含 `SyncOptions` 的檔案必須先在「共用試算表」手動解鎖。即使 JSON 同時包含 `SyncLinksLocked: false`，仍不能繞過鎖定。預覽期間若管理者登入逾時，須重新登入再操作。

## 下載範本與匯出已儲存設定

| 操作 | 內容 | 預設檔名 |
| --- | --- | --- |
| 下載 JSON 範本 | 範例 Sheet ID／GID 與預設政策，包含官方更新來源。 | `cao-settings-template.json` |
| 匯出已儲存設定 | 本機已儲存的設定值與鎖定狀態。 | `cao-settings.local.json` |

在「備份與移轉」按「匯出已儲存設定」，選擇儲存位置。畫面上尚未儲存的修改不包含在內；選擇位置期間若登入逾時或登出，程式不會寫出檔案。

匯出範圍為 Sheet A／B、同步間隔、更新網址、來源鎖定、緊急提醒延後政策、閃爍間隔、靜音時段及執行日誌保留天數。不包含任務、管理者帳密、結束密碼、裝置身分、Google OAuth 憑證或天氣地點。

匯出檔可再次匯入。目標電腦若已鎖定同步來源，仍須先解鎖。

## 格式與欄位

使用 UTF-8 JSON，可含 BOM；檔案上限為 **64 KiB（65,536 bytes，含 BOM）**。欄位名稱區分大小寫，`FormatVersion` 必須為數字 `1`，並至少包含一個可套用的設定項目。布林值使用 `true`／`false`，不可寫成字串。

| 欄位 | 格式與作用 |
| --- | --- |
| `FormatVersion` | 必填，目前固定為 `1`。 |
| `RuntimeLogRetentionDays` | 整數 1–365，預設 30，只控制 runtime 執行日誌。 |
| `SyncOptions` | 若提供此群組，必須完整提供下表八個欄位；省略整組則保留原來源。 |
| `SyncLinksLocked` | 布林值，設定同步來源是否鎖定；不能解除本次匯入 `SyncOptions` 的前置鎖定檢查。 |
| `UpdateManifestUrl` | 公開 HTTPS 更新資訊網址，不可包含帳密；空字串清除更新來源。這是 `version.json` 網址，不是安裝檔下載網址。 |
| `AllowUrgentSnooze` | 布林值，是否允許緊急提醒稍後提醒；匯入後作為管理者政策鎖定。 |
| `FlashMilliseconds`、`LockFlash` | 必須一起提供。間隔為整數 200–5000 毫秒；鎖定值為布林值。 |
| `QuietPeriods`、`LockQuiet` | 必須一起提供。最多 12 組 `{ "Start": "HH:mm", "End": "HH:mm" }`；開始與結束不可相同，可跨午夜。空陣列清除靜音時段；鎖定值為布林值。 |

`LockFlash`、`LockQuiet` 控制對應設定是否可由一般使用者修改。靜音時段不會抑制最高級的強制通知。

### SyncOptions

| 欄位 | 格式與作用 |
| --- | --- |
| `SheetAId` | Sheet A 的 Spreadsheet ID，僅填 ID，不填完整網址；空字串停用此來源。 |
| `TasksAGid` | Sheet A 任務分頁 GID，以數字字串表示，例如 `"0"`。 |
| `HolidaysGid` | Sheet A 假日分頁 GID，以數字字串表示。 |
| `EmployeesGid` | Sheet A 員工分頁 GID，以數字字串表示。 |
| `LunarGid` | Sheet A 農曆分頁 GID，以數字字串表示。 |
| `SheetBId` | Sheet B 的 Spreadsheet ID；空字串停用此來源。 |
| `TasksBGid` | Sheet B 任務分頁 GID，以數字字串表示。 |
| `IntervalSeconds` | 整數 30–60，預設 45 秒。 |

啟用 Sheet A 時，四個分頁 GID 都必須填寫；啟用 Sheet B 時，`TasksBGid` 必須填寫。停用來源時仍須保留完整八欄，可將該來源的 ID 與 GID 設為空字串。

### 舊版天氣欄位

舊檔中的 `WeatherDefaultDistrictCode`、`WeatherDefaultLocation` 仍可被辨識，但目前版本**不套用、不匯出**這些欄位。天氣地點請由使用者在設定頁調整；只有舊版天氣欄位的檔案不含可匯入項目，會被拒絕。

## 範例

只更新官方更新來源，不變更共用試算表或通知政策：

```json
{
  "FormatVersion": 1,
  "UpdateManifestUrl": "https://raw.githubusercontent.com/bruce-yang-422/cloud-alarm-overlay/main/version.json"
}
```

設定午休靜音與跨午夜靜音，並保留 30 天執行日誌：

```json
{
  "FormatVersion": 1,
  "RuntimeLogRetentionDays": 30,
  "QuietPeriods": [
    { "Start": "12:00", "End": "13:00" },
    { "Start": "22:00", "End": "07:00" }
  ],
  "LockQuiet": true
}
```

## 常見問題與檔案保管

- **不接受未知欄位、重複欄位或 null**：不修改的設定請省略，勿用 `null` 代替。陣列內也不能包含 `null`。JSON 不接受註解或尾隨逗號。
- **配對欄位缺漏**：閃爍間隔與 `LockFlash`、靜音時段與 `LockQuiet` 必須成對；`SyncOptions` 必須完整八欄。
- **無法同步私人試算表**：此 JSON 設定的是共用 CSV 同步來源，不會建立 Google 帳號授權；私人 Sheets／Google 日曆請在 Google 帳號設定中連接。
- **不能匯入密碼或任務資料**：JSON 不支援管理者帳密、結束密碼、裝置身分、使用模式或任務清單；完整本機資料移轉請用 `.calbak` 備份，Google 授權需另外登入。
- **連線資訊為明文**：透過受控管道配送。`cao-settings.local.json` 已由 `.gitignore` 排除；改名或移至其他儲存庫後須重新確認忽略規則。
- **稽核不記錄完整連線值**：新寫入的共用來源與更新網址稽核以「已設定」等標記代替實際值；既有紀錄不會自動清除。程式不複製匯入檔，也不刪除來源檔案。

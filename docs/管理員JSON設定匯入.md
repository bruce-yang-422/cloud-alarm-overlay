# 管理員 JSON 設定匯入

此功能已加入原始碼，尚未重新封裝至現有 v1.3.0 安裝檔。

1. 開啟「管理者專區」，輸入管理者帳號及密碼。
2. 在「同步來源」按「下載 JSON 範本」。範本不包含這台電腦的設定或密碼。
3. 編輯範本，將 Sheet ID 與各分頁 GID 換成公司的實際值；不需要的設定群組可刪除。
4. 按「匯入 JSON 設定」選取檔案。驗證成功即儲存，不需逐欄輸入或再按儲存。
5. 可按「測試 Sheet A／B」確認連線，或「立即同步」。背景同步也會在後續輪詢使用已儲存設定。

若同步來源已鎖定，含 SyncOptions 的檔案必須先手動解鎖才能匯入；JSON 不能繞過此限制。登入逾時須重新登入。

## 設定格式

採 UTF-8 JSON（可含 BOM），最大 64 KB；欄位名稱區分大小寫。FormatVersion 必須為 1。

| 欄位 | 說明 |
| --- | --- |
| SyncOptions | 完整八欄：SheetAId、TasksAGid、HolidaysGid、EmployeesGid、LunarGid、SheetBId、TasksBGid、IntervalSeconds。ID 為純 ID，GID 為字串；來源 ID 空字串代表停用，間隔 30–60 秒。 |
| SyncLinksLocked | true／false，儲存同步來源後是否鎖定。 |
| UpdateManifestUrl | HTTPS 更新資訊網址；空字串清除。 |
| AllowUrgentSnooze | 是否允許緊急提醒稍後提醒。 |
| FlashMilliseconds、LockFlash | 必須一起提供；閃爍 200–5000 毫秒及是否鎖定。 |
| QuietPeriods、LockQuiet | 必須一起提供；例如 `[{"Start":"12:00","End":"13:00"}]`，空陣列清除靜音時段。 |
| WeatherDefaultDistrictCode | 內建行政區代碼，例如板橋區 `65000010`；完整代碼見 Core/Data/TaiwanWeatherLocations.json 的 Code 欄位。 |

未提供的設定群組保留原值。未知欄位、重複欄位、null、無效格式或值會拒絕整份檔案，不部分套用。不支援管理者帳密、結束密碼、裝置身分或任務資料。

只修改更新來源的例子：

```json
{
  "FormatVersion": 1,
  "UpdateManifestUrl": "https://example.com/version.json"
}
```

## 檔案保管

JSON 是明文，請透過公司受控管道配送；程式不複製匯入檔、不自動刪除來源檔。匯入的 Sheet ID 與更新網址不寫入新增的管理設定稽核內容，舊紀錄不會自動清除。本機設定仍沿用既有資料庫儲存方式，此功能不提供加密或改變 Google Sheet 存取權限。

# 任務產生器本機資源

此目錄隨應用程式建置／發佈及安裝檔一起配送，瀏覽器不需向 CDN 下載資源。

| 資源 | 固定版本 | 授權 |
| --- | --- | --- |
| Tailwind CSS | 3.4.17 | MIT，見 tailwind-LICENSE.txt |
| Lucide | 0.468.0 | ISC，見 lucide-LICENSE.txt |
| SheetJS / xlsx | 0.18.5 | Apache-2.0，見 xlsx-LICENSE.txt |
| Noto Sans TC Variable | @fontsource-variable 5.1.1 | OFL，見 noto-sans-tc/LICENSE.txt |
| Plus Jakarta Sans Variable | @fontsource-variable 5.1.1 | OFL，見 plus-jakarta-sans/LICENSE.txt |
| JetBrains Mono Variable | @fontsource-variable 5.1.1 | OFL，見 jetbrains-mono/LICENSE.txt |

來源：npm 套件（下載位置與完整性雜湊記錄於 `tools/task-builder/package-lock.json`）。
SheetJS 沿用原頁面的版本，產品僅使用匯出功能，沒有讀取外部 Excel 檔案。

## 更新資源

在 `tools/task-builder` 執行 `npm ci --ignore-scripts`，再執行 `node build.cjs`。
修改 HTML 的 Tailwind class 後也須重新產生 CSS，並提交產生的資源；一般 .NET 建置不需 Node.js 或 npm 連線。
字型保留套件的 Unicode 分片，瀏覽器依文字從本機載入需要的字型檔。

執行 `node --test offline.test.cjs` 驗證離線操作（需本機 Microsoft Edge）。
測試阻擋外部網路，模擬 Sheet 失敗，驗證 HTTP 與直接開啟 HTML、手動新增、字型、圖示及 Excel／CSV 匯出。

Google Sheet 資料仍需網路；讀取失敗時可手動輸入，無法核對尚未載入的雲端重複編號。

## 員工建立權限與重複規則

任務產生器只提供一般、重要與緊急提醒，不提供管理者登入或建立強制通知的入口；強制通知由管理者透過公司管理流程設定。表單與 CSV／Excel／剪貼簿匯出均檢查草稿的提醒等級。雲端既有強制通知仍可唯讀預覽，不會加入草稿匯出。`RequireAck` 只控制手動確認，不代表強制通知等級。

| 選項 | 匯出的 Recurrence |
| --- | --- |
| 不重複 | `None` |
| 每天 | `Daily` |
| 每個工作日 | `Weekly:1,2,3,4,5` |
| 每週 | `Weekly:1,4,7`，1–7 代表週一至週日 |
| 每月 | `Monthly:10`；指定月份則為 `Monthly:10:1,3,5,7,9,11` |
| 農曆每月日期 | `LunarDay:1,15` |

以上格式與程式的 `RecurrenceRule` 一致；`ONCE`、`WORKDAY` 等舊式 CSV 別名會轉成對應的正式格式。程式另支援 `LunarDate:3:23:Both` 等每年指定農曆月日規則，產生器尚無對應選項；遇到這類草稿時會拒絕編輯並保留原始資料，避免轉成不同排程。

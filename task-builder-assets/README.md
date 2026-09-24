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

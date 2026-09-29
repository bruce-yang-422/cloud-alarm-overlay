# Google 帳號與同步設定（v1.4.0 本機版）

程式支援公司與個人 Google 帳號同時登入，讀取私人 Google Sheets 任務與帳號自己的 Google 日曆；Sheet 任務可選擇開啟線上編輯。既有公開 Sheet A／B 設定保留在同一頁的「公開 Sheet A／B」分頁。

## 一般使用者登入

使用已內建 OAuth 設定的版本，進入「管理者專區 → 同步來源 → Google 帳號與來源 → 總覽」，按「使用 Google 登入」，在 Google 官方瀏覽器頁面選擇自己的帳號並授權。個人與公司帳號可分別新增；使用者不需要建立 Google Cloud 專案或下載 JSON。

若顯示「此版本尚未設定 Google 登入」，代表軟體提供者尚未將用戶端設定納入建置，不是使用者帳號有問題。

## 開發者：一次建立並內建 Google OAuth 設定

1. 前往 [Google Cloud Console](https://console.cloud.google.com/)，建立或選取由你管理的專案。
2. 在「API 和服務 → 程式庫」啟用 **Google Sheets API** 與 **Google Calendar API**。
3. 在 **Google Auth Platform** 設定品牌資訊、支援信箱與目標對象。需要公司和個人帳號一起使用時，須採允許這些帳號的對象設定；僅限組織內部的應用程式不能讓組織外的 Gmail 帳號登入。
4. 若目前為外部 **Testing** 狀態，把預計使用的公司與個人帳號加入測試使用者。公司 Workspace 可能需要管理員允許此應用程式。
5. 建立 OAuth 用戶端，應用程式類型選 **桌面應用程式**，下載 JSON。
6. 將下載的桌面 OAuth JSON 放到專案根目錄，命名為 `google-desktop-oauth.local.json`。此檔已加入 Git 忽略清單，建置時會作為資源內建到 Infrastructure 組件，安裝後不需再匯入。也可透過 MSBuild 屬性 `GoogleDesktopOAuthPath` 指定完整檔案路徑。
7. 重新建置／封裝程式，再以測試使用者實際登入驗收。供大眾使用前，完成 External 正式發布、品牌／隱私權資訊及適用的敏感權限驗證；內建設定不會自動完成 Google 審核。

桌面 OAuth 用戶端用於識別應用程式，可隨桌面程式散布；桌面端無法保密 client secret，流程仍使用系統瀏覽器、PKCE 與本機回呼。使用者的 access／refresh token 不會包進程式。不要將服務帳戶私鑰或網頁後端用戶端設定放進此檔。

必須是含 `installed` 的桌面用戶端 JSON；服務帳戶金鑰及網頁用戶端不適用。程式固定使用 Google 的授權端點，不採用 JSON 裡自訂的登入網址。詳細流程見 [Google 桌面 OAuth 文件](https://developers.google.com/identity/protocols/oauth2/native-app)。

目前尚未取得開發者的真實桌面 OAuth 用戶端；原始碼支援內建，但缺少上述檔案的建置不會憑空取得 Google 登入能力。既有服務帳戶金鑰不會用於此登入。

## 公開發布的品牌網址

GitHub Pages 作為產品網站，對應專案根目錄的 `index.html`、`privacy.html`、`terms.html`。頁面須先提交並部署成功，確認未登入 Google／GitHub 的瀏覽器也能開啟，再填入 Google Auth Platform → 品牌：

| 欄位 | 網址 |
| --- | --- |
| 應用程式首頁 | https://remind.stack-base.com/ |
| 應用程式隱私權政策連結 | https://remind.stack-base.com/privacy.html |
| 應用程式服務條款連結 | https://remind.stack-base.com/terms.html |
| 授權網域 | `stack-base.com`（主網域，不加協定或路徑） |

在 Google Search Console 使用相同專案管理帳號新增 `stack-base.com` 的「網域」資源，將 Google 提供的 TXT 驗證記錄加入 Cloudflare DNS，完成網域所有權驗證。驗證值須使用 Google 實際產生的內容，不能自行編造。Pages 上線、網站所有權驗證與 OAuth 品牌／敏感權限驗證是不同步驟，設定網址不代表審核已通過。若 Google 驗證中心要求更廣的網域驗證範圍，須依其實際要求完成。

政策頁依目前實作描述本機憑證加密、SQLite 快取、備份及刪除限制。Google 共享日曆尚未實作，不列為已提供功能。功能或資料處理方式變更時，應一併更新政策與首頁。

## 進階：組織自訂 OAuth

需要使用組織自己的 Google Cloud 專案時，展開總覽底部「進階設定：組織自訂 OAuth」，按「匯入桌面 OAuth 設定」。可按「恢復程式預設」回到內建設定；切換前須先登出所有 Google 帳號。舊版匯入的設定仍會保留，更新程式不會替換既有憑證所綁定的用戶端。

## 多帳號登入與自動續用

1. 填入「公司」或「個人」等帳號標籤。
2. 選擇讀取 Sheets、自己的日曆，或兩者。需要 Sheet 寫回時，再勾選「允許 Sheets 編輯」。
3. 按「使用 Google 登入」，在系統瀏覽器選擇帳號並授權。
4. 回到程式後可再新增其他帳號；帳號會同時保持連線，不必來回登出。

**憑證有效就自動續用**：短效存取憑證到期時，程式在背景用更新憑證取得新的存取憑證；重新啟動程式也能續用。網路逾時保留憑證並延後重試，授權確實失效才顯示「需要重新登入」。各帳號獨立處理，本機管理員的 10 分鐘期限不影響背景同步。

Google 的外部 Testing 專案在使用 Sheets／日曆權限時，更新憑證通常 **7 天到期**；正式使用前需完成適用的發布設定與驗證。使用者撤銷授權、長期未使用、限時授權到期或公司政策限制也會影響續用，不能視為永久授權。[Google 憑證期限規則](https://developers.google.com/identity/protocols/oauth2#expiration)

授權需求如下；這些權限範圍由 Google 定義，程式內選定來源不會把 OAuth scope 自動縮小成單一檔案。

| 用途 | 權限 |
| --- | --- |
| 識別登入帳號 | `openid email` |
| 讀取試算表 | `spreadsheets.readonly` |
| 選擇開啟試算表寫回 | `spreadsheets`，取代唯讀權限 |
| 列出日曆與讀取自有行程 | `calendar.calendarlist.readonly`、`calendar.events.owned.readonly` |

Google 帳號本身仍須具有檔案存取／編輯權限；程式也會遵守工作表保護範圍。帳號標籤不會變更本機個人／公司使用模式。[Sheets scopes](https://developers.google.com/workspace/sheets/api/scopes)、[Calendar scopes](https://developers.google.com/workspace/calendar/api/auth)

## 新增私人 Sheet 任務來源

1. 在總覽選取登入帳號，按「＋新增來源」。
2. 填來源名稱，確認使用帳號，類型選「Google Sheets」。
3. 貼上試算表網址或 Spreadsheet ID，按「讀取工作表／自有日曆」，選擇 Tasks 分頁；亦可填入已知 GID。
4. 設定同步間隔（1–1440 分鐘，預設 5 分鐘），按「儲存來源」，再按「立即同步」。

每個來源有獨立的滑動分頁、狀態與最後成功時間；切換分頁保留草稿。儲存後自動鎖定設定，需關閉「鎖定來源設定」才能修改。儲存後背景每分鐘檢查是否到期；暫時失敗逐次延長重試等待，手動同步可立即重試。

此版 Google Sheets 來源同步 **Tasks**，沿用範例的英文標題列、第二列中文說明，以及 `Id`、`Time`、`Title`、`Enabled` 等欄位。支援目標／排除裝置篩選，員工資料仍沿用既有 Sheet A 快取。不同 Google 來源的相同任務 ID 互不覆蓋；既有 Sheet A／B 不會自動改為 Google 來源，也不會刪除或重建。

若同一份 Tasks 已透過公開 A／B 同步，切換到新 Google 來源前請停用舊入口，避免同一任務從兩個來源提醒。日曆 JSON 的 GitHub 更新仍維持獨立。

## Google 日曆

新增來源時選「Google 日曆」，按「讀取工作表／自有日曆」，選取該帳號擁有的主日曆或其他日曆。要加入另一個日曆，可再新增一個來源。此版不列出僅由他人分享、目前帳號不是擁有者的日曆。

- 預設提前 10 分鐘提醒，可設定 0–10080 分鐘。
- 全天事件預設不提醒；勾選後可指定 0–23 時，依來源日曆時區換算成本機時間。
- 同步前 2 天至未來 180 天的行程，處理重複事件實例、改期、取消與拒絕邀請；首次同步不大量補跳過去行程。
- 行程顯示於首頁與「我的任務」，來源包含帳號標籤與日曆名稱；可選「Google」來源篩選，搜尋也可比對帳號／日曆標籤。
- 日曆維持唯讀，本機確認提醒不會改動 Google 日曆或回覆邀請。Google 本身的通知設定仍由 Google 管理。

日曆目前使用定期完整讀取選定期間，含分頁；尚未改用 `syncToken` 增量同步。

## 在程式內編輯線上 Tasks

1. 對應帳號須以「允許 Sheets 編輯」重新授權。
2. 解鎖來源設定，勾選「允許此 Tasks 來源寫回」，儲存。
3. 在來源下方「線上任務編輯」輸入原始任務 ID 並讀取，或按「新增任務」。
4. 修改欄位；`Enabled` 設為 `FALSE` 可停用。任務 ID 固定不變。
5. 按「預覽差異並寫入」，檢查來源、任務 ID 與差異後確認。刪除則使用「刪除雲端任務」，會刪除該任務整列。

寫入前會重新讀取並比對整張表的快照；若有異動就拒絕送出，請重新讀取合併。修改只更新實際變動的儲存格，保留標題、說明列及其他欄位；含任務欄位公式的工作表不開放此編輯器，請至 Google Sheets 維護。

Google Sheets 的批次寫入不提供跨程式編輯鎖，因此此版適用於 **沒有其他人或程式同時編輯** 的 Tasks 表。寫入後回讀確認；逾時結果不明時不自動重送，草稿送出憑證立即作廢，須按任務 ID 重新讀取確認。新增的 ID 在送出前已產生，可用它查驗是否已建立。[Google 批次更新說明](https://developers.google.com/workspace/sheets/api/reference/rest/v4/spreadsheets/batchUpdate)

## 資料保存與登出

- OAuth 設定、帳號與憑證放在使用者資料目錄的 `google-workspace.dat`，使用 Windows DPAPI 加密，綁定目前 Windows 使用者；不進入一般設定匯出或 `.calbak` 備份。
- 已同步的任務／日曆資料會存在既有本機 SQLite 快取，供離線提醒；它不是整庫加密，應由 Windows 帳號與磁碟存取權保護。
- 「登出選取帳號」移除該帳號憑證、來源與本機任務快取，歷史中的私人標題／內容會清除；不刪除雲端資料，其他帳號繼續同步。
- 「解除 Google 授權」另向 Google 撤銷同一專案的授權，可能影響同帳號在其他裝置的連線；介面會先提示。
- 移除來源、改綁帳號、停用或確認無權限時清除對應快取。一般網路失敗則保留上次成功結果。

## 驗收狀態

已以模擬 Google API 與隔離資料庫驗證：多帳號／自動續用、DPAPI 保存、PKCE／state／loopback、日曆分頁與異動、寫回衝突、結果不明禁止重送及登出清除。尚未取得實際桌面 OAuth 設定，**真實 Google 登入、Workspace 管理政策與正式 Sheet／日曆 API 存取仍待實機驗收**。

後續擴充：員工／假日／農曆的 Google 來源、產生器寫回橋接、共享日曆、日曆增量同步，以及既有 A／B 來源身分的直接遷移；本機版先保留公開 A／B 路徑。

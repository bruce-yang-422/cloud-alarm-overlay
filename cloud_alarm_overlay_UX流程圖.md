# Cloud Alarm Overlay UX 流程圖

本文件用 Mermaid 圖表呈現系統的關鍵使用者流程與狀態轉換，與 [cloud_alarm_overlay_UI規劃書.md](./cloud_alarm_overlay_UI規劃書.md)（畫面清單、元件、線框圖）互補——那份文件講「每個畫面長什麼樣、有什麼元件」，這份文件講「使用者/系統怎麼從一個狀態走到下一個狀態」。

---

## 1. 首次啟動與裝置身分設定流程

對應主規劃書 19C 節、UI 規劃書 6.1 節。

```mermaid
flowchart TD
    Logon[Windows 重開機後使用者登入] --> AutoStartEnabled{管理員開啟<br/>登入自動啟動？}
    AutoStartEnabled -->|是| AutoStart[Windows 啟動項目啟動程式]
    AutoStartEnabled -->|否| ManualStart[使用者手動啟動程式]
    AutoStart --> Start([程式啟動])
    ManualStart --> Start
    Start --> CheckDevice{本機已設定<br/>裝置代碼？}
    CheckDevice -->|是| MainWindow([進入主視窗])
    CheckDevice -->|否| FirstRun[顯示首次使用設定視窗]

    FirstRun --> Choice{使用者選擇}
    Choice -->|手動輸入| InputCode[輸入裝置代碼 + 我的名稱]
    Choice -->|匯入備份檔| PickFile[選取 .calbak 檔案]

    PickFile --> ValidateFile{檔案格式<br/>正確？}
    ValidateFile -->|否| FileError[顯示錯誤訊息]
    FileError --> Choice
    ValidateFile -->|是| RestoreData[解壓縮還原：<br/>裝置身分/個人設定/<br/>本機任務/簽收紀錄/管理者密碼]
    RestoreData --> ShowSummary[顯示還原摘要]
    ShowSummary --> InputCode

    InputCode --> ValidateCode{裝置代碼<br/>非空白？}
    ValidateCode -->|否| InputCode
    ValidateCode -->|是| SaveLocal[寫入本機 SQLite<br/>Settings 表]
    SaveLocal --> MainWindow
```

---

## 2. 通知觸發與確認狀態機（四等級共通）

對應主規劃書 27 節、UI 規劃書 9.6 節。

```mermaid
stateDiagram-v2
    [*] --> Pending: 任務建立/同步
    Pending --> Triggering: AlarmWorker 判定到期<br/>(DB Transaction 鎖定)
    Triggering --> Queued: 高級/最高級<br/>且已有視窗顯示中
    Triggering --> Displayed: 可立即顯示
    Queued --> Displayed: 前一則確認關閉後

    Displayed --> Acknowledged: 使用者完成確認
    Displayed --> Snoozed: 點擊稍後提醒<br/>(僅中/高級,見23.1)
    Snoozed --> Displayed: SnoozedUntil 到期<br/>重新觸發

    Acknowledged --> [*]

    note right of Triggering
        低級/中級：直接顯示，可並存
        高級/最高級：同時只顯示一個
    end note
```

---

## 3. 四種通知等級的關閉行為決策樹

對應主規劃書 18 節、UI 規劃書 9 節。

```mermaid
flowchart TD
    Trigger([通知觸發]) --> Level{等級}

    Level -->|低級| Low[右下角便利貼<br/>不 Topmost]
    Low --> LowWait{10 秒內<br/>使用者操作？}
    LowWait -->|點擊確認| LowAck[寫入 Acknowledged]
    LowWait -->|逾時無操作| LowAuto[自動視為已讀<br/>Acknowledged]

    Level -->|中級| Mid[右下角便利貼<br/>Topmost]
    Mid --> MidWait[等待使用者點擊確認<br/>不會自動消失]
    MidWait --> MidAck[寫入 Acknowledged]

    Level -->|高級| High[60% 螢幕置中<br/>Topmost]
    High --> HighWait[等待點擊<br/>我已閱讀確認關閉]
    HighWait --> HighAck[寫入 Acknowledged]

    Level -->|最高級| Max[全螢幕 90%+<br/>Topmost 紅黑閃爍]
    Max --> MaxInput[等待輸入驗證碼]
    MaxInput --> MaxCheck{驗證碼<br/>正確？}
    MaxCheck -->|否| MaxShake[輸入框抖動<br/>不清空內容]
    MaxShake --> MaxInput
    MaxCheck -->|是| MaxAck[顯示已確認<br/>寫入 Acknowledged]

    LowAck --> Next([檢查佇列/結束])
    LowAuto --> Next
    MidAck --> Next
    HighAck --> Next
    MaxAck --> Next
```

---

## 4. 任務資料來源與編輯權限流程

對應主規劃書 19B、19.1、19.2 節（Sheet A / Sheet B 雙來源架構，Sheet A 底下含三個分頁）。

```mermaid
flowchart LR
    subgraph Cloud["Google Sheet（雲端，程式僅讀取）"]
        subgraph SheetA["Sheet A（僅 IT 可編輯）"]
            SheetATasks[("Tasks / Holidays")]
            SheetAEmployees[("Employees<br/>員工清單+通知上限")]
        end
        SheetB[("Sheet B<br/>開放團隊成員編輯<br/>Tasks")]
    end

    subgraph Local["本機"]
        SQLite[(SQLite 快取)]
        UI[我的任務頁面]
    end

    SheetATasks -->|定期同步<br/>CSV 匯出| SQLite
    SheetAEmployees -->|定期同步<br/>CSV 匯出| SQLite
    SheetB -->|定期同步<br/>CSV 匯出| SQLite
    LocalTask[使用者新增<br/>本機任務] --> SQLite
    SQLite --> UI

    SheetAEmployees -.部門鍵值查表展開.-> SheetATasks
    SheetAEmployees -.部門鍵值查表展開.-> SheetB

    UI -.唯讀顯示.-> SheetATask[來源=Sheet A 的列<br/>🔒 唯讀圖示]
    UI -.唯讀顯示.-> SheetBTask[來源=Sheet B 的列<br/>🔒 唯讀圖示]
    UI -.可編輯.-> LocalTaskRow[來源=本機的列<br/>✎ 🗑 ⧉]

    IT[IT 人員] -->|直接編輯| SheetATasks
    IT -->|直接編輯| SheetAEmployees
    TeamMember[任何團隊成員] -->|直接編輯| SheetB
```

```text
Employees 與 Tasks 的虛線關係，代表「查表展開」而非同步依賴：
  Sheet A/B 的 Tasks.TargetDeviceOrName / ExcludeDeviceOrName 若填入部門名稱，
  同步時程式會查詢已快取的 Employees 資料展開成該部門所有員工，
  再依裝置代碼/使用者名稱逐一比對是否觸發（見主規劃書 19.2 節）
  Employees 分頁讀取失敗時，部門鍵值視為空清單（不展開任何人），但不影響 Tasks/Holidays 分頁本身的同步與顯示
```

---

## 5. 管理者登入與單機管理範圍

對應主規劃書 15、16、19 節（單一管理者身分，僅管本機）。

```mermaid
flowchart TD
    User([一般使用者]) --> ClickLogin[點擊側欄「管理者專區」<br/>或 Tray 登入選單]
    ClickLogin --> LoginDialog[輸入帳號密碼]
    LoginDialog --> Verify{驗證}
    Verify -->|失敗| ShowError[顯示「帳號或密碼錯誤」]
    ShowError --> LoginDialog
    Verify -->|成功| AdminMode[右側顯示管理者專區]

    AdminMode --> Scope{以右側 Tabs 切換子功能}
    Scope --> S1[8.1 同步來源<br/>本機的 Sheet A/B 連結設定]
    Scope --> S2[8.2 本機設定<br/>本機鎖定開關/閃爍速度]
    Scope --> S3[8.3 系統紀錄<br/>本機的同步/事件紀錄]
    Scope --> S4[8.4 Audit Log<br/>本機管理者操作紀錄]
    Scope --> S5[備份與還原<br/>本機備份與管理者帳密]

    AdminMode -.不可觸及.-> OutOfScope[["公司政策內容<br/>Sheet A／Employees 員工清單與通知上限例外<br/>其他裝置的資料<br/>（一律改到 Google Sheet 編輯）"]]

    AdminMode --> Timeout{15 分鐘<br/>無操作？}
    Timeout -->|是| AutoLogout[自動登出<br/>回到一般使用者畫面]
    Timeout -->|否| AdminMode
```

---

## 6. 本機備份與還原完整流程

對應主規劃書 19C.2 節、UI 規劃書 6.0a 節。

```mermaid
sequenceDiagram
    participant U as 使用者
    participant Old as 舊電腦
    participant File as .calbak 檔案
    participant New as 新電腦

    U->>Old: 點擊「建立本機備份」
    Old->>U: 彈出選項視窗（是否包含管理者密碼）
    alt 勾選包含管理者密碼
        U->>Old: 輸入目前管理者密碼驗證
        Old->>Old: 驗證通過，加入 admin.key
    end
    Old->>Old: 打包 identity.json + settings.json<br/>+ tasks.csv + acklog.csv (+admin.key)
    Old->>File: 產生 backup_{代碼}_{日期}.calbak
    U->>File: 存到隨身碟/雲端硬碟

    Note over U,New: --- 重灌或換到新電腦 ---

    U->>New: 安裝程式，首次啟動
    New->>U: 顯示首次使用設定視窗
    U->>New: 點擊「匯入備份檔」
    U->>File: 選取 .calbak
    File->>New: 解壓縮

    New->>New: 還原 identity.json（裝置代碼/名稱）
    New->>New: 還原 settings.json（個人偏好）
    New->>New: 匯入 tasks.csv（以 Id 比對，重複則略過）
    New->>New: 匯入 acklog.csv（僅新增，不覆蓋）
    opt 備份含 admin.key
        New->>U: 二次確認是否覆蓋現有管理者密碼
        U->>New: 確認
        New->>New: 還原管理者密碼雜湊
    end
    New->>U: 顯示還原摘要（例如「已匯入 12 筆任務」）
    U->>New: 確認後點擊「開始使用」
    New->>U: 進入主視窗，狀態與舊電腦一致
```

---

## 7. 多任務排隊顯示流程（高級/最高級）

對應主規劃書 27 節「多任務排隊」。

```mermaid
flowchart TD
    T1[任務 A 到期] --> Check1{目前有其他<br/>高級/最高級視窗<br/>顯示中？}
    Check1 -->|否| Show1[立即顯示任務 A]
    Check1 -->|是| Queue1[任務 A 進入佇列]

    T2[任務 B 到期<br/>此時任務 A 仍顯示中] --> Check2{目前有其他<br/>高級/最高級視窗<br/>顯示中？}
    Check2 -->|是| Queue2[任務 B 進入佇列]

    Show1 --> Corner[視窗角落顯示<br/>「還有 N 個待確認通知」]
    Queue1 -.排入.-> Corner
    Queue2 -.排入.-> Corner

    Show1 --> UserAck[使用者完成確認]
    UserAck --> CheckQueue{佇列<br/>非空？}
    CheckQueue -->|是| Dequeue[取出佇列中<br/>最早到期的任務]
    Dequeue --> ShowNext[顯示下一個視窗]
    ShowNext --> UserAck
    CheckQueue -->|否| Done([結束，畫面清空])
```

---

## 8. 系統喚醒校正流程

對應主規劃書 26.1 節。

```mermaid
flowchart TD
    Sleep([電腦進入睡眠]) --> Resume[電腦喚醒]
    Resume --> Detect[監聽到<br/>PowerModeChanged.Resume]
    Detect --> CancelTimer[取消目前排定的 Timer<br/>時間可能已失準]
    CancelTimer --> Recalc[重新查詢目前時間<br/>重新計算下一個最早任務]
    Recalc --> CheckMissed{睡眠期間<br/>有任務錯過到期時間？}
    CheckMissed -->|是| MarkOverdue[標記為 Overdue_Unacked<br/>寫入 AcknowledgementLogs<br/>不補跳全螢幕通知]
    CheckMissed -->|否| SetTimer[設定新的一次性 Timer]
    MarkOverdue --> SetTimer
    SetTimer --> Wait([等待下次到期])
```

---

## 8.1 簽收紀錄集中化流程（未來開發目標，第一版／第二版不啟用）

對應主規劃書 20.3、38 節。第一版／第二版 AcknowledgementLogs 僅本機保留，下圖描述的是**未來**若啟用「方向 A：內網直連公司現有 Linux 主機」時的同步流程，目前僅在資料表預留 SyncedAt/SyncStatus 欄位（見主規劃書 21 節），尚無對應程式邏輯：

```mermaid
flowchart TD
    Ack([使用者簽收通知<br/>或背景逾期寬限判定]) --> WriteLocal[寫入本機 SQLite<br/>AcknowledgementLogs<br/>SyncStatus = Pending]
    WriteLocal --> Continue[本機功能立即可用<br/>不等待任何同步結果]

    Continue -.第一版／第二版：<br/>流程到此為止，不繼續.-> StopHere([結束])

    Continue -.未來開發目標：<br/>背景同步服務.-> CheckNet{目前是否<br/>在公司內網／VPN？}
    CheckNet -->|否| Wait[保持 SyncStatus = Pending<br/>累積待送清單，不遺失]
    Wait -.下次偵測到內網.-> CheckNet
    CheckNet -->|是| PushLinux[主動推送給<br/>公司內網 Linux 主機<br/>方向 A，見主規劃書 20.3 節]
    PushLinux --> PushResult{主機是否<br/>成功接收？}
    PushResult -->|成功| MarkSynced[更新該筆記錄<br/>SyncStatus = Synced<br/>SyncedAt = 現在時間]
    PushResult -->|失敗/逾時| KeepPending[維持 SyncStatus = Pending<br/>或標記 Failed，下次排程再試]
    KeepPending -.下次排程.-> CheckNet
```

```text
關鍵設計原則（見主規劃書 20.3 節）：
  本機寫入與同步推送是兩個獨立步驟，使用者簽收動作永遠只依賴本機 SQLite 立即完成，
    不會因為同步失敗、內網不通而卡住或延遲通知視窗的關閉
  方向為「員工電腦主動推給 Linux 主機」，不是「主機來查電腦」：
    員工電腦不需開放任何連入 port、不需要固定 IP，Linux 主機只要能被動接收即可
  離線期間（筆電帶出公司、行動熱點上網）累積的 Pending 記錄不會遺失，
    只是延後同步，下次偵測到內網環境即自動補送
  此圖為未來開發目標的預期流程，實際採用哪種傳輸方式（HTTP API／共用資料夾／SFTP）
    留待該階段實作時決定，不影響本圖的整體流程邏輯
```

---

## 9. 整體資訊架構導覽圖

```mermaid
flowchart TD
    Main[主視窗] --> Dashboard[首頁 Dashboard]
    Main --> Tasks[我的任務]
    Main --> History[歷史紀錄]
    Main --> Settings[設定]
    Main -->|需登入| Admin[管理者頁]

    Dashboard --> TestAlarm[測試警示<br/>四等級預覽]
    Tasks --> TaskDialog[新增/編輯<br/>本機任務 Dialog]
    Settings --> AdminLogin[管理者登入 Dialog]

    Admin --> A1[同步來源<br/>Sheet A/B 連結]
    Admin --> A2[本機設定<br/>鎖定開關與結束密碼]
    Admin --> A3[系統紀錄]
    Admin --> A4[Audit Log]
    Admin --> A5[備份與還原]

    Tray[System Tray] --> Main
    Tray --> TestAlarm
    Tray --> TaskDialog
    Tray --> AdminLogin
    Tray --> Quit[結束程式確認與密碼驗證]

    AlarmWindow[["通知視窗<br/>(低/中/高/最高級)"]]
    Dashboard -.觸發.-> AlarmWindow
    AlarmWindow -.簽收後寫入.-> History
```

## 10. 結束程式密碼保護

```mermaid
flowchart TD
    TrayQuit[系統匣：結束程式] --> Blocking{有待確認的強制通知？}
    Blocking -->|是| Continue[繼續執行提醒]
    Blocking -->|否| Confirm{確認結束？}
    Confirm -->|否| Continue
    Confirm -->|是| Required{管理員設定：結束時要求密碼？}
    Required -->|否| CheckAgain
    Required -->|是| Mode{管理員設定的驗證方式}
    Mode -->|預設：管理員帳密| AdminPassword[重新輸入管理員帳號與密碼]
    Mode -->|專用結束密碼| DedicatedPassword[輸入專用結束密碼]
    AdminPassword --> Valid{驗證成功？}
    DedicatedPassword --> Valid
    Valid -->|否或取消| Continue
    Valid -->|是| CheckAgain{此時有待確認的強制通知？}
    CheckAgain -->|是| Continue
    CheckAgain -->|否| Exit[保存狀態並結束程式]
    AdminSettings[管理員專區：本機設定] --> Change[設定或重設專用密碼；無需舊結束密碼]
    AdminSettings --> Default[切回管理員帳密]
```

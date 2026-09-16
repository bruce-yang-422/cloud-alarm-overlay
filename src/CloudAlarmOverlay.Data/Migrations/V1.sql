CREATE TABLE IF NOT EXISTS Tasks (
    Id                      TEXT NOT NULL PRIMARY KEY,      -- GUID 或本機自增 Id，本機任務與雲端任務需可共存不衝突
    ExternalId              TEXT,                  -- 對應 Sheet 上的 Id 欄（雲端任務），本機任務為 NULL
    Title                   TEXT NOT NULL,
    Description             TEXT,
    ScheduledAt             TEXT NOT NULL,          -- ISO8601 本地時間字串，完整日期時間（見 20.2 節：本身支援跨日）
    Source                  TEXT NOT NULL,          -- '本機' / 'SheetA' / 'SheetB'
    Level                   TEXT NOT NULL,          -- '一般提醒' / '重要提醒' / '緊急提醒' / '強制通知'
    Enabled                 INTEGER NOT NULL DEFAULT 1,
    IsTriggered             INTEGER NOT NULL DEFAULT 0,
    RequireAcknowledgement  INTEGER NOT NULL DEFAULT 0,
    TargetDeviceOrName      TEXT,                   -- 分號分隔清單，見 19.2 節；空值=全公司廣播
    ExcludeDeviceOrName     TEXT,                   -- 同上；空值=不排除
    Recurrence              TEXT NOT NULL DEFAULT 'None',  -- None/Daily/Weekly:n,n/Monthly:n/LunarDay:n,n
    SkipOnHoliday           INTEGER NOT NULL DEFAULT 0,
    CreatedAt               TEXT NOT NULL,
    UpdatedAt               TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS Holidays (
    Id      INTEGER PRIMARY KEY AUTOINCREMENT,
    Date    TEXT NOT NULL,      -- YYYY-MM-DD
    Type    TEXT NOT NULL,      -- '國定假日' / '全公司停班' / '補班日' / '其他'
    Note    TEXT,
    Source  TEXT NOT NULL DEFAULT 'Sheet'  -- 'Sheet' / '本機'（規劃書保留本機可能性，但目前僅 Sheet 來源會寫入）
);

CREATE TABLE IF NOT EXISTS LunarCalendar (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    Date        TEXT NOT NULL,       -- 西曆 YYYY-MM-DD，建議加 UNIQUE 約束
    LunarDate   TEXT,                -- 農曆日期文字，如「正月初一」，純顯示用
    LunarDay    INTEGER NOT NULL,    -- 農曆當月第幾天，1-30，程式比對用
    SolarTerm   TEXT                 -- 節氣，選填
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_LunarCalendar_Date ON LunarCalendar(Date);

CREATE TABLE IF NOT EXISTS AcknowledgementLogs (
    Id                TEXT NOT NULL PRIMARY KEY,
    TaskId            TEXT NOT NULL,
    DeviceId          TEXT NOT NULL,
    DisplayName       TEXT,                -- 使用者名稱快照，記錄當下的名稱（即使日後改名不影響歷史）
    TriggeredAt       TEXT NOT NULL,
    AcknowledgedAt    TEXT,
    DurationSeconds    INTEGER,
    Result            TEXT NOT NULL,   -- Acknowledged / Overdue_Acknowledged / Overdue_Unacked / NotLaunched
    SyncedAt          TEXT,            -- 【未來擴充預留】第一版/第二版永遠 NULL，不使用
    SyncStatus        TEXT NOT NULL DEFAULT 'NotApplicable'  -- 【未來擴充預留】永遠 NotApplicable
);

CREATE TABLE IF NOT EXISTS Devices (
    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
    DeviceId     TEXT NOT NULL,     -- IT 手動分配的固定代碼（19C、19C.1 節，不可程式自動產生）
    DisplayName  TEXT,              -- 使用者名稱，同樣由 IT 分配，一般使用者不可自行修改
    LastSeen     TEXT,
    Version      TEXT
);

CREATE TABLE IF NOT EXISTS Users (
    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    Username      TEXT NOT NULL UNIQUE,
    DisplayName   TEXT,
    PasswordHash  TEXT NOT NULL,   -- PBKDF2 或 Argon2id，見 30 節
    Salt          TEXT NOT NULL,
    Enabled       INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS Employees (
    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    DeviceId            TEXT NOT NULL,   -- 對應 Sheet A/Employees 的 Id 欄
    Name                TEXT,            -- 可留空（公用電腦，見 19.1 節）
    Department          TEXT,            -- 供部門查表展開用（19.2 節），非純顯示，程式會讀取
    MaxAllowedLevel     TEXT,            -- 一般提醒/重要提醒/緊急提醒/強制通知/NULL（NULL 或 '-' 代表沿用全體預設=強制通知）
    RequireAckOverride  TEXT             -- TRUE/FALSE/NULL（NULL 或 '-' 代表不覆寫，沿用任務本身設定）
);

CREATE TABLE IF NOT EXISTS Settings (
    Key     TEXT NOT NULL PRIMARY KEY,
    Value   TEXT,
    Locked  INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS PomodoroSettings (
    Key     TEXT NOT NULL PRIMARY KEY,
    Value   TEXT
);

CREATE TABLE IF NOT EXISTS PomodoroLog (
    Id            TEXT NOT NULL PRIMARY KEY,
    Type          TEXT NOT NULL,     -- Focus / Break / LongBreak
    StartedAt     TEXT NOT NULL,
    CompletedAt   TEXT,              -- 提前結束則為 NULL
    Result        TEXT NOT NULL      -- Completed / Interrupted
);

CREATE TABLE IF NOT EXISTS SyncLogs (
    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
    Time         TEXT NOT NULL,
    Status       TEXT NOT NULL,     -- Success / Failed
    Message      TEXT,
    RecordCount  INTEGER,
    Source       TEXT               -- SheetA / SheetB（21.1 節匯出規格要求此欄）
);

CREATE TABLE IF NOT EXISTS AuditLogs (
    Id         INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId     TEXT NOT NULL,
    Action     TEXT NOT NULL,
    OldValue   TEXT,
    NewValue   TEXT,
    CreatedAt  TEXT NOT NULL
);

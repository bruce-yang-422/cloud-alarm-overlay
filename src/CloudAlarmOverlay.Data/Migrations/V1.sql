-- Complete initial schema. PRAGMA user_version is set to 1 by DatabaseInitializer.
CREATE TABLE Tasks (
    Id TEXT NOT NULL PRIMARY KEY,
    ExternalId TEXT,
    Title TEXT NOT NULL,
    Description TEXT,
    ScheduledAt TEXT NOT NULL,
    Source TEXT NOT NULL,
    Level TEXT NOT NULL,
    Enabled INTEGER NOT NULL DEFAULT 1,
    IsTriggered INTEGER NOT NULL DEFAULT 0,
    RequireAcknowledgement INTEGER NOT NULL DEFAULT 0,
    TargetDeviceOrName TEXT,
    ExcludeDeviceOrName TEXT,
    Recurrence TEXT NOT NULL DEFAULT 'None',
    SkipOnHoliday INTEGER NOT NULL DEFAULT 0,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL,
    Note TEXT
);

CREATE TABLE Holidays (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Date TEXT NOT NULL,
    Type TEXT NOT NULL,
    Note TEXT,
    Source TEXT NOT NULL DEFAULT 'Sheet'
);

CREATE TABLE LunarCalendar (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Date TEXT NOT NULL,
    LunarDate TEXT,
    LunarDay INTEGER NOT NULL,
    SolarTerm TEXT
);
CREATE UNIQUE INDEX IX_LunarCalendar_Date ON LunarCalendar(Date);

CREATE TABLE AcknowledgementLogs (
    Id TEXT NOT NULL PRIMARY KEY,
    TaskId TEXT NOT NULL,
    DeviceId TEXT NOT NULL,
    DisplayName TEXT,
    TriggeredAt TEXT NOT NULL,
    AcknowledgedAt TEXT,
    DurationSeconds INTEGER,
    Result TEXT NOT NULL,
    SyncedAt TEXT,
    SyncStatus TEXT NOT NULL DEFAULT 'NotApplicable',
    TaskName TEXT,
    ScheduledAt TEXT,
    Source TEXT
);
CREATE INDEX IX_AckLog_TriggeredAt ON AcknowledgementLogs(TriggeredAt);

CREATE TABLE Devices (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    DeviceId TEXT NOT NULL,
    DisplayName TEXT,
    LastSeen TEXT,
    Version TEXT
);

CREATE TABLE Users (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Username TEXT NOT NULL UNIQUE,
    DisplayName TEXT,
    PasswordHash TEXT NOT NULL,
    Salt TEXT NOT NULL,
    Enabled INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE Employees (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    DeviceId TEXT NOT NULL,
    Name TEXT,
    Department TEXT,
    MaxAllowedLevel TEXT,
    RequireAckOverride TEXT
);

CREATE TABLE Settings (
    Key TEXT NOT NULL PRIMARY KEY,
    Value TEXT,
    Locked INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE PomodoroSettings (
    Key TEXT NOT NULL PRIMARY KEY,
    Value TEXT
);

CREATE TABLE PomodoroLog (
    Id TEXT NOT NULL PRIMARY KEY,
    Type TEXT NOT NULL,
    StartedAt TEXT NOT NULL,
    CompletedAt TEXT,
    Result TEXT NOT NULL,
    EndedAt TEXT,
    PlannedMinutes INTEGER NOT NULL DEFAULT 0,
    IsActive INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IX_PomodoroLog_StartedAt ON PomodoroLog(StartedAt);

CREATE TABLE SyncLogs (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Time TEXT NOT NULL,
    Status TEXT NOT NULL,
    Message TEXT,
    RecordCount INTEGER,
    Source TEXT
);

CREATE TABLE AuditLogs (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId TEXT NOT NULL,
    Action TEXT NOT NULL,
    OldValue TEXT,
    NewValue TEXT,
    CreatedAt TEXT NOT NULL
);
CREATE INDEX IX_AuditLogs_CreatedAt ON AuditLogs(CreatedAt);

CREATE TABLE Occurrences (
    Id TEXT NOT NULL PRIMARY KEY,
    TaskId TEXT NOT NULL,
    ScheduledAt TEXT NOT NULL,
    State TEXT NOT NULL,
    TriggeredAt TEXT,
    UNIQUE(TaskId, ScheduledAt)
);
CREATE INDEX IX_Occurrences_State ON Occurrences(State);

CREATE TABLE SystemEvents (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Time TEXT NOT NULL,
    EventType TEXT NOT NULL,
    Message TEXT NOT NULL
);
CREATE INDEX IX_SystemEvents_Time ON SystemEvents(Time);

CREATE TRIGGER TR_Audit_SystemEvent AFTER INSERT ON AuditLogs BEGIN
    INSERT INTO SystemEvents(Time, EventType, Message)
    VALUES (NEW.CreatedAt,
            CASE WHEN NEW.Action = '管理者登入' THEN 'AdminLogin' ELSE 'SettingChanged' END,
            NEW.UserId || '：' || NEW.Action);
END;

CREATE TRIGGER TR_Sync_SystemEvent AFTER INSERT ON SyncLogs WHEN NEW.Status = '失敗' BEGIN
    INSERT INTO SystemEvents(Time, EventType, Message)
    VALUES (NEW.Time, 'SyncFailed', COALESCE(NEW.Source, '') || '：' || COALESCE(NEW.Message, ''));
END;

CREATE TRIGGER TR_AlarmShown_SystemEvent AFTER INSERT ON AcknowledgementLogs WHEN NEW.Result = 'Pending' BEGIN
    INSERT INTO SystemEvents(Time, EventType, Message)
    VALUES (NEW.TriggeredAt, 'AlarmShown', COALESCE(NEW.TaskName, NEW.TaskId));
END;

CREATE TRIGGER TR_AlarmClosed_SystemEvent AFTER UPDATE OF AcknowledgedAt ON AcknowledgementLogs
WHEN OLD.AcknowledgedAt IS NULL AND NEW.AcknowledgedAt IS NOT NULL BEGIN
    INSERT INTO SystemEvents(Time, EventType, Message)
    VALUES (NEW.AcknowledgedAt, 'AlarmClosed', COALESCE(NEW.TaskName, NEW.TaskId));
END;

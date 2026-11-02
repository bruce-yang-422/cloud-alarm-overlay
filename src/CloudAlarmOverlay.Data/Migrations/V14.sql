-- Temporary authorization failures remove private event data, but retain local reminder preferences.
CREATE TABLE IF NOT EXISTS CalendarReminderRecovery (
    TaskId TEXT PRIMARY KEY,
    Source TEXT NOT NULL,
    Enabled INTEGER,
    ScheduledAt TEXT
);
CREATE INDEX IF NOT EXISTS IX_CalendarReminderRecovery_Source ON CalendarReminderRecovery(Source);

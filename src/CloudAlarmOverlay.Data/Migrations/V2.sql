ALTER TABLE Tasks ADD COLUMN Note TEXT;
ALTER TABLE AcknowledgementLogs ADD COLUMN TaskName TEXT;
ALTER TABLE AcknowledgementLogs ADD COLUMN ScheduledAt TEXT;
ALTER TABLE AcknowledgementLogs ADD COLUMN Source TEXT;
UPDATE AcknowledgementLogs SET TaskName=(SELECT Title FROM Tasks WHERE Tasks.Id=TaskId),
    ScheduledAt=(SELECT ScheduledAt FROM Tasks WHERE Tasks.Id=TaskId),
    Source=(SELECT Source FROM Tasks WHERE Tasks.Id=TaskId);
CREATE TABLE Occurrences (
    Id TEXT NOT NULL PRIMARY KEY,
    TaskId TEXT NOT NULL,
    ScheduledAt TEXT NOT NULL,
    State TEXT NOT NULL,
    TriggeredAt TEXT,
    UNIQUE(TaskId, ScheduledAt)
);
CREATE INDEX IX_Occurrences_State ON Occurrences(State);
CREATE INDEX IX_AckLog_TriggeredAt ON AcknowledgementLogs(TriggeredAt);

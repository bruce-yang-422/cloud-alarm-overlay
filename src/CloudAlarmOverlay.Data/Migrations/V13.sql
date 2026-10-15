CREATE TABLE IF NOT EXISTS TaskCompletions (
    Source TEXT NOT NULL,
    TaskId TEXT NOT NULL,
    OccurrenceAt TEXT NOT NULL,
    CompletedAt TEXT NOT NULL,
    SnapshotJson TEXT NOT NULL,
    PRIMARY KEY (Source, TaskId, OccurrenceAt)
);

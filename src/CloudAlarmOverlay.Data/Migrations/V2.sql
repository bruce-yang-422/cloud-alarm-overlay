-- Additive upgrade from the released v1.0.0 database. Preserve all existing rows.
ALTER TABLE AcknowledgementLogs ADD COLUMN TaskSnapshotJson TEXT;
ALTER TABLE SyncLogs ADD COLUMN LastSeenAt TEXT;
ALTER TABLE SyncLogs ADD COLUMN RepeatCount INTEGER NOT NULL DEFAULT 1;
ALTER TABLE SyncLogs ADD COLUMN EventKind TEXT NOT NULL DEFAULT 'Legacy';
CREATE TABLE SyncStates (
    Source TEXT PRIMARY KEY NOT NULL,
    Fingerprint TEXT,
    ConfigFingerprint TEXT NOT NULL,
    Status TEXT NOT NULL,
    Message TEXT,
    LastCheckedAt TEXT NOT NULL,
    LastSuccessAt TEXT,
    ActiveLogId INTEGER
);

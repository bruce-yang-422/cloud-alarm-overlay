CREATE TABLE IF NOT EXISTS SystemEvents (
 Id INTEGER PRIMARY KEY AUTOINCREMENT, Time TEXT NOT NULL, EventType TEXT NOT NULL, Message TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_SystemEvents_Time ON SystemEvents(Time);
CREATE INDEX IF NOT EXISTS IX_AuditLogs_CreatedAt ON AuditLogs(CreatedAt);
CREATE TRIGGER IF NOT EXISTS TR_Audit_SystemEvent AFTER INSERT ON AuditLogs BEGIN
 INSERT INTO SystemEvents(Time,EventType,Message)
 VALUES(NEW.CreatedAt,CASE WHEN NEW.Action='管理者登入' THEN 'AdminLogin' ELSE 'SettingChanged' END,NEW.UserId||'：'||NEW.Action);
END;
CREATE TRIGGER IF NOT EXISTS TR_Sync_SystemEvent AFTER INSERT ON SyncLogs WHEN NEW.Status='失敗' BEGIN
 INSERT INTO SystemEvents(Time,EventType,Message) VALUES(NEW.Time,'SyncFailed',COALESCE(NEW.Source,'')||'：'||COALESCE(NEW.Message,''));
END;
CREATE TRIGGER IF NOT EXISTS TR_AlarmShown_SystemEvent AFTER INSERT ON AcknowledgementLogs WHEN NEW.Result='Pending' BEGIN
 INSERT INTO SystemEvents(Time,EventType,Message) VALUES(NEW.TriggeredAt,'AlarmShown',COALESCE(NEW.TaskName,NEW.TaskId));
END;
CREATE TRIGGER IF NOT EXISTS TR_AlarmClosed_SystemEvent AFTER UPDATE OF AcknowledgedAt ON AcknowledgementLogs WHEN OLD.AcknowledgedAt IS NULL AND NEW.AcknowledgedAt IS NOT NULL BEGIN
 INSERT INTO SystemEvents(Time,EventType,Message) VALUES(NEW.AcknowledgedAt,'AlarmClosed',COALESCE(NEW.TaskName,NEW.TaskId));
END;

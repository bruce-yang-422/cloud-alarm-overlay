CREATE TABLE Countdowns_V8 (
 Id TEXT NOT NULL PRIMARY KEY, Title TEXT NOT NULL, TargetAt TEXT NOT NULL,
 Mode TEXT NOT NULL CHECK (Mode IN ('Days','Time')),
 IsPinned INTEGER NOT NULL DEFAULT 0 CHECK (IsPinned IN (0,1)), CreatedAt TEXT NOT NULL,
 IsTop INTEGER NOT NULL DEFAULT 0 CHECK (IsTop IN (0,1)),
 Category TEXT NOT NULL DEFAULT '工作' CHECK (Category IN ('工作','生活','節日','旅行','其他')),
 Repeat TEXT NOT NULL DEFAULT 'None' CHECK (Repeat IN ('None','Weekly','Monthly','Yearly')),
 ReminderDays INTEGER NOT NULL DEFAULT -1 CHECK (ReminderDays IN (-1,0,1,3,7)),
 ReminderMinutes INTEGER NOT NULL DEFAULT 540 CHECK (ReminderMinutes BETWEEN 0 AND 1439),
 Notes TEXT NOT NULL DEFAULT '', CompletedAt TEXT NULL,
 ReminderChangedAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00',
 Direction TEXT NOT NULL DEFAULT 'Down', DisplayFormat TEXT NOT NULL DEFAULT 'Days',
 Recurrence TEXT NOT NULL DEFAULT '', SkipOnHoliday INTEGER NOT NULL DEFAULT 0
);
INSERT INTO Countdowns_V8 SELECT Id,Title,TargetAt,Mode,IsPinned,CreatedAt,IsTop,Category,Repeat,ReminderDays,ReminderMinutes,Notes,CompletedAt,ReminderChangedAt,Direction,DisplayFormat,Recurrence,SkipOnHoliday FROM Countdowns;
DROP TABLE Countdowns;
ALTER TABLE Countdowns_V8 RENAME TO Countdowns;

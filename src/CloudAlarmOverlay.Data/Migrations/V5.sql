-- Keep the new reminder names in stored tasks and employee level caps.
UPDATE Tasks SET Level = CASE Level
    WHEN '低級' THEN '一般提醒'
    WHEN '中級' THEN '重要提醒'
    WHEN '高級' THEN '緊急提醒'
    WHEN '最高級' THEN '強制通知'
    ELSE Level END
WHERE Level IN ('低級', '中級', '高級', '最高級');

UPDATE Employees SET MaxAllowedLevel = CASE MaxAllowedLevel
    WHEN '低級' THEN '一般提醒'
    WHEN '中級' THEN '重要提醒'
    WHEN '高級' THEN '緊急提醒'
    WHEN '最高級' THEN '強制通知'
    ELSE MaxAllowedLevel END
WHERE MaxAllowedLevel IN ('低級', '中級', '高級', '最高級');

-- Preserve a user's sound choices when the setting keys change.
INSERT OR IGNORE INTO Settings(Key, Value, Locked)
SELECT 'Sound:' || CASE substr(Key, 7)
    WHEN '低級' THEN '一般提醒'
    WHEN '中級' THEN '重要提醒'
    WHEN '高級' THEN '緊急提醒'
    WHEN '最高級' THEN '強制通知' END, Value, Locked
FROM Settings WHERE Key IN ('Sound:低級', 'Sound:中級', 'Sound:高級', 'Sound:最高級');

DELETE FROM Settings WHERE Key IN ('Sound:低級', 'Sound:中級', 'Sound:高級', 'Sound:最高級');

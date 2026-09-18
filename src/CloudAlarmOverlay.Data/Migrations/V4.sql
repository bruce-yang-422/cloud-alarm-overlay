ALTER TABLE Countdowns ADD COLUMN IsTop INTEGER NOT NULL DEFAULT 0 CHECK (IsTop IN (0, 1));

-- Keep every countdown; only remove excess home pins from earlier builds.
UPDATE Countdowns SET IsPinned=0
WHERE IsPinned=1 AND Id NOT IN (
    SELECT Id FROM Countdowns WHERE IsPinned=1 ORDER BY TargetAt, Title, Id LIMIT 2
);

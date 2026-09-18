CREATE TABLE Countdowns (
    Id TEXT NOT NULL PRIMARY KEY,
    Title TEXT NOT NULL,
    TargetAt TEXT NOT NULL,
    Mode TEXT NOT NULL CHECK (Mode IN ('Days', 'Time')),
    IsPinned INTEGER NOT NULL DEFAULT 0 CHECK (IsPinned IN (0, 1)),
    CreatedAt TEXT NOT NULL
);

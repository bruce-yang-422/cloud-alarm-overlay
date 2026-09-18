using CloudAlarmOverlay.Core.Services;
using Microsoft.Data.Sqlite;

namespace CloudAlarmOverlay.Data.Tests;

public sealed class DatabaseInitializerTests : IDisposable
{
    private readonly TestPaths _paths = new();
    private SqliteConnectionFactory Factory => new(_paths);
    private DatabaseInitializer Initializer => new(Factory);

    // Independent inventory for schema regression checks, not extracted from migration SQL.
    public static TheoryData<string, string> TableColumns => new()
    {
        { "Tasks", "Id ExternalId Title Description ScheduledAt Source Level Enabled IsTriggered RequireAcknowledgement TargetDeviceOrName ExcludeDeviceOrName Recurrence SkipOnHoliday CreatedAt UpdatedAt Note" },
        { "Holidays", "Id Date Type Note Source" },
        { "LunarCalendar", "Id Date LunarDate LunarDay SolarTerm" },
        { "AcknowledgementLogs", "Id TaskId DeviceId DisplayName TriggeredAt AcknowledgedAt DurationSeconds Result SyncedAt SyncStatus TaskName ScheduledAt Source TaskSnapshotJson" },
        { "Devices", "Id DeviceId DisplayName LastSeen Version" },
        { "Users", "Id Username DisplayName PasswordHash Salt Enabled" },
        { "Employees", "Id DeviceId Name Department MaxAllowedLevel RequireAckOverride" },
        { "Settings", "Key Value Locked" },
        { "PomodoroSettings", "Key Value" },
        { "PomodoroLog", "Id Type StartedAt CompletedAt Result EndedAt PlannedMinutes IsActive" },
        { "SyncLogs", "Id Time Status Message RecordCount Source LastSeenAt RepeatCount EventKind" },
        { "AuditLogs", "Id UserId Action OldValue NewValue CreatedAt" },
        { "Occurrences", "Id TaskId ScheduledAt State TriggeredAt" },
        { "SyncStates", "Source Fingerprint ConfigFingerprint Status Message LastCheckedAt LastSuccessAt ActiveLogId" },
        { "SystemEvents", "Id Time EventType Message" },
        { "Countdowns", "Id Title TargetAt Mode IsPinned CreatedAt IsTop Category Repeat ReminderDays ReminderMinutes Notes CompletedAt ReminderChangedAt Direction DisplayFormat Recurrence SkipOnHoliday" },
        { "TaskHomePins", "TaskId" }
    };

    [Fact]
    public async Task Version_seven_upgrade_preserves_all_countdown_fields_and_allows_five_categories()
    {
        for (var version = 1; version <= 7; version++)
        {
            using var stream = typeof(DatabaseInitializer).Assembly.GetManifestResourceStream($"CloudAlarmOverlay.Data.Migrations.V{version}.sql")!;
            using var reader = new StreamReader(stream);
            await ExecuteAsync(await reader.ReadToEndAsync());
        }
        await ExecuteAsync("PRAGMA user_version=7;");
        await ExecuteAsync("""
            INSERT INTO Countdowns VALUES ('kept','舊事件','2026-09-20T09:00:00','Time',1,'2026-09-18T00:00:00',1,'生活','Yearly',3,570,'備註','2026-09-21T00:00:00','2026-09-18T00:00:00','Up','YearsMonthsDays','LunarDate:3:23:Both',1);
            """);
        var before = await ScalarAsync("SELECT json_array(Id,Title,TargetAt,Mode,IsPinned,CreatedAt,IsTop,Category,Repeat,ReminderDays,ReminderMinutes,Notes,CompletedAt,ReminderChangedAt,Direction,DisplayFormat,Recurrence,SkipOnHoliday) FROM Countdowns;");
        await Initializer.InitializeAsync();
        Assert.Equal(before, await ScalarAsync("SELECT json_array(Id,Title,TargetAt,Mode,IsPinned,CreatedAt,IsTop,Category,Repeat,ReminderDays,ReminderMinutes,Notes,CompletedAt,ReminderChangedAt,Direction,DisplayFormat,Recurrence,SkipOnHoliday) FROM Countdowns;"));
        await ExecuteAsync("UPDATE Countdowns SET Category='旅行';");
        await ExecuteAsync("UPDATE Countdowns SET Category='其他';");
        Assert.Equal("其他", await ScalarAsync("SELECT Category FROM Countdowns;"));
        Assert.Equal("ok", await ScalarAsync("PRAGMA integrity_check;"));
    }

    [Fact]
    public async Task Fresh_database_has_baseline_schema_and_exactly_seventeen_empty_business_tables()
    {
        Assert.False(File.Exists(_paths.DatabasePath));
        await Initializer.InitializeAsync();
        Assert.True(File.Exists(_paths.DatabasePath));
        Assert.Equal((long)DatabaseInitializer.CurrentSchemaVersion, await ScalarAsync("PRAGMA user_version;"));
        Assert.Equal(17L, await ScalarAsync(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';"));
        foreach (var row in TableColumns)
            Assert.Equal(0L, await ScalarAsync($"SELECT COUNT(*) FROM [{row[0]}];"));
        Assert.Equal("ok", await ScalarAsync("PRAGMA integrity_check;"));
    }

    [Theory]
    [MemberData(nameof(TableColumns))]
    public async Task Schema_contains_all_planned_columns_including_reserved_fields(string table, string columns)
    {
        await Initializer.InitializeAsync();
        await using var connection = await Factory.OpenConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info([{table}]);";
        await using var reader = await command.ExecuteReaderAsync();
        var names = new List<string>();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(1));
            Assert.Contains(reader.GetString(2), new[] { "TEXT", "INTEGER" });
        }
        Assert.Equal(columns.Split(' '), names);
    }

    [Fact]
    public async Task Reinitialization_preserves_data_and_SQL_defaults()
    {
        await Initializer.InitializeAsync();
        await ExecuteAsync("""
            INSERT INTO Tasks (Id, Title, ScheduledAt, Source, Level, CreatedAt, UpdatedAt)
            VALUES ('local-1', '本機提醒', '2026-09-15T09:00:00', '本機', '中級',
                '2026-09-15T08:00:00', '2026-09-15T08:00:00');
            INSERT INTO AcknowledgementLogs (Id, TaskId, DeviceId, TriggeredAt, Result)
            VALUES ('ack-1', 'local-1', 'IT-001', '2026-09-15T09:00:00', 'Acknowledged');
            INSERT INTO Settings (Key, Value) VALUES ('SoundEnabled_Mid', 'false');
            INSERT INTO PomodoroSettings (Key, Value) VALUES ('SoundEnabled', 'true');
            INSERT INTO Users (Username, PasswordHash, Salt) VALUES ('admin-test', 'test-hash', 'test-salt');
            INSERT INTO Holidays (Date, Type) VALUES ('2026-09-15', '其他');
            """);
        await Initializer.InitializeAsync();
        await new DatabaseInitializer(Factory).InitializeAsync();
        Assert.Equal("本機提醒", await ScalarAsync("SELECT Title FROM Tasks;"));
        Assert.Equal(1L, await ScalarAsync("SELECT COUNT(*) FROM Tasks;"));
        Assert.Equal("1|0|0|None|0", await ScalarAsync(
            "SELECT Enabled || '|' || IsTriggered || '|' || RequireAcknowledgement || '|' || Recurrence || '|' || SkipOnHoliday FROM Tasks;"));
        Assert.Equal(DBNull.Value, await ScalarAsync("SELECT SyncedAt FROM AcknowledgementLogs;"));
        Assert.Equal("NotApplicable", await ScalarAsync("SELECT SyncStatus FROM AcknowledgementLogs;"));
        Assert.Equal("false", await ScalarAsync("SELECT Value FROM Settings;"));
        Assert.Equal(0L, await ScalarAsync("SELECT Locked FROM Settings;"));
        Assert.Equal("true", await ScalarAsync("SELECT Value FROM PomodoroSettings;"));
        Assert.Equal(1L, await ScalarAsync("SELECT Enabled FROM Users;"));
        Assert.Equal("Sheet", await ScalarAsync("SELECT Source FROM Holidays;"));
    }

    [Fact]
    public async Task Previous_development_schema_is_rejected_without_modifying_data()
    {
        await ExecuteAsync("""
            CREATE TABLE LegacyData (Value TEXT);
            INSERT INTO LegacyData VALUES ('keep');
            PRAGMA user_version=99;
            """);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Initializer.InitializeAsync());
        Assert.Contains("newer than supported schema", error.Message);
        Assert.Equal(99L, await ScalarAsync("PRAGMA user_version;"));
        Assert.Equal("keep", await ScalarAsync("SELECT Value FROM LegacyData;"));
        Assert.Equal(0L, await ScalarAsync("SELECT COUNT(*) FROM sqlite_master WHERE name = 'Tasks';"));
    }

    [Theory]
    [InlineData("INSERT INTO LunarCalendar (Date, LunarDay) VALUES ('2026-09-15', 5), ('2026-09-15', 6);")]
    [InlineData("INSERT INTO Users (Username, PasswordHash, Salt) VALUES ('admin', 'hash', 'salt'), ('admin', 'hash', 'salt');")]
    [InlineData("INSERT INTO Settings (Key) VALUES (NULL);")]
    [InlineData("INSERT INTO Tasks (Id) VALUES ('missing-required-fields');")]
    public async Task Uniqueness_and_required_fields_are_enforced(string sql)
    {
        await Initializer.InitializeAsync();
        var exception = await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(sql));
        Assert.Equal(19, exception.SqliteErrorCode);
    }

    [Fact]
    public async Task Future_schema_is_rejected_without_modifying_existing_data_or_version()
    {
        await ExecuteAsync("CREATE TABLE FutureData (Value TEXT); INSERT INTO FutureData VALUES ('keep'); PRAGMA user_version = 99;");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Initializer.InitializeAsync());
        Assert.Equal(99L, await ScalarAsync("PRAGMA user_version;"));
        Assert.Equal("keep", await ScalarAsync("SELECT Value FROM FutureData;"));
        Assert.Equal(0L, await ScalarAsync("SELECT COUNT(*) FROM sqlite_master WHERE name = 'Tasks';"));
    }

    [Fact]
    public async Task Unknown_unversioned_database_is_not_silently_adopted()
    {
        await ExecuteAsync("CREATE TABLE Tasks (Id TEXT); INSERT INTO Tasks VALUES ('keep');");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Initializer.InitializeAsync());
        Assert.Equal(0L, await ScalarAsync("PRAGMA user_version;"));
        Assert.Equal("keep", await ScalarAsync("SELECT Id FROM Tasks;"));
    }

    [Fact]
    public async Task Missing_column_in_current_schema_is_reported_without_recreating_database()
    {
        await Initializer.InitializeAsync();
        await ExecuteAsync("ALTER TABLE Employees DROP COLUMN Department;");
        await Assert.ThrowsAsync<SqliteException>(() => Initializer.InitializeAsync());
        Assert.Equal((long)DatabaseInitializer.CurrentSchemaVersion, await ScalarAsync("PRAGMA user_version;"));
    }

    [Fact]
    public async Task Cancelled_initialization_does_not_create_database()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Initializer.InitializeAsync(cancellation.Token));
        Assert.False(File.Exists(_paths.DatabasePath));
    }

    [Fact]
    public async Task Concurrent_initializers_converge_on_one_schema()
    {
        await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => Task.Run(() => new DatabaseInitializer(Factory).InitializeAsync())));
        Assert.Equal((long)DatabaseInitializer.CurrentSchemaVersion, await ScalarAsync("PRAGMA user_version;"));
        Assert.Equal("ok", await ScalarAsync("PRAGMA integrity_check;"));
    }

    [Fact]
    public async Task Corrupt_file_is_reported_and_not_replaced()
    {
        Directory.CreateDirectory(_paths.DataDirectory);
        byte[] original = "this is not a SQLite database"u8.ToArray();
        await File.WriteAllBytesAsync(_paths.DatabasePath, original);
        await Assert.ThrowsAsync<SqliteException>(() => Initializer.InitializeAsync());
        Assert.Equal(original, await File.ReadAllBytesAsync(_paths.DatabasePath));
    }

    [Fact]
    public async Task Every_connection_enables_foreign_keys_and_has_bounded_lock_timeout()
    {
        for (var i = 0; i < 2; i++)
        {
            await using var connection = await Factory.OpenConnectionAsync();
            Assert.Equal(5, connection.DefaultTimeout);
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA foreign_keys;";
            Assert.Equal(1L, await command.ExecuteScalarAsync());
        }
    }

    [Fact]
    public async Task Failed_schema_validation_rolls_back_DDL_and_version_together()
    {
        // A temporary table shadows the valid main Tasks table, forcing validation to fail
        // after CREATE TABLE and user_version have executed within the transaction.
        var initializer = new DatabaseInitializer(new ShadowingFactory(Factory));
        await Assert.ThrowsAsync<SqliteException>(() => initializer.InitializeAsync());
        Assert.Equal(0L, await ScalarAsync("PRAGMA user_version;"));
        Assert.Equal(0L, await ScalarAsync(
            "SELECT COUNT(*) FROM sqlite_master WHERE name NOT LIKE 'sqlite_%';"));
        // A clean retry must succeed without repairing partial DDL.
        await Initializer.InitializeAsync();
        Assert.Equal((long)DatabaseInitializer.CurrentSchemaVersion, await ScalarAsync("PRAGMA user_version;"));
    }

    [Fact]
    public async Task Released_v1_database_upgrades_in_place_preserving_identity_tasks_credentials_and_history()
    {
        using var stream = typeof(DatabaseInitializer).Assembly.GetManifestResourceStream("CloudAlarmOverlay.Data.Migrations.V1.sql")!;
        using var reader = new StreamReader(stream);
        await ExecuteAsync(await reader.ReadToEndAsync());
        await ExecuteAsync("""
            PRAGMA user_version=1;
            INSERT INTO Tasks(Id,Title,ScheduledAt,Source,Level,CreatedAt,UpdatedAt)
            VALUES('keep','既有任務','2026-09-17T09:00:00','本機','一般提醒','2026-09-17T08:00:00','2026-09-17T08:00:00');
            INSERT INTO Devices(DeviceId,DisplayName) VALUES('TEST-KEEP','既有使用者');
            INSERT INTO Users(Username,PasswordHash,Salt) VALUES('keep-admin','hash-keep','salt-keep');
            INSERT INTO Settings(Key,Value) VALUES('ThemeMode','深色');
            INSERT INTO AcknowledgementLogs(Id,TaskId,DeviceId,TriggeredAt,Result)
            VALUES('old-history','keep','TEST-KEEP','2026-09-17T09:00:00','Acknowledged');
            INSERT INTO SyncLogs(Time,Status,Message,Source) VALUES('2026-09-17T09:00:00','成功','既有同步','SheetB/Tasks');
            """);
        await Initializer.InitializeAsync();
        await Initializer.InitializeAsync();
        Assert.Equal((long)DatabaseInitializer.CurrentSchemaVersion, await ScalarAsync("PRAGMA user_version;"));
        Assert.Equal("既有任務", await ScalarAsync("SELECT Title FROM Tasks;"));
        Assert.Equal("TEST-KEEP", await ScalarAsync("SELECT DeviceId FROM Devices;"));
        Assert.Equal("hash-keep", await ScalarAsync("SELECT PasswordHash FROM Users;"));
        Assert.Equal("深色", await ScalarAsync("SELECT Value FROM Settings;"));
        Assert.Equal("Acknowledged", await ScalarAsync("SELECT Result FROM AcknowledgementLogs;"));
        Assert.Equal(DBNull.Value, await ScalarAsync("SELECT TaskSnapshotJson FROM AcknowledgementLogs;"));
        Assert.Equal("Legacy", await ScalarAsync("SELECT EventKind FROM SyncLogs;"));
        Assert.Equal("ok", await ScalarAsync("PRAGMA integrity_check;"));
    }

    [Fact]
    public async Task Released_v2_upgrades_without_changing_existing_rows_and_can_reopen_countdowns()
    {
        foreach (var migration in new[] { "V1", "V2" })
        {
            using var stream = typeof(DatabaseInitializer).Assembly.GetManifestResourceStream($"CloudAlarmOverlay.Data.Migrations.{migration}.sql")!;
            using var reader = new StreamReader(stream);
            await ExecuteAsync(await reader.ReadToEndAsync());
        }
        await ExecuteAsync("""
            PRAGMA user_version=2;
            INSERT INTO Devices(DeviceId,DisplayName) VALUES('KEEP','使用者');
            INSERT INTO Settings(Key,Value) VALUES('ThemeColorStyle','粉紅色');
            INSERT INTO Tasks(Id,Title,ScheduledAt,Source,Level,CreatedAt,UpdatedAt)
            VALUES('existing','既有任務','2026-09-18T09:00:00','本機','一般提醒','2026-09-18','2026-09-18');
            """);
        await Initializer.InitializeAsync();
        await ExecuteAsync("INSERT INTO Countdowns(Id,Title,TargetAt,Mode,IsPinned,CreatedAt) VALUES('pinned','中秋節','2026-09-25T00:00:00','Days',1,'2026-09-18T00:00:00');");
        await Initializer.InitializeAsync();
        Assert.Equal((long)DatabaseInitializer.CurrentSchemaVersion, await ScalarAsync("PRAGMA user_version;"));
        Assert.Equal("KEEP", await ScalarAsync("SELECT DeviceId FROM Devices;"));
        Assert.Equal("粉紅色", await ScalarAsync("SELECT Value FROM Settings;"));
        Assert.Equal("既有任務", await ScalarAsync("SELECT Title FROM Tasks;"));
        Assert.Equal(1L, await ScalarAsync("SELECT IsPinned FROM Countdowns;"));
        Assert.Equal("ok", await ScalarAsync("PRAGMA integrity_check;"));
    }

    [Fact]
    public async Task V3_upgrade_limits_legacy_home_pins_without_deleting_countdowns()
    {
        foreach(var migration in new[]{"V1","V2","V3"})
        {
            using var stream=typeof(DatabaseInitializer).Assembly.GetManifestResourceStream($"CloudAlarmOverlay.Data.Migrations.{migration}.sql")!;
            using var reader=new StreamReader(stream);
            await ExecuteAsync(await reader.ReadToEndAsync());
        }
        await ExecuteAsync("""
            PRAGMA user_version=3;
            INSERT INTO Countdowns VALUES
            ('late','晚','2026-10-01','Days',1,'2026-09-18'),
            ('first','早','2026-09-20','Days',1,'2026-09-18'),
            ('second','中','2026-09-25','Days',1,'2026-09-18');
            """);
        await Initializer.InitializeAsync();await Initializer.InitializeAsync();
        Assert.Equal(3L,await ScalarAsync("SELECT COUNT(*) FROM Countdowns;"));
        Assert.Equal(2L,await ScalarAsync("SELECT COUNT(*) FROM Countdowns WHERE IsPinned=1;"));
        Assert.Equal(0L,await ScalarAsync("SELECT IsPinned FROM Countdowns WHERE Id='late';"));
        Assert.Equal(0L,await ScalarAsync("SELECT SUM(IsTop) FROM Countdowns;"));
        Assert.Equal(3L,await ScalarAsync("SELECT COUNT(*) FROM Countdowns WHERE Category='工作' AND Repeat='None' AND ReminderDays=-1 AND ReminderMinutes=540 AND CompletedAt IS NULL AND Notes='';"));
    }

    [Fact]
    public async Task V5_countdowns_upgrade_with_default_direction_and_format_without_losing_rows()
    {
        foreach (var migration in new[] { "V1", "V2", "V3", "V4", "V5" })
        {
            using var stream = typeof(DatabaseInitializer).Assembly.GetManifestResourceStream($"CloudAlarmOverlay.Data.Migrations.{migration}.sql")!;
            using var reader = new StreamReader(stream);
            await ExecuteAsync(await reader.ReadToEndAsync());
        }
        await ExecuteAsync("PRAGMA user_version=5; INSERT INTO Countdowns(Id,Title,TargetAt,Mode,IsPinned,CreatedAt) VALUES('old','原有事件','2026-09-25','Days',1,'2026-09-18');");
        await Initializer.InitializeAsync(); await Initializer.InitializeAsync();
        Assert.Equal(1L, await ScalarAsync("SELECT COUNT(*) FROM Countdowns WHERE Id='old' AND Title='原有事件' AND IsPinned=1 AND Direction='Down' AND DisplayFormat='Days';"));
    }

    private sealed class ShadowingFactory(ISqliteConnectionFactory inner) : ISqliteConnectionFactory
    {
        public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
        {
            var connection = await inner.OpenConnectionAsync(cancellationToken);
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TEMP TABLE Tasks (Id TEXT);";
            await command.ExecuteNonQueryAsync(cancellationToken);
            return connection;
        }
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = await Factory.OpenConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private async Task<object?> ScalarAsync(string sql)
    {
        await using var connection = await Factory.OpenConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    public void Dispose()
    {
        // Only the unique directory created by this fixture is removed.
        if (Directory.Exists(_paths.DataDirectory))
            Directory.Delete(_paths.DataDirectory, recursive: true);
    }

    private sealed class TestPaths : IAppPaths
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(),
            "CloudAlarmOverlay.Tests", Guid.NewGuid().ToString("N"), "繁體中文資料");
        public string DatabasePath => Path.Combine(DataDirectory, "test.db");
    }
}

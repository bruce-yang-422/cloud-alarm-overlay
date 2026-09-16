using CloudAlarmOverlay.Core.Services;
using Microsoft.Data.Sqlite;

namespace CloudAlarmOverlay.Data.Tests;

public sealed class DatabaseInitializerTests : IDisposable
{
    private readonly TestPaths _paths = new();
    private SqliteConnectionFactory Factory => new(_paths);
    private DatabaseInitializer Initializer => new(Factory);

    // Independent inventory from AI_PLAN §3.1 / main plan §21, not extracted from migration SQL.
    public static TheoryData<string, string> TableColumns => new()
    {
        { "Tasks", "Id ExternalId Title Description ScheduledAt Source Level Enabled IsTriggered RequireAcknowledgement TargetDeviceOrName ExcludeDeviceOrName Recurrence SkipOnHoliday CreatedAt UpdatedAt Note" },
        { "Holidays", "Id Date Type Note Source" },
        { "LunarCalendar", "Id Date LunarDate LunarDay SolarTerm" },
        { "AcknowledgementLogs", "Id TaskId DeviceId DisplayName TriggeredAt AcknowledgedAt DurationSeconds Result SyncedAt SyncStatus TaskName ScheduledAt Source" },
        { "Devices", "Id DeviceId DisplayName LastSeen Version" },
        { "Users", "Id Username DisplayName PasswordHash Salt Enabled" },
        { "Employees", "Id DeviceId Name Department MaxAllowedLevel RequireAckOverride" },
        { "Settings", "Key Value Locked" },
        { "PomodoroSettings", "Key Value" },
        { "PomodoroLog", "Id Type StartedAt CompletedAt Result EndedAt PlannedMinutes IsActive" },
        { "SyncLogs", "Id Time Status Message RecordCount Source" },
        { "AuditLogs", "Id UserId Action OldValue NewValue CreatedAt" },
        { "Occurrences", "Id TaskId ScheduledAt State TriggeredAt" },
        { "SystemEvents", "Id Time EventType Message" }
    };

    [Fact]
    public async Task Fresh_database_has_version_five_and_exactly_fourteen_empty_business_tables()
    {
        Assert.False(File.Exists(_paths.DatabasePath));
        await Initializer.InitializeAsync();
        Assert.True(File.Exists(_paths.DatabasePath));
        Assert.Equal(5L, await ScalarAsync("PRAGMA user_version;"));
        Assert.Equal(14L, await ScalarAsync(
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
    public async Task Version_five_converts_old_levels_and_preserves_sound_preferences()
    {
        await Initializer.InitializeAsync();
        await ExecuteAsync("""
            INSERT INTO Tasks(Id,Title,ScheduledAt,Source,Level,CreatedAt,UpdatedAt)
            VALUES ('old', '舊任務', '2026-09-15T09:00:00', '本機', '高級', '2026-09-14', '2026-09-14');
            INSERT INTO Employees(DeviceId,MaxAllowedLevel) VALUES ('PC-1','中級');
            INSERT INTO Settings(Key,Value,Locked) VALUES ('Sound:最高級','{"Enabled":true,"Name":"Chime"}',0);
            PRAGMA user_version=4;
            """);
        await Initializer.InitializeAsync();
        Assert.Equal(5L,await ScalarAsync("PRAGMA user_version;"));
        Assert.Equal("緊急提醒",await ScalarAsync("SELECT Level FROM Tasks WHERE Id='old';"));
        Assert.Equal("重要提醒",await ScalarAsync("SELECT MaxAllowedLevel FROM Employees WHERE DeviceId='PC-1';"));
        Assert.Equal("{\"Enabled\":true,\"Name\":\"Chime\"}",await ScalarAsync("SELECT Value FROM Settings WHERE Key='Sound:強制通知';"));
        Assert.Equal(0L,await ScalarAsync("SELECT COUNT(*) FROM Settings WHERE Key='Sound:最高級';"));
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
        Assert.Equal(5L, await ScalarAsync("PRAGMA user_version;"));
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
        Assert.Equal(5L, await ScalarAsync("PRAGMA user_version;"));
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
        Assert.Equal(5L, await ScalarAsync("PRAGMA user_version;"));
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

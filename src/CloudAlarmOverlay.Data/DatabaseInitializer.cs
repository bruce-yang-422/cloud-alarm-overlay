using CloudAlarmOverlay.Core.Services;
using Microsoft.Data.Sqlite;

namespace CloudAlarmOverlay.Data;

public sealed class DatabaseInitializer(ISqliteConnectionFactory connections) : IDatabaseInitializer
{
    public const int CurrentSchemaVersion = 10;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        // An immediate transaction serializes competing initializers across processes.
        // Schema DDL and user_version commit together, or both roll back.
        using var transaction = connection.BeginTransaction(deferred: false);
        var version = Convert.ToInt32(await ExecuteScalarAsync(
            connection, transaction, "PRAGMA user_version;", cancellationToken).ConfigureAwait(false));

        if (version > CurrentSchemaVersion)
            throw new InvalidOperationException(
                $"Database schema {version} is newer than supported schema {CurrentSchemaVersion}. No changes were made.");

        if (version == 0)
        {
            var existingObjects = Convert.ToInt32(await ExecuteScalarAsync(connection, transaction,
                "SELECT COUNT(*) FROM sqlite_master WHERE name NOT LIKE 'sqlite_%';",
                cancellationToken).ConfigureAwait(false));
            if (existingObjects != 0)
                throw new InvalidOperationException(
                    "An unversioned database already contains objects. Refusing to initialize an unknown schema.");

            using var stream = typeof(DatabaseInitializer).Assembly.GetManifestResourceStream(
                "CloudAlarmOverlay.Data.Migrations.V1.sql")
                ?? throw new InvalidOperationException("Embedded schema V1.sql was not found.");
            using var reader = new StreamReader(stream);
            var sql = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await ExecuteNonQueryAsync(connection, transaction, sql, cancellationToken).ConfigureAwait(false);
            await ExecuteNonQueryAsync(connection, transaction, "PRAGMA user_version = 1;",
                cancellationToken).ConfigureAwait(false);
        }
        if(version<2)
        {
            using var upgrade=typeof(DatabaseInitializer).Assembly.GetManifestResourceStream("CloudAlarmOverlay.Data.Migrations.V2.sql")
                ?? throw new InvalidOperationException("Embedded schema V2.sql was not found.");
            using var reader=new StreamReader(upgrade);
            await ExecuteNonQueryAsync(connection,transaction,await reader.ReadToEndAsync(cancellationToken),cancellationToken);
            await ExecuteNonQueryAsync(connection,transaction,"PRAGMA user_version = 2;",cancellationToken);
        }
        if(version<3)
        {
            using var upgrade=typeof(DatabaseInitializer).Assembly.GetManifestResourceStream("CloudAlarmOverlay.Data.Migrations.V3.sql")
                ?? throw new InvalidOperationException("Embedded schema V3.sql was not found.");
            using var reader=new StreamReader(upgrade);
            await ExecuteNonQueryAsync(connection,transaction,await reader.ReadToEndAsync(cancellationToken),cancellationToken);
            await ExecuteNonQueryAsync(connection,transaction,"PRAGMA user_version = 3;",cancellationToken);
        }
        if(version<4)
        {
            using var upgrade=typeof(DatabaseInitializer).Assembly.GetManifestResourceStream("CloudAlarmOverlay.Data.Migrations.V4.sql")
                ?? throw new InvalidOperationException("Embedded schema V4.sql was not found.");
            using var reader=new StreamReader(upgrade);
            await ExecuteNonQueryAsync(connection,transaction,await reader.ReadToEndAsync(cancellationToken),cancellationToken);
            await ExecuteNonQueryAsync(connection,transaction,"PRAGMA user_version = 4;",cancellationToken);
        }
        if(version<5)
        {
            using var upgrade=typeof(DatabaseInitializer).Assembly.GetManifestResourceStream("CloudAlarmOverlay.Data.Migrations.V5.sql")
                ?? throw new InvalidOperationException("Embedded schema V5.sql was not found.");
            using var reader=new StreamReader(upgrade);
            await ExecuteNonQueryAsync(connection,transaction,await reader.ReadToEndAsync(cancellationToken),cancellationToken);
            await ExecuteNonQueryAsync(connection,transaction,"PRAGMA user_version = 5;",cancellationToken);
        }
        if(version<6)
        {
            using var upgrade=typeof(DatabaseInitializer).Assembly.GetManifestResourceStream("CloudAlarmOverlay.Data.Migrations.V6.sql")
                ?? throw new InvalidOperationException("Embedded schema V6.sql was not found.");
            using var reader=new StreamReader(upgrade);
            await ExecuteNonQueryAsync(connection,transaction,await reader.ReadToEndAsync(cancellationToken),cancellationToken);
            await ExecuteNonQueryAsync(connection,transaction,"PRAGMA user_version = 6;",cancellationToken);
        }
        if(version<7)
        {
            using var upgrade=typeof(DatabaseInitializer).Assembly.GetManifestResourceStream("CloudAlarmOverlay.Data.Migrations.V7.sql")
                ?? throw new InvalidOperationException("Embedded schema V7.sql was not found.");
            using var reader=new StreamReader(upgrade);
            await ExecuteNonQueryAsync(connection,transaction,await reader.ReadToEndAsync(cancellationToken),cancellationToken);
            await ExecuteNonQueryAsync(connection,transaction,"PRAGMA user_version = 7;",cancellationToken);
        }
        if(version<8)
        {
            using var upgrade=typeof(DatabaseInitializer).Assembly.GetManifestResourceStream("CloudAlarmOverlay.Data.Migrations.V8.sql")
                ?? throw new InvalidOperationException("Embedded schema V8.sql was not found.");
            using var reader=new StreamReader(upgrade);
            await ExecuteNonQueryAsync(connection,transaction,await reader.ReadToEndAsync(cancellationToken),cancellationToken);
            await ExecuteNonQueryAsync(connection,transaction,"PRAGMA user_version = 8;",cancellationToken);
        }
        if(version<9)
        {
            using var upgrade=typeof(DatabaseInitializer).Assembly.GetManifestResourceStream("CloudAlarmOverlay.Data.Migrations.V9.sql")
                ?? throw new InvalidOperationException("Embedded schema V9.sql was not found.");
            using var reader=new StreamReader(upgrade);
            await ExecuteNonQueryAsync(connection,transaction,await reader.ReadToEndAsync(cancellationToken),cancellationToken);
            await ExecuteNonQueryAsync(connection,transaction,"PRAGMA user_version = 9;",cancellationToken);
        }
        if(version<10)
        {
            using var upgrade=typeof(DatabaseInitializer).Assembly.GetManifestResourceStream("CloudAlarmOverlay.Data.Migrations.V10.sql")!;
            using var reader=new StreamReader(upgrade);
            await ExecuteNonQueryAsync(connection,transaction,await reader.ReadToEndAsync(cancellationToken),cancellationToken);
            await ExecuteNonQueryAsync(connection,transaction,"PRAGMA user_version = 10;",cancellationToken);
        }
        // Fail before displaying the main window if an expected table/column is missing.
        using (var validation = connection.CreateCommand())
        {
            validation.Transaction = transaction;
            validation.CommandText = SchemaValidationSql;
            await using var reader = await validation.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            // Advance through every result so all SELECT statements are prepared.
            while (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false)) { }
        }
        cancellationToken.ThrowIfCancellationRequested();
        transaction.Commit();
    }

    private static async Task<object?> ExecuteScalarAsync(SqliteConnection connection,
        SqliteTransaction transaction, string sql, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExecuteNonQueryAsync(SqliteConnection connection,
        SqliteTransaction transaction, string sql, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private const string SchemaValidationSql = """
        SELECT Id, ExternalId, Title, Description, ScheduledAt, Source, Level, Enabled,
            IsTriggered, RequireAcknowledgement, TargetDeviceOrName, ExcludeDeviceOrName,
            Recurrence, SkipOnHoliday, CreatedAt, UpdatedAt, Note FROM Tasks LIMIT 0;
        SELECT Id, Date, Type, Note, Source FROM Holidays LIMIT 0;
        SELECT Id, Date, LunarDate, LunarDay, SolarTerm FROM LunarCalendar LIMIT 0;
        SELECT Id, TaskId, DeviceId, DisplayName, TriggeredAt, AcknowledgedAt,
            DurationSeconds, Result, SyncedAt, SyncStatus, TaskName, ScheduledAt, Source, TaskSnapshotJson, SnoozeCount FROM AcknowledgementLogs LIMIT 0;
        SELECT Id, TaskId, ScheduledAt, State, TriggeredAt, SnoozedUntil FROM Occurrences LIMIT 0;
        SELECT Id, DeviceId, DisplayName, LastSeen, Version FROM Devices LIMIT 0;
        SELECT Id, Username, DisplayName, PasswordHash, Salt, Enabled FROM Users LIMIT 0;
        SELECT Id, DeviceId, Name, Department, MaxAllowedLevel, RequireAckOverride FROM Employees LIMIT 0;
        SELECT Key, Value, Locked FROM Settings LIMIT 0;
        SELECT Key, Value FROM PomodoroSettings LIMIT 0;
        SELECT Id, Type, StartedAt, CompletedAt, EndedAt, PlannedMinutes, IsActive, Result FROM PomodoroLog LIMIT 0;
        SELECT Source, Fingerprint, ConfigFingerprint, Status, Message, LastCheckedAt, LastSuccessAt, ActiveLogId FROM SyncStates LIMIT 0;
        SELECT Id, Time, Status, Message, RecordCount, Source, LastSeenAt, RepeatCount, EventKind FROM SyncLogs LIMIT 0;
        SELECT Id, UserId, Action, OldValue, NewValue, CreatedAt FROM AuditLogs LIMIT 0;
        SELECT Id, Time, EventType, Message FROM SystemEvents LIMIT 0;
        SELECT Id, Title, TargetAt, Mode, IsPinned, CreatedAt, IsTop, Category, Repeat, ReminderDays, ReminderMinutes, Notes, CompletedAt, ReminderChangedAt, Direction, DisplayFormat, Recurrence, SkipOnHoliday FROM Countdowns LIMIT 0;
        SELECT TaskId FROM TaskHomePins LIMIT 0;
        """;
}

using CloudAlarmOverlay.Core.Services;
using Microsoft.Data.Sqlite;

namespace CloudAlarmOverlay.Data;

public sealed class DatabaseInitializer(ISqliteConnectionFactory connections) : IDatabaseInitializer
{
    public const int CurrentSchemaVersion = 5;

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
                    "An unversioned database already contains objects. Refusing to mark an unknown schema as version 1.");

            using var stream = typeof(DatabaseInitializer).Assembly.GetManifestResourceStream(
                "CloudAlarmOverlay.Data.Migrations.V1.sql")
                ?? throw new InvalidOperationException("Embedded schema V1.sql was not found.");
            using var reader = new StreamReader(stream);
            var sql = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await ExecuteNonQueryAsync(connection, transaction, sql, cancellationToken).ConfigureAwait(false);
            await ExecuteNonQueryAsync(connection, transaction, "PRAGMA user_version = 1;",
                cancellationToken).ConfigureAwait(false);
        }

        if (version < 2)
        {
            using var stream = typeof(DatabaseInitializer).Assembly.GetManifestResourceStream(
                "CloudAlarmOverlay.Data.Migrations.V2.sql")!;
            using var reader = new StreamReader(stream);
            await ExecuteNonQueryAsync(connection, transaction,
                await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
            await ExecuteNonQueryAsync(connection, transaction, "PRAGMA user_version = 2;",
                cancellationToken).ConfigureAwait(false);
        }
        if (version < 3)
        {
            using var stream = typeof(DatabaseInitializer).Assembly.GetManifestResourceStream("CloudAlarmOverlay.Data.Migrations.V3.sql")!;
            using var reader = new StreamReader(stream);
            await ExecuteNonQueryAsync(connection, transaction, await reader.ReadToEndAsync(cancellationToken), cancellationToken);
            await ExecuteNonQueryAsync(connection, transaction, "PRAGMA user_version = 3;", cancellationToken);
        }
        if (version < 4)
        {
            using var stream = typeof(DatabaseInitializer).Assembly.GetManifestResourceStream("CloudAlarmOverlay.Data.Migrations.V4.sql")!;
            using var reader = new StreamReader(stream);
            await ExecuteNonQueryAsync(connection, transaction, await reader.ReadToEndAsync(cancellationToken), cancellationToken);
            await ExecuteNonQueryAsync(connection, transaction, "PRAGMA user_version = 4;", cancellationToken);
        }
        if (version < 5)
        {
            using var stream = typeof(DatabaseInitializer).Assembly.GetManifestResourceStream("CloudAlarmOverlay.Data.Migrations.V5.sql")!;
            using var reader = new StreamReader(stream);
            await ExecuteNonQueryAsync(connection, transaction, await reader.ReadToEndAsync(cancellationToken), cancellationToken);
            await ExecuteNonQueryAsync(connection, transaction, "PRAGMA user_version = 5;", cancellationToken);
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
            DurationSeconds, Result, SyncedAt, SyncStatus, TaskName, ScheduledAt, Source FROM AcknowledgementLogs LIMIT 0;
        SELECT Id, TaskId, ScheduledAt, State, TriggeredAt FROM Occurrences LIMIT 0;
        SELECT Id, DeviceId, DisplayName, LastSeen, Version FROM Devices LIMIT 0;
        SELECT Id, Username, DisplayName, PasswordHash, Salt, Enabled FROM Users LIMIT 0;
        SELECT Id, DeviceId, Name, Department, MaxAllowedLevel, RequireAckOverride FROM Employees LIMIT 0;
        SELECT Key, Value, Locked FROM Settings LIMIT 0;
        SELECT Key, Value FROM PomodoroSettings LIMIT 0;
        SELECT Id, Type, StartedAt, CompletedAt, EndedAt, PlannedMinutes, IsActive, Result FROM PomodoroLog LIMIT 0;
        SELECT Id, Time, Status, Message, RecordCount, Source FROM SyncLogs LIMIT 0;
        SELECT Id, UserId, Action, OldValue, NewValue, CreatedAt FROM AuditLogs LIMIT 0;
        SELECT Id, Time, EventType, Message FROM SystemEvents LIMIT 0;
        """;
}

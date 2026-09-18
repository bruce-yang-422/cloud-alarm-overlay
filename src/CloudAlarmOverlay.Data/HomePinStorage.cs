using CloudAlarmOverlay.Core.Models;
using Dapper;
using Microsoft.Data.Sqlite;

namespace CloudAlarmOverlay.Data;

internal static class HomePinStorage
{
    public const string CountSql = "SELECT (SELECT COUNT(*) FROM Countdowns WHERE IsPinned=1) + (SELECT COUNT(*) FROM TaskHomePins);";
    public static async Task<int> ReadLimitAsync(SqliteConnection connection, SqliteTransaction? transaction = null, CancellationToken ct = default)
        => HomePinOptions.ReadLimit(await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT Value FROM Settings WHERE Key=@key;", new { key = HomePinOptions.SettingKey }, transaction, cancellationToken: ct)));
}

using Microsoft.Data.Sqlite;

namespace CloudAlarmOverlay.Data;

public interface ISqliteConnectionFactory
{
    // Caller owns and must dispose the opened connection.
    Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken = default);
}


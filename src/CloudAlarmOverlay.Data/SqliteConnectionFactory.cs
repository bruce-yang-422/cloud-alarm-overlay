using CloudAlarmOverlay.Core.Services;
using Microsoft.Data.Sqlite;

namespace CloudAlarmOverlay.Data;

public sealed class SqliteConnectionFactory(IAppPaths paths) : ISqliteConnectionFactory
{
    public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(paths.DatabasePath)
            ?? throw new InvalidOperationException("Database path must have a parent directory."));

        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            DefaultTimeout = 5,
            // Each operation owns its connection; no idle pooled file handles at shutdown.
            Pooling = false
        }.ToString());
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}


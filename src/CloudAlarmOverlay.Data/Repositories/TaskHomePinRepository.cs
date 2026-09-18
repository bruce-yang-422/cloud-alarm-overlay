using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using Dapper;

namespace CloudAlarmOverlay.Data.Repositories;

internal sealed class TaskHomePinRepository(Database db) : ITaskHomePinRepository
{
    public async Task<int> GetLimitAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        return await HomePinStorage.ReadLimitAsync(connection, ct: cancellationToken);
    }
    public Task<IReadOnlyList<string>> GetTaskIdsAsync(CancellationToken cancellationToken = default)
        => db.QueryAsync<string>("SELECT TaskId FROM TaskHomePins ORDER BY TaskId;", ct:cancellationToken);

    public async Task SetPinnedAsync(string taskId, bool pinned, CancellationToken cancellationToken = default)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction(deferred:false);
        var args = new { taskId };
        if (!pinned)
            await connection.ExecuteAsync(new CommandDefinition("DELETE FROM TaskHomePins WHERE TaskId=@taskId;",args,transaction,cancellationToken:cancellationToken));
        else
        {
            if (await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM Tasks WHERE Id=@taskId;",args,transaction,cancellationToken:cancellationToken)) == 0)
                throw new InvalidOperationException("任務已移除，請重新選擇。");
            var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT (SELECT COUNT(*) FROM TaskHomePins WHERE TaskId<>@taskId) + (SELECT COUNT(*) FROM Countdowns WHERE IsPinned=1);",args,transaction,cancellationToken:cancellationToken));
            var limit = await HomePinStorage.ReadLimitAsync(connection, transaction, cancellationToken);
            if (count >= limit)
                throw new InvalidOperationException(HomePinOptions.FullMessage(limit));
            await connection.ExecuteAsync(new CommandDefinition("INSERT OR IGNORE INTO TaskHomePins(TaskId) VALUES(@taskId);",args,transaction,cancellationToken:cancellationToken));
        }
        transaction.Commit();
    }
}

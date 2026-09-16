using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using Dapper;
namespace CloudAlarmOverlay.Data.Repositories;
internal sealed class EmployeeRepository(Database db) : IEmployeeRepository
{
    public Task<IReadOnlyList<Employee>> GetAllAsync(CancellationToken cancellationToken = default)
        => db.QueryAsync<Employee>("SELECT * FROM Employees;",ct:cancellationToken);
    public async Task ReplaceCacheAsync(IReadOnlyList<Employee> employees, CancellationToken cancellationToken = default)
    {
        await using var c = await db.OpenAsync(cancellationToken);
        using var tx = c.BeginTransaction();
        await c.ExecuteAsync(new CommandDefinition("DELETE FROM Employees;", transaction:tx, cancellationToken:cancellationToken));
        foreach(var row in employees)
            await c.ExecuteAsync(new CommandDefinition("INSERT INTO Employees(DeviceId,Name,Department,MaxAllowedLevel,RequireAckOverride) VALUES(@DeviceId,@Name,@Department,@MaxAllowedLevel,@RequireAckOverride);",
                Database.Parameters(row),tx,cancellationToken:cancellationToken));
        tx.Commit();
    }
}

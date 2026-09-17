using Dapper;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using CloudAlarmOverlay.Core.Services;
namespace CloudAlarmOverlay.Data.Repositories;
internal sealed class UserRepository(Database db,AdminSession session):IUserRepository
{
    public async Task<bool> AnyAsync(CancellationToken ct=default)=>(await db.QueryAsync<long>("SELECT COUNT(*) FROM Users;",ct:ct)).Single()>0;
    public async Task<User?> GetByUsernameAsync(string username,CancellationToken cancellationToken=default)=>
        (await db.QueryAsync<User>("SELECT * FROM Users WHERE Username=@username;",new {username},cancellationToken)).SingleOrDefault();
    public async Task CreateInitialAsync(User user,CancellationToken ct=default)
    {
        await using var c=await db.OpenAsync(ct);
        using var tx=c.BeginTransaction(deferred:false);
        if(await c.ExecuteScalarAsync<long>(new CommandDefinition("SELECT COUNT(*) FROM Users;",transaction:tx,cancellationToken:ct))>0)
            throw new InvalidOperationException("管理者帳號已建立，請使用既有帳號登入。");
        await c.ExecuteAsync(new CommandDefinition("INSERT INTO Users(Username,DisplayName,PasswordHash,Salt,Enabled) VALUES(@Username,@DisplayName,@PasswordHash,@Salt,1);",user,tx,cancellationToken:ct));
        await c.ExecuteAsync(new CommandDefinition("INSERT INTO AuditLogs(UserId,Action,CreatedAt) VALUES(@Username,'建立管理者帳號',@at);",new {user.Username,at=DateTime.Now.ToString("O")},tx,cancellationToken:ct));
        tx.Commit();
    }
    public async Task SaveAsync(User user,CancellationToken cancellationToken=default)
    {
        var actor=session.RequireAdmin();
        await using var c=await db.OpenAsync(cancellationToken);
        using var tx=c.BeginTransaction(deferred:false);
        session.RequireAdmin();
        var updated=await c.ExecuteAsync(new CommandDefinition("UPDATE Users SET Username=@Username,DisplayName=@DisplayName,PasswordHash=@PasswordHash,Salt=@Salt,Enabled=@Enabled WHERE Id=@Id AND Username=@actor;",
            new {user.Id,user.Username,user.DisplayName,user.PasswordHash,user.Salt,user.Enabled,actor},tx,cancellationToken:cancellationToken));
        if(updated!=1)throw new UnauthorizedAccessException("只能更新目前登入的管理者帳號。");
        await c.ExecuteAsync(new CommandDefinition("INSERT INTO AuditLogs(UserId,Action,OldValue,NewValue,CreatedAt) VALUES(@actor,'更新管理者帳號',@actor,@newUsername,@at);",
            new {actor,newUsername=user.Username,at=DateTime.Now.ToString("O")},tx,cancellationToken:cancellationToken));
        tx.Commit();
    }
}

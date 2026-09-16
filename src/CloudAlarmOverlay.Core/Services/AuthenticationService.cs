using System.Security.Cryptography;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
namespace CloudAlarmOverlay.Core.Services;
internal sealed class AuthenticationService(IUserRepository users,IAuditService audit,AdminSession session):IAuthenticationService
{
    private const int Iterations=600_000;
    public async Task ChangePasswordAsync(string username,string currentPassword,string newPassword,CancellationToken cancellationToken=default)
    {
        if(newPassword.Length is <8 or >128)throw new ArgumentException("新密碼需為 8–128 字。");
        if(!await AuthenticateAsync(username,currentPassword,cancellationToken))throw new UnauthorizedAccessException("帳號或密碼錯誤。");
        var user=(await users.GetByUsernameAsync(username.Trim(),cancellationToken))!;
        var salt=RandomNumberGenerator.GetBytes(32);
        var hash=await Task.Run(()=>Rfc2898DeriveBytes.Pbkdf2(newPassword,salt,Iterations,HashAlgorithmName.SHA256,32),cancellationToken);
        await users.SaveAsync(user with {Salt=Convert.ToBase64String(salt),PasswordHash=$"PBKDF2-SHA256:{Iterations}:{Convert.ToBase64String(hash)}"},cancellationToken);
        session.SignOut();
    }
    public async Task EnsureDefaultAdministratorAsync(CancellationToken ct=default)
    {
        if(await users.AnyAsync(ct))return;
        try { await CreateHashedAsync("admin","12345",ct); }
        catch(InvalidOperationException)
        {
            if(!await users.AnyAsync(ct))throw;
        }
    }
    public Task<bool> HasAdministratorAsync(CancellationToken ct=default)=>users.AnyAsync(ct);
    public async Task CreateInitialAsync(string username,string password,CancellationToken ct=default)
    {
        username=username.Trim();
        if(username.Length is <1 or >50||password.Length is <8 or >128)throw new ArgumentException("帳號必填且最多 50 字，密碼需為 8–128 字。");
        await CreateHashedAsync(username,password,ct);
    }
    private async Task CreateHashedAsync(string username,string password,CancellationToken ct)
    {
        var salt=RandomNumberGenerator.GetBytes(32);
        var hash=await Task.Run(()=>Rfc2898DeriveBytes.Pbkdf2(password,salt,Iterations,HashAlgorithmName.SHA256,32),ct);
        await users.CreateInitialAsync(new User{Username=username,Salt=Convert.ToBase64String(salt),PasswordHash=$"PBKDF2-SHA256:{Iterations}:{Convert.ToBase64String(hash)}"},ct);
    }
    public async Task<bool> AuthenticateAsync(string username,string password,CancellationToken cancellationToken=default)
    {
        if(string.IsNullOrWhiteSpace(username)||password.Length is <1 or >128)return false;
        var user=await users.GetByUsernameAsync(username.Trim(),cancellationToken);
        bool valid=false;
        try
        {
            if(user is {Enabled:true})
            {
                var parts=user.PasswordHash.Split(':');
                if(parts.Length==3&&parts[0]=="PBKDF2-SHA256"&&int.TryParse(parts[1],out var n)&&n is >=100_000 and <=2_000_000)
                {
                    var actual=await Task.Run(()=>Rfc2898DeriveBytes.Pbkdf2(password,Convert.FromBase64String(user.Salt),n,HashAlgorithmName.SHA256,32),cancellationToken);
                    valid=CryptographicOperations.FixedTimeEquals(actual,Convert.FromBase64String(parts[2]));
                }
            }
        }
        catch(FormatException){return false;}
        if(!valid)return false;
        await audit.RecordAsync(new AuditLogEntry{UserId=user!.Username,Action="管理者登入",CreatedAt=DateTime.Now},cancellationToken);
        session.SignIn(user.Username);
        return true;
    }
}

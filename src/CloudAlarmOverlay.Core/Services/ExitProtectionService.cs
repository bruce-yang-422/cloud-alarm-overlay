using System.Security.Cryptography;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;

namespace CloudAlarmOverlay.Core.Services;

internal sealed class ExitProtectionService(AdminSession session,ISettingsRepository settings,IAdminSettingsStore store):IExitProtectionService
{
    public const string Administrator="Administrator";
    public const string Dedicated="Dedicated";
    private const int Iterations=600_000;

    public async Task<bool> IsRequiredAsync(CancellationToken ct=default)
        =>(await settings.GetAsync("ExitPasswordRequired",ct))?.Value!="false";

    public async Task SetRequiredAsync(bool required,CancellationToken ct=default)
    {
        session.RequireAdmin();
        await store.SaveAsync([new Setting{Key="ExitPasswordRequired",Value=required?"true":"false"}],ct);
    }

    public async Task<string> GetModeAsync(CancellationToken ct=default)
        =>(await settings.GetAsync("ExitPasswordMode",ct))?.Value==Dedicated?Dedicated:Administrator;

    public async Task SetDedicatedPasswordAsync(string password,CancellationToken ct=default)
    {
        session.RequireAdmin();
        if(password.Length is <8 or >128)throw new ArgumentException("結束程式密碼需為 8–128 字。");
        var salt=RandomNumberGenerator.GetBytes(32);
        var hash=await Task.Run(()=>Rfc2898DeriveBytes.Pbkdf2(password,salt,Iterations,HashAlgorithmName.SHA256,32),ct);
        var encoded=$"PBKDF2-SHA256:{Iterations}:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
        session.RequireAdmin();
        await store.SaveAsync([
            new Setting{Key="ExitPasswordHash",Value=encoded},
            new Setting{Key="ExitPasswordMode",Value=Dedicated}
        ],ct);
    }

    public async Task UseAdministratorPasswordAsync(CancellationToken ct=default)
    {
        session.RequireAdmin();
        await store.SaveAsync([
            new Setting{Key="ExitPasswordMode",Value=Administrator},
            new Setting{Key="ExitPasswordHash",Value=null}
        ],ct);
    }

    public async Task<bool> VerifyDedicatedPasswordAsync(string password,CancellationToken ct=default)
    {
        if(password.Length is <1 or >128 || await GetModeAsync(ct)!=Dedicated)return false;
        var encoded=(await settings.GetAsync("ExitPasswordHash",ct))?.Value;
        var parts=encoded?.Split(':');
        if(parts is not {Length:4}||parts[0]!="PBKDF2-SHA256"||!int.TryParse(parts[1],out var rounds)||rounds is <100_000 or >2_000_000)return false;
        try
        {
            var salt=Convert.FromBase64String(parts[2]);
            var expected=Convert.FromBase64String(parts[3]);
            if(salt.Length!=32||expected.Length!=32)return false;
            var actual=await Task.Run(()=>Rfc2898DeriveBytes.Pbkdf2(password,salt,rounds,HashAlgorithmName.SHA256,32),ct);
            return CryptographicOperations.FixedTimeEquals(actual,expected);
        }
        catch(FormatException){return false;}
    }
}

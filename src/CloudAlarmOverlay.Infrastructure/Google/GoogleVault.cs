using System.Security.Cryptography;
using System.Text.Json;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Services;

namespace CloudAlarmOverlay.Infrastructure.Google;

internal sealed record GoogleClient(string Id,string Secret);
internal sealed record GoogleCredential
{
    public string Id {get;init;}="";
    public string Email {get;init;}="";
    public string Label {get;init;}="";
    public string AccessToken {get;init;}="";
    public string RefreshToken {get;init;}="";
    public string Scope {get;init;}="";
    public DateTimeOffset ExpiresAt {get;init;}
    public string Status {get;init;}="已連線";
}
internal sealed record GoogleVaultState
{
    public GoogleClient? Client {get;init;}
    public bool UsesBuiltInClient {get;init;}
    public List<GoogleCredential> Accounts {get;init;}=[];
    public List<GoogleSource> Sources {get;init;}=[];
}
internal interface IGoogleVault
{
    Task<GoogleVaultState> ReadAsync(CancellationToken ct);
    Task WriteAsync(GoogleVaultState state,CancellationToken ct);
}
// Separate from SQLite and backup/export payloads. DPAPI binds the complete store to this Windows user.
internal sealed class GoogleVault(IAppPaths paths):IGoogleVault
{
    private string PathName=>Path.Combine(paths.DataDirectory,"google-workspace.dat");
    public async Task<GoogleVaultState> ReadAsync(CancellationToken ct)
    {
        if(!File.Exists(PathName))return new();
        try
        {
            var bytes=ProtectedData.Unprotect(await File.ReadAllBytesAsync(PathName,ct),null,DataProtectionScope.CurrentUser);
            try{return JsonSerializer.Deserialize<GoogleVaultState>(bytes)??throw new InvalidDataException();}
            finally{CryptographicOperations.ZeroMemory(bytes);}
        }
        catch(Exception ex) when(ex is CryptographicException or JsonException or InvalidDataException)
        {throw new InvalidOperationException("無法解密 Google 帳號資料，請使用原 Windows 帳號；原檔案已保留。",ex);}
    }
    public async Task WriteAsync(GoogleVaultState state,CancellationToken ct)
    {
        Directory.CreateDirectory(paths.DataDirectory);
        var bytes=JsonSerializer.SerializeToUtf8Bytes(state);
        byte[] encrypted;
        try{encrypted=ProtectedData.Protect(bytes,null,DataProtectionScope.CurrentUser);}
        finally{CryptographicOperations.ZeroMemory(bytes);}
        var temporary=PathName+".tmp";
        await File.WriteAllBytesAsync(temporary,encrypted,ct);
        File.Move(temporary,PathName,true);
    }
}

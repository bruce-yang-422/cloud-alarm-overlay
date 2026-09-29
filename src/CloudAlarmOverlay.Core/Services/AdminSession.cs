namespace CloudAlarmOverlay.Core.Services;
public sealed class AdminSession(TimeProvider clock)
{
    private readonly object gate=new();
    private string? username;
    private long signedInAt;
    private bool personal;
    public bool IsPersonal { get { lock(gate) return personal; } }
    public static TimeSpan SessionLifetime=>TimeSpan.FromMinutes(10);
    public event Action? Changed;
    public string? Username {get{CheckExpiry();lock(gate)return username;}}
    public bool IsAuthenticated=>Username is not null;
    internal void SignIn(string name){lock(gate){if(personal)return;username=name;signedInAt=clock.GetTimestamp();}Changed?.Invoke();}
    internal void SetUsageMode(bool isPersonal)
    {
        lock(gate)
        {
            if(personal==isPersonal)return;
            personal=isPersonal;username=isPersonal?"個人使用者":null;
        }
        Changed?.Invoke();
    }
    public bool CheckExpiry()
    {
        bool expired;
        lock(gate){expired=!personal&&username is not null&&clock.GetElapsedTime(signedInAt)>=SessionLifetime;if(expired)username=null;}
        if(expired)Changed?.Invoke();
        return expired;
    }
    public void SignOut(){lock(gate){if(personal)return;username=null;}Changed?.Invoke();}
    public string RequireAdmin()=>Username??throw new UnauthorizedAccessException("請先登入管理者；登入逾時請重新驗證。");
}

namespace CloudAlarmOverlay.Core.Services;
public sealed class AdminSession(TimeProvider clock)
{
    private readonly object gate=new();
    private string? username;
    private long signedInAt;
    public static TimeSpan SessionLifetime=>TimeSpan.FromMinutes(10);
    public event Action? Changed;
    public string? Username {get{CheckExpiry();lock(gate)return username;}}
    public bool IsAuthenticated=>Username is not null;
    internal void SignIn(string name){lock(gate){username=name;signedInAt=clock.GetTimestamp();}Changed?.Invoke();}
    public bool CheckExpiry()
    {
        bool expired;
        lock(gate){expired=username is not null&&clock.GetElapsedTime(signedInAt)>=SessionLifetime;if(expired)username=null;}
        if(expired)Changed?.Invoke();
        return expired;
    }
    public void SignOut(){lock(gate)username=null;Changed?.Invoke();}
    public string RequireAdmin()=>Username??throw new UnauthorizedAccessException("請先登入管理者；登入逾時請重新驗證。");
}

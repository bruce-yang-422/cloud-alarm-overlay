namespace CloudAlarmOverlay.Core.Services;
public sealed class AdminSession(TimeProvider clock)
{
    private readonly object gate=new();
    private string? username;
    private long lastActivity;
    public static TimeSpan IdleTimeout=>TimeSpan.FromMinutes(15);
    public event Action? Changed;
    public string? Username {get{CheckExpiry();lock(gate)return username;}}
    public bool IsAuthenticated=>Username is not null;
    internal void SignIn(string name){lock(gate){username=name;lastActivity=clock.GetTimestamp();}Changed?.Invoke();}
    public void Touch(){CheckExpiry();lock(gate)if(username is not null)lastActivity=clock.GetTimestamp();}
    public bool CheckExpiry()
    {
        bool expired;
        lock(gate){expired=username is not null&&clock.GetElapsedTime(lastActivity)>=IdleTimeout;if(expired)username=null;}
        if(expired)Changed?.Invoke();
        return expired;
    }
    public void SignOut(){lock(gate)username=null;Changed?.Invoke();}
    public string RequireAdmin()=>Username??throw new UnauthorizedAccessException("請先登入管理者；登入逾時請重新驗證。");
}

namespace CloudAlarmOverlay.Core.Models;

public sealed record GoogleAccount(string Id,string Email,string Label,string Status);
public sealed record GoogleResource(string Id,string Name,string TimeZone="")
{
    public override string ToString()=>Name;
}
public sealed record GoogleSource
{
    public string Id {get;init;}=Guid.NewGuid().ToString("N");
    public string Name {get;init;}="新來源";
    public string AccountId {get;init;}="";
    public string Kind {get;init;}="Sheet";
    public string ResourceId {get;init;}="";
    public string TabId {get;init;}="";
    public bool Enabled {get;init;}=true;
    public bool AllowWrite {get;init;}
    public int IntervalMinutes {get;init;}=5;
    public int ReminderMinutes {get;init;}=10;
    public bool IncludeAllDay {get;init;}
    public int AllDayHour {get;init;}=9;
    public DateTimeOffset? LastAttempt {get;init;}
    public DateTimeOffset? LastSuccess {get;init;}
    public DateTimeOffset? RetryAfter {get;init;}
    public int FailureCount {get;init;}
    public string Status {get;init;}="尚未同步";
    public string CacheSource=>"Google:"+Id;
    public override string ToString()=>Name;
}
public sealed record GoogleWorkspaceSnapshot(bool Configured,IReadOnlyList<GoogleAccount> Accounts,IReadOnlyList<GoogleSource> Sources)
{
    public bool UsesBuiltInClient {get;init;}
}
public sealed record GoogleTaskDraft(string Token,string SourceName,string ExternalId,bool Exists,IReadOnlyDictionary<string,string> Fields);

public sealed class GoogleAuthorizationException(string message):Exception(message);

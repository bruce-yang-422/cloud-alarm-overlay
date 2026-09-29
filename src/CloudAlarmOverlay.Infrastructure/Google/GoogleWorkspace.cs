using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using CloudAlarmOverlay.Core.Services;

namespace CloudAlarmOverlay.Infrastructure.Google;

internal sealed partial class GoogleWorkspace(IGoogleVault vault,GoogleApi api,AdminSession session,ITaskRepository tasks,
    ICsvSheetParser parser,IDeviceIdentityService identity,IAudienceFilterService audience,IEmployeeRepository employees,
    ChangeSignal changes,TimeProvider clock,IPrivateSourceStore privateSources,IAuditService audit,GoogleBuiltInClient builtIn):IGoogleWorkspace,IDisposable
{
    private readonly SemaphoreSlim gate=new(1,1);
    public event Action? Changed;
    private void Notify(){changes.Notify();Changed?.Invoke();}
    private async Task<GoogleVaultState> ReadStateAsync(CancellationToken ct)
    {
        var state=await vault.ReadAsync(ct);
        // Keep the client associated with existing refresh tokens across application upgrades.
        return state.Client is null && builtIn.Client is {} client
            ? state with{Client=client,UsesBuiltInClient=true}:state;
    }
    public async Task UseBuiltInClientAsync(CancellationToken ct=default)
    {
        session.RequireAdmin();await gate.WaitAsync(ct);
        try
        {
            var state=await vault.ReadAsync(ct);session.RequireAdmin();
            var client=builtIn.Client??throw new InvalidOperationException("此版本尚未內建 Google 登入設定。");
            if(state.Accounts.Count>0)throw new InvalidOperationException("切換登入設定前，請先登出所有 Google 帳號。");
            await vault.WriteAsync(state with{Client=client,UsesBuiltInClient=true},ct);
        }
        finally{gate.Release();}Notify();
    }
    public async Task<GoogleWorkspaceSnapshot> GetAsync(CancellationToken ct=default)
    {
        await gate.WaitAsync(ct);
        try
        {
            var state=await ReadStateAsync(ct);
            return new(state.Client is not null,state.Accounts.Select(a=>new GoogleAccount(a.Id,a.Email,a.Label,a.Status)).ToArray(),state.Sources.ToArray()){UsesBuiltInClient=state.UsesBuiltInClient};
        }
        finally{gate.Release();}
    }
    public async Task ImportClientAsync(string json,CancellationToken ct=default)
    {
        session.RequireAdmin();var client=GoogleApi.ParseClient(json);
        await gate.WaitAsync(ct);
        try
        {
            var state=await ReadStateAsync(ct);session.RequireAdmin();
            if(state.Accounts.Count>0&&state.Client?.Id!=client.Id)throw new InvalidOperationException("更換 OAuth 專案前，請先登出所有 Google 帳號。");
            await vault.WriteAsync(state with{Client=client,UsesBuiltInClient=false},ct);
        }
        finally{gate.Release();}Notify();
    }
    public async Task SignInAsync(string label,bool sheets,bool calendar,Action<string> openBrowser,CancellationToken ct=default,bool writeSheets=false)
    {
        session.RequireAdmin();
        if(!sheets&&!calendar)throw new ArgumentException("請選擇 Sheets 或 Google 日曆。");
        GoogleClient client;
        await gate.WaitAsync(ct);
        try{client=(await ReadStateAsync(ct)).Client??throw new InvalidOperationException("此版本尚未設定 Google 登入，請聯絡軟體提供者提供已設定的版本。");}
        finally{gate.Release();}
        var scope="openid email"+(sheets?" "+(writeSheets?GoogleApi.SheetsWrite:GoogleApi.SheetsRead):"")+(calendar?" "+GoogleApi.CalendarList+" "+GoogleApi.CalendarRead:"");
        var tokens=await api.AuthorizeAsync(client,scope,openBrowser,ct);
        var access=GoogleApi.Text(tokens,"access_token");var refresh=GoogleApi.Text(tokens,"refresh_token");
        if(string.IsNullOrEmpty(access)||string.IsNullOrEmpty(refresh))throw new InvalidOperationException("Google 未提供可續用的憑證，請重新授權。");
        var user=await api.GetAsync("https://openidconnect.googleapis.com/v1/userinfo",access,ct);
        var id=GoogleApi.Text(user,"sub");var email=GoogleApi.Text(user,"email");
        if(id.Length==0||email.Length==0||!user.TryGetProperty("email_verified",out var verified)||verified.ValueKind!=JsonValueKind.True)
            throw new InvalidOperationException("無法確認 Google 帳號身分。");
        await gate.WaitAsync(ct);
        try
        {
            var state=await ReadStateAsync(ct);session.RequireAdmin();
            if(state.Client?.Id!=client.Id)throw new InvalidOperationException("OAuth 設定已變更，請重新登入。");
            state.Accounts.RemoveAll(a=>a.Id==id);
            state.Accounts.Add(new(){Id=id,Email=email,Label=string.IsNullOrWhiteSpace(label)?email:label.Trim()[..Math.Min(label.Trim().Length,40)],
                AccessToken=access,RefreshToken=refresh,Scope=GoogleApi.Text(tokens,"scope"),ExpiresAt=Expiry(tokens),Status="已連線"});
            await vault.WriteAsync(state,ct);
        }
        finally{gate.Release();}Notify();
    }
    private DateTimeOffset Expiry(JsonElement token)=>clock.GetUtcNow().AddSeconds(token.GetProperty("expires_in").GetInt32());
    public async Task SignOutAsync(string accountId,bool revoke,CancellationToken ct=default)
    {
        session.RequireAdmin();await gate.WaitAsync(ct);
        try
        {
            var state=await ReadStateAsync(ct);var account=state.Accounts.Single(a=>a.Id==accountId);
            if(revoke&&!string.IsNullOrEmpty(account.RefreshToken))await api.RevokeAsync(account.RefreshToken,ct);
            session.RequireAdmin();
            foreach(var source in state.Sources.Where(s=>s.AccountId==accountId))await privateSources.ClearAsync(source.CacheSource,ct);
            drafts.Clear();
            state.Sources.RemoveAll(s=>s.AccountId==accountId);state.Accounts.RemoveAll(a=>a.Id==accountId);
            await vault.WriteAsync(state,ct);
        }
        finally{gate.Release();}Notify();
    }
    private async Task<string> TokenAsync(GoogleVaultState state,string id,bool force,CancellationToken ct)
    {
        var account=state.Accounts.SingleOrDefault(a=>a.Id==id)??throw new GoogleAuthorizationException("此帳號已登出，請重新登入。");
        if(account.RefreshToken.Length==0)throw new GoogleAuthorizationException("需要重新登入");
        if(!force&&account.ExpiresAt>clock.GetUtcNow().AddMinutes(2))return account.AccessToken;
        try
        {
            var response=await api.TokenAsync(state.Client!,new(){{"grant_type","refresh_token"},{"refresh_token",account.RefreshToken}},ct);
            var refresh=GoogleApi.Text(response,"refresh_token");
            var next=account with{AccessToken=GoogleApi.Text(response,"access_token"),RefreshToken=refresh.Length>0?refresh:account.RefreshToken,ExpiresAt=Expiry(response),Status="已連線"};
            if(next.AccessToken.Length==0)throw new InvalidDataException("Google 憑證回應無效。");
            state.Accounts[state.Accounts.IndexOf(account)]=next;
            await vault.WriteAsync(state,ct);return next.AccessToken;
        }
        catch(GoogleAuthorizationException)
        {
            state.Accounts[state.Accounts.IndexOf(account)]=account with{AccessToken="",RefreshToken="",Status="需要重新登入"};
            foreach(var source in state.Sources.Where(s=>s.AccountId==id))await privateSources.ClearAsync(source.CacheSource,ct);
            await vault.WriteAsync(state,ct);Notify();throw;
        }
    }
    private async Task<JsonElement> GetAsync(GoogleVaultState state,string accountId,string url,CancellationToken ct)
    {
        var token=await TokenAsync(state,accountId,false,ct);
        try{return await api.GetAsync(url,token,ct);}
        catch(GoogleAuthorizationException){return await api.GetAsync(url,await TokenAsync(state,accountId,true,ct),ct);}
    }
    internal static string SpreadsheetId(string input)
    {
        var value=input.Trim();
        if(Uri.TryCreate(value,UriKind.Absolute,out var uri))
        {
            if(uri.Scheme!="https"||uri.Host!="docs.google.com")throw new ArgumentException("請使用 Google Sheets 網址或 Spreadsheet ID。");
            var match=Regex.Match(uri.AbsolutePath,@"^/spreadsheets/d/([a-zA-Z0-9_-]+)(?:/|$)");
            value=match.Success?match.Groups[1].Value:"";
        }
        if(!Regex.IsMatch(value,@"^[a-zA-Z0-9_-]+$"))throw new ArgumentException("Spreadsheet ID 格式錯誤。");
        return value;
    }
    public async Task<IReadOnlyList<GoogleResource>> ListTabsAsync(string accountId,string spreadsheet,CancellationToken ct=default)
    {
        session.RequireAdmin();await gate.WaitAsync(ct);
        try{return await TabsAsync(await ReadStateAsync(ct),accountId,SpreadsheetId(spreadsheet),ct);}
        finally{gate.Release();}
    }
    private async Task<IReadOnlyList<GoogleResource>> TabsAsync(GoogleVaultState state,string accountId,string spreadsheet,CancellationToken ct)
    {
        var result=await GetAsync(state,accountId,$"https://sheets.googleapis.com/v4/spreadsheets/{GoogleApi.Encode(spreadsheet)}?fields=sheets.properties",ct);
        return result.GetProperty("sheets").EnumerateArray().Select(s=>s.GetProperty("properties"))
            .Where(p=>GoogleApi.Text(p,"sheetType")=="GRID")
            .Select(p=>new GoogleResource(p.GetProperty("sheetId").GetInt32().ToString(CultureInfo.InvariantCulture),GoogleApi.Text(p,"title"))).ToArray();
    }
    public async Task<IReadOnlyList<GoogleResource>> ListCalendarsAsync(string accountId,CancellationToken ct=default)
    {
        session.RequireAdmin();await gate.WaitAsync(ct);
        try
        {
            var state=await ReadStateAsync(ct);var resources=new List<GoogleResource>();var page="";
            do
            {
                var doc=await GetAsync(state,accountId,"https://www.googleapis.com/calendar/v3/users/me/calendarList?maxResults=250"+(page.Length>0?"&pageToken="+GoogleApi.Encode(page):""),ct);
                if(doc.TryGetProperty("items",out var items))foreach(var item in items.EnumerateArray())
                    if(GoogleApi.Text(item,"accessRole")=="owner")resources.Add(new(GoogleApi.Text(item,"id"),GoogleApi.Text(item,"summary"),GoogleApi.Text(item,"timeZone")));
                page=GoogleApi.Text(doc,"nextPageToken");
            }while(page.Length>0);
            return resources;
        }
        finally{gate.Release();}
    }
    public async Task SaveSourceAsync(GoogleSource source,CancellationToken ct=default)
    {
        session.RequireAdmin();
        if(!Guid.TryParseExact(source.Id,"N",out _)||source.Kind is not ("Sheet" or "Calendar")||string.IsNullOrWhiteSpace(source.Name)||source.Name.Length>60||source.IntervalMinutes is <1 or >1440||source.ReminderMinutes is <0 or >10080||source.AllDayHour is <0 or >23||string.IsNullOrWhiteSpace(source.ResourceId))
            throw new ArgumentException("請填寫來源名稱與資源；同步間隔 1–1440 分鐘、提前提醒 0–10080 分鐘、全天提醒 0–23 時。");
        source=source with{Name=source.Name.Trim(),ResourceId=source.Kind=="Sheet"?SpreadsheetId(source.ResourceId):source.ResourceId.Trim()};
        if(source.Kind=="Sheet"&&!int.TryParse(source.TabId,out _))throw new ArgumentException("請先讀取並選擇 Tasks 工作表。");
        await gate.WaitAsync(ct);
        try
        {
            var state=await ReadStateAsync(ct);session.RequireAdmin();
            if(!state.Accounts.Any(a=>a.Id==source.AccountId))throw new ArgumentException("請選擇已登入帳號。");
            if(state.Sources.Any(s=>s.Id!=source.Id&&s.AccountId==source.AccountId&&s.Kind==source.Kind&&s.ResourceId==source.ResourceId&&s.TabId==source.TabId))throw new ArgumentException("此帳號已加入相同來源，請編輯既有來源。");
            var previous=state.Sources.SingleOrDefault(s=>s.Id==source.Id);
            if(previous is not null&&(!source.Enabled||previous.AccountId!=source.AccountId||previous.ResourceId!=source.ResourceId||previous.TabId!=source.TabId||previous.Kind!=source.Kind))
                await privateSources.ClearAsync(previous.CacheSource,ct);
            state.Sources.RemoveAll(s=>s.Id==source.Id);state.Sources.Add(source with{LastAttempt=null,Status=source.Enabled?"等待同步":"已停用"});
            await vault.WriteAsync(state,ct);
        }
        finally{gate.Release();}Notify();
    }
    public async Task RemoveSourceAsync(string id,CancellationToken ct=default)
    {
        session.RequireAdmin();await gate.WaitAsync(ct);
        try
        {
            var state=await ReadStateAsync(ct);var source=state.Sources.Single(s=>s.Id==id);session.RequireAdmin();
            await privateSources.ClearAsync(source.CacheSource,ct);state.Sources.Remove(source);drafts.Clear();await vault.WriteAsync(state,ct);
        }
        finally{gate.Release();}Notify();
    }
    public async Task SyncAsync(string? sourceId=null,bool automatic=false,CancellationToken ct=default)
    {
        if(!automatic)session.RequireAdmin();await gate.WaitAsync(ct);
        try
        {
            var state=await ReadStateAsync(ct);
            var device=await identity.GetLocalAsync(ct);if(device is null)return;
            foreach(var orphan in (await tasks.GetAllAsync(ct)).Select(t=>t.Source).Where(s=>s.StartsWith("Google:",StringComparison.Ordinal)&&state.Sources.All(x=>x.CacheSource!=s)).Distinct())
                await privateSources.ClearAsync(orphan,ct);
            foreach(var source in state.Sources.ToArray())
            {
                if(!source.Enabled||(sourceId is not null&&source.Id!=sourceId)||
                    (automatic&&source.RetryAfter>clock.GetUtcNow())||
                    (automatic&&source.LastAttempt is {} at&&clock.GetUtcNow()-at<TimeSpan.FromMinutes(source.IntervalMinutes)))continue;
                var next=source with{LastAttempt=clock.GetUtcNow()};
                // Persist attempt before transport to bound retry rates across restarts.
                state.Sources[state.Sources.IndexOf(source)]=next;await vault.WriteAsync(state,ct);
                try
                {
                    IReadOnlyList<AlarmTask> incoming;
                    if(source.Kind=="Sheet")
                    {
                        var tabs=await TabsAsync(state,source.AccountId,source.ResourceId,ct);
                        var tab=tabs.SingleOrDefault(t=>t.Id==source.TabId)??throw new GoogleAccessException("找不到設定的工作表，請重新選擇。");
                        var range="'"+tab.Name.Replace("'","''")+"'";
                        var doc=await GetAsync(state,source.AccountId,$"https://sheets.googleapis.com/v4/spreadsheets/{GoogleApi.Encode(source.ResourceId)}/values/{GoogleApi.Encode(range)}?valueRenderOption=FORMATTED_VALUE",ct);
                        var csv=ToCsv(doc);
                        var staff=await employees.GetAllAsync(ct);
                        incoming=parser.ParseTasks(csv,source.CacheSource).Where(t=>audience.IsIncluded(t,device,staff)).Select(t=>t with{Note=SourceLabel(state,source)}).ToArray();
                    }
                    else incoming=await CalendarTasksAsync(state,source,ct);
                    await tasks.ReplaceCloudCacheAsync(source.CacheSource,incoming,ct);
                    next=next with{LastSuccess=clock.GetUtcNow(),Status=$"同步成功 · {incoming.Count} 筆",RetryAfter=null,FailureCount=0};
                }
                catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
                catch(Exception ex)
                {
                    if(ex is GoogleAuthorizationException or GoogleAccessException or GooglePolicyException)
                        await privateSources.ClearAsync(source.CacheSource,ct);
                    var failures=Math.Min(source.FailureCount+1,10);
                    next=next with{Status=SafeError(ex),FailureCount=failures,RetryAfter=clock.GetUtcNow().AddMinutes(Math.Min(60,source.IntervalMinutes*Math.Pow(2,Math.Min(failures,6)))).AddSeconds(Random.Shared.Next(0,31))};
                }
                state.Sources[state.Sources.FindIndex(s=>s.Id==source.Id)]=next;await vault.WriteAsync(state,ct);
            }
        }
        finally{gate.Release();Notify();}
    }
    private static string SourceLabel(GoogleVaultState state,GoogleSource source)=>$"{state.Accounts.Single(a=>a.Id==source.AccountId).Label} · {source.Name}";
    internal static string SafeError(Exception ex)=>ex switch
    {
        GoogleAuthorizationException or GoogleAccessException or GooglePolicyException=>ex.Message,
        OperationCanceledException=>"連線逾時，保留快取與憑證，稍後重試。",
        HttpRequestException=>"Google 連線失敗或流量受限，保留快取，稍後重試。",
        FormatException or JsonException or InvalidDataException=>"資料格式不符，保留上次成功快取。",
        _=>"同步失敗，保留上次成功快取。"
    };
    internal static string ToCsv(JsonElement response)
    {
        if(!response.TryGetProperty("values",out var values)||values.GetArrayLength()<2)throw new FormatException("工作表須有標題與中文說明列。");
        var width=values[0].GetArrayLength();
        return string.Join("\r\n",values.EnumerateArray().Select(row=>string.Join(",",Enumerable.Range(0,width).Select(i=>"\""+(i<row.GetArrayLength()?row[i].ToString():"").Replace("\"","\"\"")+"\""))));
    }
    private async Task<IReadOnlyList<AlarmTask>> CalendarTasksAsync(GoogleVaultState state,GoogleSource source,CancellationToken ct)
    {
        var now=clock.GetUtcNow();var items=new List<AlarmTask>();var page="";
        do
        {
            var url=$"https://www.googleapis.com/calendar/v3/calendars/{GoogleApi.Encode(source.ResourceId)}/events?singleEvents=true&maxResults=2500&showDeleted=true&timeMin={GoogleApi.Encode(now.AddDays(-2).ToString("O"))}&timeMax={GoogleApi.Encode(now.AddDays(180).ToString("O"))}";
            if(page.Length>0)url+="&pageToken="+GoogleApi.Encode(page);
            var doc=await GetAsync(state,source.AccountId,url,ct);
            var zone=GoogleApi.Text(doc,"timeZone");
            if(doc.TryGetProperty("items",out var events))foreach(var item in events.EnumerateArray())
            {
                var task=GoogleCalendarProjection.Project(item,source,SourceLabel(state,source),zone,now);
                if(task is not null)items.Add(task);
            }
            page=GoogleApi.Text(doc,"nextPageToken");
        }while(page.Length>0);
        return items;
    }
    public void Dispose()=>gate.Dispose();
}

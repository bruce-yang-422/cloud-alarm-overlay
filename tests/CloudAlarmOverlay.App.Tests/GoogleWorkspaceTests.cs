using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CloudAlarmOverlay.App.ViewModels;
using CloudAlarmOverlay.App.Views;
using CloudAlarmOverlay.App.Styles;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using CloudAlarmOverlay.Core.Services;
using CloudAlarmOverlay.Data;
using CloudAlarmOverlay.Infrastructure.Google;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CloudAlarmOverlay.App.Tests;

public sealed class GoogleWorkspaceTests
{
    [Fact] public async Task Built_in_login_needs_no_import_and_pins_client_for_refresh()
    {
        using var f=new Fixture(new("builtin.apps.googleusercontent.com","desktop-value"));await f.Initialize(UsageModes.Company);
        await f.Vault.WriteAsync(new(),default);
        var snapshot=await f.Workspace.GetAsync();Assert.True(snapshot.Configured);Assert.True(snapshot.UsesBuiltInClient);
        Task? callback=null;
        await f.Workspace.SignInAsync("個人",true,true,url=>
        {
            var query=ParseQuery(new Uri(url).Query);Assert.Equal("builtin.apps.googleusercontent.com",query["client_id"]);
            f.Handler.ExpectedChallenge=query["code_challenge"];
            callback=Task.Run(async()=>{using var http=new HttpClient();var result=await http.GetAsync(query["redirect_uri"]+"?state="+query["state"]+"&code=authcode");result.EnsureSuccessStatusCode();});
        });
        await callback!;
        var state=await f.Vault.ReadAsync(default);Assert.Equal("builtin.apps.googleusercontent.com",state.Client!.Id);Assert.True(state.UsesBuiltInClient);Assert.Single(state.Accounts);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Workspace.UseBuiltInClientAsync());
    }
    [Fact] public async Task Custom_client_survives_upgrade_and_can_restore_default_after_signout()
    {
        using var f=new Fixture(new("builtin.apps.googleusercontent.com","desktop-value"));await f.Initialize(UsageModes.Company);
        Assert.False((await f.Workspace.GetAsync()).UsesBuiltInClient);
        Assert.Equal("test.apps.googleusercontent.com",(await f.Vault.ReadAsync(default)).Client!.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Workspace.UseBuiltInClientAsync());
        await f.Workspace.SignOutAsync("a",false);await f.Workspace.SignOutAsync("b",false);
        await f.Workspace.UseBuiltInClientAsync();
        Assert.True((await f.Workspace.GetAsync()).UsesBuiltInClient);
        Assert.Equal("builtin.apps.googleusercontent.com",(await f.Vault.ReadAsync(default)).Client!.Id);
    }
    [Fact] public async Task Missing_provider_configuration_does_not_open_browser()
    {
        using var f=new Fixture();await f.Initialize();await f.Vault.WriteAsync(new(),default);
        Assert.False((await f.Workspace.GetAsync()).Configured);
        var opened=false;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Workspace.SignInAsync("個人",true,true,_=>opened=true));
        Assert.False(opened);
    }
    [Fact] public async Task Refresh_is_automatic_persisted_and_accounts_are_isolated()
    {
        using var f=new Fixture();await f.Initialize();
        await f.Workspace.SyncAsync();
        Assert.Equal(1,f.Handler.Refreshes);Assert.Equal(2,(await f.Get<ITaskRepository>().GetAllAsync()).Count);
        Assert.Contains(f.Handler.Requests,r=>r.Path.Contains("company")&&r.Token=="new-a");
        Assert.Contains(f.Handler.Requests,r=>r.Path.Contains("personal")&&r.Token=="token-b");
        await f.Workspace.SyncAsync();Assert.Equal(1,f.Handler.Refreshes);
        var state=await f.Vault.ReadAsync(default);Assert.Equal("new-a",state.Accounts.Single(a=>a.Id=="a").AccessToken);
        await f.Workspace.SignOutAsync("a",false);
        Assert.Single((await f.Workspace.GetAsync()).Accounts);
        Assert.All(await f.Get<ITaskRepository>().GetAllAsync(),t=>Assert.Equal(f.Personal.CacheSource,t.Source));
    }
    [Fact] public async Task Temporary_failure_keeps_cache_and_tokens_but_revocation_clears_only_one_account()
    {
        using var f=new Fixture();await f.Initialize();await f.Workspace.SyncAsync();
        f.Handler.FailCompany=true;await f.Workspace.SyncAsync();
        Assert.Equal(2,(await f.Get<ITaskRepository>().GetAllAsync()).Count);
        Assert.NotEmpty((await f.Vault.ReadAsync(default)).Accounts.Single(a=>a.Id=="a").RefreshToken);
        f.Handler.FailCompany=false;f.Handler.InvalidRefresh=true;
        var state=await f.Vault.ReadAsync(default);state.Accounts[0]=state.Accounts[0] with{ExpiresAt=DateTimeOffset.MinValue};await f.Vault.WriteAsync(state,default);
        await f.Workspace.SyncAsync();
        Assert.Equal("需要重新登入",(await f.Workspace.GetAsync()).Accounts.Single(a=>a.Id=="a").Status);
        Assert.Single(await f.Get<ITaskRepository>().GetAllAsync());
        Assert.Equal(f.Personal.CacheSource,(await f.Get<ITaskRepository>().GetAllAsync())[0].Source);
    }
    [Fact] public async Task Automatic_polling_obeys_interval_and_disabled_source_removes_cached_tasks()
    {
        using var f=new Fixture();await f.Initialize();await f.Workspace.SyncAsync(automatic:true);
        var count=f.Handler.Requests.Count;await f.Workspace.SyncAsync(automatic:true);Assert.Equal(count,f.Handler.Requests.Count);
        await f.Workspace.SaveSourceAsync(f.Company with{Enabled=false});
        Assert.Single(await f.Get<ITaskRepository>().GetAllAsync());
        await Assert.ThrowsAsync<ArgumentException>(()=>f.Workspace.SaveSourceAsync(f.Personal with{Id=Guid.NewGuid().ToString("N")}));
    }
    [Fact] public async Task Calendar_paging_cancellation_rescheduling_and_private_sources_work()
    {
        using var f=new Fixture();await f.Initialize();
        var calendars=await f.Workspace.ListCalendarsAsync("b");Assert.Single(calendars);Assert.Equal("own",calendars[0].Id);
        var source=new GoogleSource{Kind="Calendar",AccountId="b",ResourceId="own",Name="個人日曆"};await f.Workspace.SaveSourceAsync(source);
        await f.Workspace.SyncAsync(source.Id);
        var task=(await f.Get<ITaskRepository>().GetAllAsync()).Single();Assert.Equal("會議",task.Title);
        Assert.Equal(f.Handler.EventAt.AddMinutes(-10).LocalDateTime,task.ScheduledAt);
        f.Handler.EventAt=f.Handler.EventAt.AddHours(2);await f.Workspace.SyncAsync(source.Id);
        var moved=(await f.Get<ITaskRepository>().GetAllAsync()).Single();Assert.Equal(task.Id,moved.Id);Assert.NotEqual(task.ScheduledAt,moved.ScheduledAt);
        f.Handler.CancelEvent=true;await f.Workspace.SyncAsync(source.Id);Assert.Empty(await f.Get<ITaskRepository>().GetAllAsync());
    }
    [Fact] public void All_day_dates_use_calendar_timezone_and_declined_events_do_not_schedule()
    {
        var source=new GoogleSource{Kind="Calendar",IncludeAllDay=true,AllDayHour=9};
        var ev=JsonSerializer.SerializeToElement(new{id="day",summary="全天",start=new{date="2026-11-01"}});
        var task=GoogleCalendarProjection.Project(ev,source,"test","Asia/Taipei",DateTimeOffset.UtcNow)!;
        Assert.Equal(new DateTimeOffset(2026,11,1,9,0,0,TimeSpan.FromHours(8)).LocalDateTime,task.ScheduledAt);
        Assert.Null(GoogleCalendarProjection.Project(ev,source with{IncludeAllDay=false},"test","Asia/Taipei",DateTimeOffset.UtcNow));
        var declined=JsonSerializer.SerializeToElement(new{id="day",attendees=new[]{new{self=true,responseStatus="declined"}}});
        Assert.Null(GoogleCalendarProjection.Project(declined,source,"test","Asia/Taipei",DateTimeOffset.UtcNow));
    }
    [Fact] public async Task Editing_checks_permissions_conflicts_and_applies_only_changed_cells()
    {
        using var f=new Fixture();await f.Initialize();
        var draft=await f.Workspace.ReadTaskAsync(f.Company.Id,"task1");var fields=draft.Fields.ToDictionary();fields["Title"]="更新標題";
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Workspace.WriteTaskAsync(draft.Token,fields,false));Assert.Equal(0,f.Handler.Writes);
        await f.Workspace.SaveSourceAsync(f.Company with{AllowWrite=true});
        draft=await f.Workspace.ReadTaskAsync(f.Company.Id,"task1");
        f.Handler.Rows[2][2]="他人修改";
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Workspace.WriteTaskAsync(draft.Token,fields,false));Assert.Equal(0,f.Handler.Writes);
        draft=await f.Workspace.ReadTaskAsync(f.Company.Id,"task1");fields=draft.Fields.ToDictionary();fields["Title"]="更新標題";
        await f.Workspace.WriteTaskAsync(draft.Token,fields,false);
        Assert.Equal(1,f.Handler.Writes);Assert.Equal("更新標題",f.Handler.Rows[2][2]);Assert.Equal("保留未知欄",f.Handler.Rows[2][4]);
        Assert.Single(f.Handler.LastWrite.GetProperty("requests").EnumerateArray());
        Assert.Contains(await f.Get<IAuditLogRepository>().GetRangeAsync(DateTime.Today.AddDays(-1),DateTime.Today.AddDays(1)),a=>a.UserId=="Google:a");
    }
    [Fact] public async Task Create_delete_and_unknown_write_outcome_require_fresh_read()
    {
        using var f=new Fixture();await f.Initialize();await f.Workspace.SaveSourceAsync(f.Company with{AllowWrite=true});
        var draft=await f.Workspace.ReadTaskAsync(f.Company.Id,"");var fields=draft.Fields.ToDictionary();fields["Title"]="新任務";
        await f.Workspace.WriteTaskAsync(draft.Token,fields,false);Assert.Equal(4,f.Handler.Rows.Count);
        var created=await f.Workspace.ReadTaskAsync(f.Company.Id,draft.ExternalId);Assert.True(created.Exists);
        await f.Workspace.WriteTaskAsync(created.Token,created.Fields,true);Assert.Equal(3,f.Handler.Rows.Count);
        var old=await f.Workspace.ReadTaskAsync(f.Company.Id,"task1");fields=old.Fields.ToDictionary();fields["Title"]="已寫入但逾時";
        f.Handler.TimeoutAfterWrite=true;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Workspace.WriteTaskAsync(old.Token,fields,false));
        var writes=f.Handler.Writes;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Workspace.WriteTaskAsync(old.Token,fields,false));Assert.Equal(writes,f.Handler.Writes);
        Assert.Equal("已寫入但逾時",(await f.Workspace.ReadTaskAsync(f.Company.Id,"task1")).Fields["Title"]);
    }
    [Fact] public async Task Logout_scrubs_private_history_and_keeps_other_sources()
    {
        using var f=new Fixture();await f.Initialize();await f.Workspace.SyncAsync();
        var task=(await f.Get<ITaskRepository>().GetAllAsync()).Single(t=>t.Source==f.Company.CacheSource);
        var runtime=f.Get<IRuntimeStore>();var id=OccurrenceIdentity.For(task.Id,task.ScheduledAt);await runtime.ClaimAsync(id,task,task.ScheduledAt);
        await runtime.DisplayedAsync(id,task,task.ScheduledAt,(await f.Get<IDeviceIdentityService>().GetLocalAsync())!);
        await f.Workspace.SignOutAsync("a",false);
        var logs=await f.Get<IAckLogRepository>().GetRangeAsync(DateTime.Today.AddDays(-1),DateTime.Today.AddDays(1));Assert.Single(logs);Assert.Null(logs[0].TaskSnapshotJson);Assert.Equal("已清除的私人行程",logs[0].TaskName);
    }
    [Fact] public async Task Vault_uses_windows_encryption_and_rejects_service_account_configuration()
    {
        using var f=new Fixture();var vault=new GoogleVault(f);var state=await f.Vault.ReadAsync(default);await vault.WriteAsync(state,default);
        var bytes=await File.ReadAllBytesAsync(Path.Combine(f.DataDirectory,"google-workspace.dat"));
        Assert.DoesNotContain("refresh-a",Encoding.UTF8.GetString(bytes));Assert.Equal("a",(await vault.ReadAsync(default)).Accounts[0].Id);
        await Assert.ThrowsAsync<ArgumentException>(()=>f.Workspace.ImportClientAsync("{}"));
        await f.Initialize();await Assert.ThrowsAsync<ArgumentException>(()=>f.Workspace.ImportClientAsync("{\"type\":\"service_account\"}"));
        Assert.Equal("abc",GoogleWorkspace.SpreadsheetId("https://docs.google.com/spreadsheets/d/abc/edit#gid=0"));
        Assert.Throws<ArgumentException>(()=>GoogleWorkspace.SpreadsheetId("https://example.com/abc"));
    }
    [Fact] public async Task OAuth_uses_pkce_state_and_loopback_before_accepting_account()
    {
        using var f=new Fixture();await f.Initialize();Task? callback=null;
        await f.Workspace.SignInAsync("新增帳號",true,true,url=>
        {
            var query=ParseQuery(new Uri(url).Query);Assert.Equal("S256",query["code_challenge_method"]);Assert.Equal("consent select_account",query["prompt"]);
            f.Handler.ExpectedChallenge=query["code_challenge"];
            callback=Task.Run(async()=>
            {
                using var client=new HttpClient();
                var invalid=await client.GetAsync(query["redirect_uri"]+"?state=wrong&code=bad");Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);
                var valid=await client.GetAsync(query["redirect_uri"]+"?state="+query["state"]+"&code=authcode");Assert.Equal(HttpStatusCode.OK,valid.StatusCode);
            });
        });
        await callback!;Assert.Equal(3,(await f.Workspace.GetAsync()).Accounts.Count);
    }
    [Fact] public async Task Company_user_manages_personal_google_connections_without_admin()
    {
        using var f=new Fixture();await f.Initialize(UsageModes.Company);
        var admin=f.Get<AdminSession>();Assert.False(admin.IsAuthenticated);
        await f.Workspace.ImportClientAsync("{\"installed\":{\"client_id\":\"test.apps.googleusercontent.com\",\"client_secret\":\"test-secret\"}}");
        Assert.Single(await f.Workspace.ListTabsAsync("a","company"));
        Assert.Single(await f.Workspace.ListCalendarsAsync("b"));
        await f.Workspace.SaveSourceAsync(f.Company with{AllowWrite=true});
        await f.Workspace.SyncAsync(f.Company.Id);
        var draft=await f.Workspace.ReadTaskAsync(f.Company.Id,"task1");
        var fields=draft.Fields.ToDictionary();fields["Title"]="使用者自行編輯";
        await f.Workspace.WriteTaskAsync(draft.Token,fields,false);
        Assert.Equal("使用者自行編輯",f.Handler.Rows[2][2]);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Get<IAuditLogRepository>().GetRangeAsync(DateTime.Today.AddDays(-1),DateTime.Today.AddDays(1)));
        var readOnly=await f.Workspace.ReadTaskAsync(f.Personal.Id,"task1");
        await f.Workspace.SaveSourceAsync(f.Personal with{AllowWrite=true});
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Workspace.WriteTaskAsync(readOnly.Token,readOnly.Fields,false));
        Assert.Equal(1,f.Handler.Writes);
        await f.Workspace.RemoveSourceAsync(f.Company.Id);
        await f.Workspace.SignOutAsync("a",false);await f.Workspace.SignOutAsync("b",true);
        Assert.Empty((await f.Workspace.GetAsync()).Accounts);
        Assert.False(admin.IsAuthenticated);Assert.Throws<UnauthorizedAccessException>(()=>admin.RequireAdmin());
    }
    [Fact] public async Task Google_settings_ui_is_available_without_admin_and_keeps_source_drafts()
    {
        await MilestoneOneTests.RunSta(async()=>
        {
            using var f=new Fixture();await f.Initialize(UsageModes.Company);
            var main=f.Get<MainViewModel>();var window=f.Get<MainWindow>();
            try
            {
                await main.InitializeAsync();window.ShowActivated=false;window.ShowInTaskbar=false;window.Width=1200;window.Height=920;window.Show();
                Assert.False(main.Admin.IsAuthenticated);main.PageIndex=5;Assert.Equal(0,main.PageIndex);
                main.PageIndex=4;main.Preferences.SelectedSettingsTab=PreferencesViewModel.GoogleSettingsTabIndex;window.UpdateLayout();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var vm=main.Preferences.Google!;await vm.LoadAsync();Assert.Equal(2,vm.Accounts.Count);Assert.Equal(3,vm.Tabs.Count);
                var view=Find<GoogleWorkspaceView>(window)!;Assert.True(view.IsVisible);
                Assert.True(view.IsEnabled);
                main.Preferences.SelectedSettingsTab=0;await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);Assert.False(view.IsVisible);
                main.Preferences.SelectedSettingsTab=PreferencesViewModel.GoogleSettingsTabIndex;await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);Assert.True(view.IsVisible);
                vm.Selected=vm.Tabs[1];vm.Selected.Name="未儲存草稿";vm.Selected=vm.Tabs[2];vm.Selected=vm.Tabs[1];Assert.Equal("未儲存草稿",vm.Selected.Name);Assert.True(vm.Selected.Locked);
                vm.Selected=vm.Tabs[0];window.UpdateLayout();await Task.Delay(180);Capture(window,"google-overview");
                vm.Selected=vm.Tabs[1];window.UpdateLayout();await Task.Delay(180);Capture(window,"google-source");
                vm.Selected.Locked=false;
                var scroll=(ScrollViewer)view.FindName("SettingsScroll");
                var editor=(Expander)view.FindName("TaskEditorExpander");
                var save=(Button)view.FindName("SaveSourceButton");
                var sync=(Button)view.FindName("SyncSourceButton");
                window.UpdateLayout();Assert.True(save.IsEnabled);Assert.True(sync.IsEnabled);
                foreach(var size in new[]{new Size(1050,680),new Size(1050,780),new Size(1440,1000)})
                {
                    window.Width=size.Width;window.Height=size.Height;window.UpdateLayout();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    Assert.True(scroll.ActualHeight>=180,$"Settings viewport too small: {scroll.ActualHeight}");
                    var viewport=new Rect(0,0,view.ActualWidth,view.ActualHeight);
                    foreach(var button in new[]{save,sync})
                    {
                        Assert.True(button.IsVisible);
                        Assert.True(viewport.Contains(button.TransformToAncestor(view).TransformBounds(new Rect(button.RenderSize))));
                    }
                    editor.IsExpanded=true;window.UpdateLayout();scroll.ScrollToEnd();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    var before=save.TransformToAncestor(view).Transform(new Point());
                    scroll.ScrollToTop();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    Assert.Equal(before,save.TransformToAncestor(view).Transform(new Point()));
                    editor.IsExpanded=false;window.UpdateLayout();
                    Capture(window,$"google-management-{size.Width}x{size.Height}");
                }
                AdaptiveBrushExtension.Apply(true);window.UpdateLayout();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);Capture(window,"google-management-dark");
                scroll.ScrollToEnd();vm.Selected=vm.Tabs[0];await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);Assert.Equal(0,scroll.VerticalOffset);
                vm.Accounts.Clear();window.UpdateLayout();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.True(((Expander)view.FindName("AddAccountExpander")).IsExpanded);
            }
            finally{AdaptiveBrushExtension.Apply(false);window.ForceClose();}
        });
    }
    private static T? Find<T>(DependencyObject parent) where T:DependencyObject
    {if(parent is T found)return found;for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)if(Find<T>(VisualTreeHelper.GetChild(parent,i)) is {} child)return child;return null;}
    private static void Capture(Window window,string name)
    {
        var directory=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../artifacts/screenshots"));Directory.CreateDirectory(directory);
        var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(directory,name+".png"));png.Save(file);
    }
    private static Dictionary<string,string> ParseQuery(string input)=>input.TrimStart('?').Split('&').Select(x=>x.Split('=',2)).ToDictionary(x=>Uri.UnescapeDataString(x[0]),x=>Uri.UnescapeDataString(x[1].Replace('+',' ')));
    private sealed class MemoryVault:IGoogleVault
    {
        private string json="{}";
        public Task<GoogleVaultState> ReadAsync(CancellationToken ct)=>Task.FromResult(JsonSerializer.Deserialize<GoogleVaultState>(json)!);
        public Task WriteAsync(GoogleVaultState state,CancellationToken ct){json=JsonSerializer.Serialize(state);return Task.CompletedTask;}
    }
    private sealed class Handler:HttpMessageHandler
    {
        public int Refreshes,Writes;public bool FailCompany,InvalidRefresh,CancelEvent,TimeoutAfterWrite;
        public string? ExpectedChallenge;
        public DateTimeOffset EventAt=DateTimeOffset.UtcNow.AddDays(2);
        public JsonElement LastWrite;
        public List<(string Path,string Token)> Requests=[];
        public List<List<string>> Rows=[["Id","Time","Title","Enabled","Custom"],["編號","時間","標題","啟用","其他"],["task1",DateTime.Now.AddDays(1).ToString("yyyy-MM-dd HH:mm"),"任務","TRUE","保留未知欄"]];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            var path=request.RequestUri!.AbsolutePath;Requests.Add((path,request.Headers.Authorization?.Parameter??""));
            object result;
            if(path=="/token")
            {
                var fields=ParseQuery(await request.Content!.ReadAsStringAsync(ct));
                if(fields["grant_type"]=="authorization_code")
                {
                    var challenge=Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(fields["code_verifier"]))).TrimEnd('=').Replace('+','-').Replace('/','_');Assert.Equal(ExpectedChallenge,challenge);
                    result=new{access_token="token-c",refresh_token="refresh-c",expires_in=3600,scope=GoogleApi.SheetsRead};
                }
                else
                {
                    Refreshes++;if(InvalidRefresh)return Reply(new{error="invalid_grant"},HttpStatusCode.BadRequest);
                    result=new{access_token="new-a",expires_in=3600};
                }
            }
            else if(path=="/v1/userinfo")result=new{sub="c",email="c@example.test",email_verified=true};
            else if(path=="/revoke")result=new{};
            else if(path.EndsWith(":batchUpdate"))
            {
                Writes++;using var doc=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));LastWrite=doc.RootElement.Clone();
                foreach(var change in LastWrite.GetProperty("requests").EnumerateArray())
                {
                    if(change.TryGetProperty("updateCells",out var update))
                    {
                        var at=update.GetProperty("start");Rows[at.GetProperty("rowIndex").GetInt32()][at.GetProperty("columnIndex").GetInt32()]=update.GetProperty("rows")[0].GetProperty("values")[0].GetProperty("userEnteredValue").GetProperty("stringValue").GetString()!;
                    }
                    else if(change.TryGetProperty("appendCells",out var append))Rows.Add(append.GetProperty("rows")[0].GetProperty("values").EnumerateArray().Select(v=>v.GetProperty("userEnteredValue").GetProperty("stringValue").GetString()!).ToList());
                    else if(change.TryGetProperty("deleteDimension",out var delete))Rows.RemoveAt(delete.GetProperty("range").GetProperty("startIndex").GetInt32());
                }
                if(TimeoutAfterWrite)throw new TaskCanceledException();result=new{};
            }
            else if(FailCompany&&path.Contains("company"))return Reply(new{},HttpStatusCode.ServiceUnavailable);
            else if(path.Contains("/values/"))result=new{values=Rows};
            else if(path.Contains("/spreadsheets/"))result=new{sheets=new[]{new{properties=new{sheetId=0,title="Tasks",sheetType="GRID"}}}};
            else if(path.EndsWith("/calendarList"))result=new{items=new[]{new{id="own",summary="個人",accessRole="owner",timeZone="Asia/Taipei"},new{id="shared",summary="他人",accessRole="reader",timeZone="Asia/Taipei"}}};
            else if(path.EndsWith("/events"))
            {
                if(request.RequestUri.Query.Contains("pageToken"))result=new{items=new[]{new{id="cancelled",status="cancelled"}},timeZone="Asia/Taipei"};
                else result=new{items=new[]{new{id="ev1",summary="會議",status=CancelEvent?"cancelled":"confirmed",start=new{dateTime=EventAt.ToString("O")}}},timeZone="Asia/Taipei",nextPageToken="second"};
            }
            else throw new InvalidOperationException("Unexpected test request: "+path);
            return Reply(result);
        }
        private static HttpResponseMessage Reply(object value,HttpStatusCode status=HttpStatusCode.OK)=>new(status){Content=new StringContent(JsonSerializer.Serialize(value),Encoding.UTF8,"application/json")};
    }
    private sealed class Fixture:IDisposable,IAppPaths
    {
        public string DataDirectory{get;}=Path.Combine(Path.GetTempPath(),"CloudAlarmGoogleTests",Guid.NewGuid().ToString("N"));
        public string DatabasePath=>Path.Combine(DataDirectory,"test.db");
        public MemoryVault Vault{get;}=new();public Handler Handler{get;}=new();
        public GoogleSource Company{get;}=new(){Name="公司任務",AccountId="a",ResourceId="company",TabId="0"};
        public GoogleSource Personal{get;}=new(){Name="個人任務",AccountId="b",ResourceId="personal",TabId="0"};
        private readonly IHost host;
        public Fixture(GoogleClient? builtIn=null)
        {
            host=new HostBuilder().ConfigureServices(s=>{CompositionRoot.ConfigureServices(s);s.AddSingleton<IAppPaths>(this);s.AddSingleton<IGoogleVault>(Vault);s.AddSingleton(new GoogleBuiltInClient(()=>builtIn));s.AddSingleton(new GoogleApi(new HttpClient(Handler)));}).Build();
            Vault.WriteAsync(new(){Client=new("test.apps.googleusercontent.com","test-secret"),Sources=[Company,Personal],Accounts=[
                new(){Id="a",Email="a@example.test",Label="公司",AccessToken="old-a",RefreshToken="refresh-a",ExpiresAt=DateTimeOffset.MinValue,Scope=GoogleApi.SheetsWrite},
                new(){Id="b",Email="b@example.test",Label="個人",AccessToken="token-b",RefreshToken="refresh-b",ExpiresAt=DateTimeOffset.UtcNow.AddHours(1),Scope=GoogleApi.SheetsRead+" "+GoogleApi.CalendarList+" "+GoogleApi.CalendarRead}]},default).GetAwaiter().GetResult();
        }
        public IGoogleWorkspace Workspace=>Get<IGoogleWorkspace>();
        public T Get<T>() where T:notnull=>host.Services.GetRequiredService<T>();
        public async Task Initialize(string usageMode=UsageModes.Personal){await Get<IDatabaseInitializer>().InitializeAsync();await Get<IDeviceIdentityService>().SetInitialIdentityAsync("GOOGLE-TEST","測試",usageMode);}
        public void Dispose(){host.Dispose();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(DataDirectory))Directory.Delete(DataDirectory,true);}
    }
}

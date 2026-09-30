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
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OAuth_configuration_requires_admin_and_rechecks_expiry_before_writing(bool restore)
    {
        var clock=new ConfigurationClock();
        using var f=new Fixture(new("builtin.apps.googleusercontent.com","desktop-value"),clock);await f.Initialize(UsageModes.Company);
        var initial=await f.Vault.ReadAsync(default);await f.Vault.WriteAsync(initial with{Accounts=[],Sources=[]},default);
        var before=JsonSerializer.Serialize(await f.Vault.ReadAsync(default));
        Task Change()=>restore?f.Workspace.UseBuiltInClientAsync():f.Workspace.ImportClientAsync("{\"installed\":{\"client_id\":\"organization.apps.googleusercontent.com\",\"client_secret\":\"desktop-test\"}}");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(Change);
        var auth=f.Get<IAuthenticationService>();await auth.EnsureDefaultAdministratorAsync();Assert.True(await auth.AuthenticateAsync("admin","12345"));
        f.Vault.BeforeRead=()=>clock.Advance(TimeSpan.FromMinutes(11));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(Change);
        f.Vault.BeforeRead=null;
        Assert.Equal(before,JsonSerializer.Serialize(await f.Vault.ReadAsync(default)));
        Assert.True(await auth.AuthenticateAsync("admin","12345"));await Change();
        var saved=await f.Vault.ReadAsync(default);Assert.Equal(restore,saved.UsesBuiltInClient);
        Assert.Equal(restore?"builtin.apps.googleusercontent.com":"organization.apps.googleusercontent.com",saved.Client!.Id);
        await Assert.ThrowsAsync<ArgumentException>(()=>f.Workspace.ImportClientAsync("{\"type\":\"service_account\"}"));
        Assert.Empty(f.Handler.Requests);
    }
    [Fact] public async Task OAuth_configuration_is_only_present_in_administrator_area()
    {
        await MilestoneOneTests.RunSta(async()=>
        {
            using var f=new Fixture();await f.Initialize(UsageModes.Company);
            var vm=f.Get<MainViewModel>();await vm.InitializeAsync();var main=f.Get<MainWindow>();main.Width=1200;main.Height=850;main.ShowInTaskbar=false;main.Show();
            try
            {
                vm.PageIndex=4;vm.Preferences.SelectedPage=SettingsPage.Google;main.UpdateLayout();
                var personal=Find<GoogleWorkspaceView>(main)!;
                Assert.DoesNotContain(FindAll<Expander>(personal),e=>Equals(e.Header,"進階登入設定"));
                Assert.DoesNotContain(FindAll<Button>(personal),b=>Equals(b.Content,"匯入設定…"));
                var auth=f.Get<IAuthenticationService>();await auth.EnsureDefaultAdministratorAsync();Assert.True(await auth.AuthenticateAsync("admin","12345"));
                await vm.Admin.SessionChangedAsync();await vm.Preferences.Google!.LoadAsync();vm.PageIndex=5;vm.Admin.SelectedTab=4;main.UpdateLayout();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var admin=Find<AdminView>(main)!;var import=(Button)admin.FindName("ImportGoogleClientButton");var restore=(Button)admin.FindName("RestoreGoogleClientButton");
                var advanced=(Expander)admin.FindName("GoogleLoginAdvanced");
                Assert.False(advanced.IsExpanded);Assert.False(import.IsVisible);
                advanced.IsExpanded=true;main.UpdateLayout();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.True(import.IsVisible&&import.IsEnabled);Assert.True(restore.IsVisible&&restore.IsEnabled);
                Capture(main,"google-admin-login-settings");AdaptiveBrushExtension.Apply(true);main.UpdateLayout();Capture(main,"google-admin-login-settings-dark");
                vm.Admin.Session.SignOut();await vm.Admin.SessionChangedAsync();main.UpdateLayout();Assert.False(import.IsEnabled);Assert.False(restore.IsEnabled);
            }
            finally{AdaptiveBrushExtension.Apply(false);main.ForceClose();}
        });
    }
    [Fact] public void Connection_health_distinguishes_pending_disabled_errors_and_stale_sources()
    {
        var now=DateTimeOffset.Now;var account=new GoogleAccount("a","test@example.test","工作排程","已連線");
        var source=new GoogleSource{AccountId="a",LastSuccess=now,LastAttempt=now};
        Assert.Equal("Healthy",ConnectionPresentation.ForSource(source,account,now).Severity);
        Assert.Equal("Pending",ConnectionPresentation.ForSource(source with{LastSuccess=null},account,now).Severity);
        Assert.Equal("Stopped",ConnectionPresentation.ForSource(source with{LastSuccess=now.AddHours(-1)},account,now).Severity);
        Assert.Equal("Error",ConnectionPresentation.ForSource(source with{FailureCount=1,Status="連線失敗"},account,now).Severity);
        Assert.Equal("Error",ConnectionPresentation.ForSource(source,account with{Status="需要重新登入"},now).Severity);
        var paused=ConnectionPresentation.ForSource(source with{Enabled=false,FailureCount=1},account,now);
        Assert.Equal("Inactive",paused.Severity);
        Assert.Equal("Healthy",ConnectionPresentation.Aggregate([paused,new("正常","排程正常","Healthy")]).Severity);
    }
    [Fact] public async Task Connection_overview_lists_accounts_and_sources_without_growing_sidebar_and_settings_order_is_correct()
    {
        await MilestoneOneTests.RunSta(async()=>
        {
            using var f=new Fixture();await f.Initialize(UsageModes.Company);
            var state=await f.Vault.ReadAsync(default);var now=DateTimeOffset.Now;
            state.Accounts[0]=state.Accounts[0] with{Label="工作排程"};state.Accounts[1]=state.Accounts[1] with{Label="家庭生活"};
            state=state with{Sources=Enumerable.Range(0,12).Select(i=>new GoogleSource{AccountId=i<6?"a":"b",Name=$"來源 {i+1}",Kind=i%2==0?"Calendar":"Sheet",LastAttempt=now,LastSuccess=now,Status=i==8?"暫時無法連線，保留上次同步資料。":"同步成功",Enabled=i!=11,FailureCount=i==8?1:0}).ToList()};
            await f.Vault.WriteAsync(state,default);f.Get<IAlarmHeartbeat>().Tick();
            var vm=f.Get<MainViewModel>();await vm.InitializeAsync();await vm.RefreshHealthAsync();
            Assert.Equal("Error",vm.OverallHealth.Severity);Assert.Contains("2 個 Google 帳號",vm.ConnectionSummary);Assert.Contains("12 個來源",vm.ConnectionSummary);
            Assert.False(vm.HasSheetASource);Assert.False(vm.HasSheetBSource);Assert.Equal(2,vm.ConnectionGroups.Count);
            Assert.Equal("工作排程",vm.ConnectionGroups[0].Name);Assert.Equal(6,vm.ConnectionGroups[1].Sources.Count);
            var main=f.Get<MainWindow>();main.Width=1050;main.Height=680;main.ShowInTaskbar=false;main.ShowActivated=false;main.Show();
            var details=new ConnectionStatusWindow(vm){Owner=main,ShowInTaskbar=false,ShowActivated=false};
            try
            {
                foreach(var dark in new[]{false,true})
                {
                    AdaptiveBrushExtension.Apply(dark);main.UpdateLayout();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    Assert.InRange(((Border)main.FindName("ConnectionStatusCard")).ActualHeight,100,220);
                    Capture(main,dark?"connections-sidebar-dark":"connections-sidebar-light");
                    if(!details.IsVisible)details.Show();details.UpdateLayout();
                    var primaryTexts=FindAll<TextBlock>(details).Where(t=>t.Text is "工作排程" or "來源 1" or "正常" or "同步失敗").ToArray();
                    Assert.True(primaryTexts.Length>=4);
                    foreach(var label in primaryTexts)
                    {
                        var color=Assert.IsType<SolidColorBrush>(label.Foreground).Color;
                        Assert.True(dark?color.R>200:color.R<80,$"Unreadable primary text: {label.Text}");
                    }
                    var scroll=(ScrollViewer)details.FindName("ConnectionsScroll");Assert.True(scroll.ScrollableHeight>0);
                    scroll.ScrollToTop();details.UpdateLayout();Capture(details,dark?"connections-detail-dark":"connections-detail-light");
                    scroll.ScrollToEnd();details.UpdateLayout();Capture(details,dark?"connections-detail-bottom-dark":"connections-detail-bottom-light");
                }
                details.Close();AdaptiveBrushExtension.Apply(false);vm.PageIndex=4;main.UpdateLayout();
                var settings=FindAll<PreferencesView>(main).Single();var tabs=(TabControl)settings.FindName("SettingsTabs");
                Assert.Equal("我的裝置",((TabItem)tabs.Items[tabs.Items.Count-1]).Header);
                Assert.Equal("Google 帳號與同步",tabs.Items.OfType<TabItem>().Single(t=>Equals(t.Tag,SettingsPage.Google)).Header);
                vm.Preferences.SelectedPage=SettingsPage.Home;main.UpdateLayout();Capture(main,"settings-dropdown-chevron");
                vm.Preferences.SelectedPage=SettingsPage.Device;main.UpdateLayout();Capture(main,"settings-device-last");
                Assert.Empty(f.Handler.Requests);
            }
            finally{AdaptiveBrushExtension.Apply(false);details.Close();main.ForceClose();}
        });
    }
    [Fact] public async Task Account_rename_persists_only_alias_without_authorization_or_source_changes()
    {
        using var f=new Fixture();await f.Initialize(UsageModes.Company);
        var before=await f.Vault.ReadAsync(default);var notices=0;f.Workspace.Changed+=()=>notices++;
        await f.Workspace.RenameAccountAsync("b","  家庭生活  ");
        var after=await f.Vault.ReadAsync(default);
        Assert.Equal(before.Accounts[0],after.Accounts[0]);Assert.Equal(before.Accounts[1] with{Label="家庭生活"},after.Accounts[1]);
        Assert.Equal(before.Sources,after.Sources);Assert.Equal(before.Client,after.Client);
        Assert.Equal("家庭生活",(await f.Workspace.GetAsync()).Accounts.Single(a=>a.Id=="b").Label);
        foreach(var invalid in new[]{"","   ",new string('字',41),"工作\n私人"})
            await Assert.ThrowsAsync<ArgumentException>(()=>f.Workspace.RenameAccountAsync("b",invalid));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Workspace.RenameAccountAsync("missing","名稱"));
        Assert.Equal("家庭生活",(await f.Vault.ReadAsync(default)).Accounts[1].Label);
        Assert.Equal(1,notices);Assert.Empty(f.Handler.Requests);Assert.False(f.Get<AdminSession>().IsAuthenticated);
    }
    [Fact] public async Task Account_rename_menu_targets_its_row_and_preserves_source_drafts()
    {
        await MilestoneOneTests.RunSta(async()=>
        {
            using var f=new Fixture();await f.Initialize(UsageModes.Company);
            var vm=f.Get<GoogleWorkspaceViewModel>();await vm.LoadAsync();
            var view=new GoogleWorkspaceView{DataContext=vm};var host=new Window{Content=view,Width=1050,Height=700,ShowInTaskbar=false};host.Show();
            try
            {
                var draft=vm.Tabs.Single(t=>t.AccountId=="b");draft.Name="尚未儲存的來源名稱";
                vm.Selected=draft;host.UpdateLayout();
                await vm.RenameAccountAsync("b","行程專用");
                Assert.Equal("b",draft.AccountId);Assert.Equal("尚未儲存的來源名稱",draft.Name);
                vm.Selected=vm.Tabs[0];vm.SelectedAccount=vm.Accounts.Single(a=>a.Id=="a");host.UpdateLayout();
                MenuItem RenameItem()
                {
                    var list=(ItemsControl)view.FindName("AccountList");
                    var button=FindAll<Button>(list).Single(b=>b.ContextMenu is not null&&b.DataContext is GoogleAccount{Id:"b"});
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var menu=button.ContextMenu!;menu.IsOpen=false;
                    return menu.Items.OfType<MenuItem>().Single(m=>Equals(m.Header,"重新命名…"));
                }
                _=host.Dispatcher.BeginInvoke(()=>
                {
                    var dialog=host.OwnedWindows.OfType<GoogleAccountNameDialog>().Single();
                    Assert.Equal("b@example.test",((TextBlock)dialog.FindName("AccountEmail")).Text);
                    ((TextBox)dialog.FindName("NameInput")).Text="取消不儲存";dialog.Close();
                });
                RenameItem().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Assert.Equal("行程專用",(await f.Workspace.GetAsync()).Accounts.Single(a=>a.Id=="b").Label);
                _=host.Dispatcher.BeginInvoke(()=>
                {
                    var dialog=host.OwnedWindows.OfType<GoogleAccountNameDialog>().Single();var input=(TextBox)dialog.FindName("NameInput");var save=(Button)dialog.FindName("SaveButton");
                    input.Text=" ";Assert.False(save.IsEnabled);input.Text=" 家庭生活 ";Assert.True(save.IsEnabled);
                    dialog.UpdateLayout();Capture(dialog,"google-rename-account");save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                });
                RenameItem().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                for(var i=0;i<50&&vm.Busy;i++)await Task.Delay(20);
                Assert.False(vm.Busy);Assert.Equal("家庭生活",vm.Accounts.Single(a=>a.Id=="b").Label);
                Assert.Equal("公司",vm.Accounts.Single(a=>a.Id=="a").Label);Assert.Equal("a",vm.SelectedAccount!.Id);
                host.UpdateLayout();Capture(host,"google-named-accounts");
                Assert.Empty(f.Handler.Requests);Assert.False(f.Get<AdminSession>().IsAuthenticated);
            }
            finally{host.Close();}
        });
    }
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
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Workspace.UseBuiltInClientAsync());
    }
    [Fact] public async Task Custom_client_survives_upgrade_and_can_restore_default_after_signout()
    {
        using var f=new Fixture(new("builtin.apps.googleusercontent.com","desktop-value"));await f.Initialize();
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
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Legacy_calendar_cache_refreshes_before_saved_interval_but_respects_failure_backoff(bool fail)
    {
        using var f=new Fixture();await f.Initialize(UsageModes.Company);
        var source=new GoogleSource{Kind="Calendar",AccountId="b",ResourceId="own",IntervalMinutes=1440};
        await f.Workspace.SaveSourceAsync(source);await f.Workspace.SyncAsync(source.Id);
        var repo=f.Get<ITaskRepository>();var original=Assert.Single(await repo.GetAllAsync());
        await repo.ReplaceCloudCacheAsync(source.CacheSource,[original with{CalendarStartAt=null,GoogleReminderAt=null,GoogleReminderEnabled=null}]);
        Assert.False((await repo.GetByIdAsync(original.Id))!.IsGoogleCalendar);
        var before=f.Handler.Requests.Count;f.Handler.FailCalendar=fail;
        await f.Workspace.SyncAsync(source.Id,automatic:true);
        Assert.True(f.Handler.Requests.Count>before);
        var refreshed=(await repo.GetByIdAsync(original.Id))!;
        if(fail)
        {
            Assert.False(refreshed.IsGoogleCalendar);Assert.Equal(original.ScheduledAt,refreshed.ScheduledAt);
            Assert.True((await f.Workspace.GetAsync()).Sources.Single(s=>s.Id==source.Id).RetryAfter>DateTimeOffset.UtcNow);
        }
        else
        {
            Assert.True(refreshed.IsGoogleCalendar);Assert.True(new TaskRow(refreshed,null).CanAdjustReminder);
            await f.Get<ITaskService>().SaveCalendarReminderAsync(original.Id,false,null);
        }
        var after=f.Handler.Requests.Count;
        await f.Workspace.SyncAsync(source.Id,automatic:true);Assert.Equal(after,f.Handler.Requests.Count);
        if(fail){f.Handler.FailCalendar=false;await f.Workspace.SyncAsync(source.Id);Assert.True((await repo.GetByIdAsync(original.Id))!.IsGoogleCalendar);}
        else Assert.False((await repo.GetByIdAsync(original.Id))!.Enabled);
        Assert.False(f.Get<AdminSession>().IsAuthenticated);
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
        var ev=JsonSerializer.SerializeToElement(new{id="day",summary="全天",start=new{date="2026-11-01"},reminders=new{useDefault=false,overrides=new[]{new{method="popup",minutes=60}}}});
        var task=GoogleCalendarProjection.Project(ev,source,"test","Asia/Taipei",DateTimeOffset.UtcNow)!;
        Assert.Equal(new DateTimeOffset(2026,10,31,23,0,0,TimeSpan.FromHours(8)).LocalDateTime,task.ScheduledAt);
        Assert.Null(GoogleCalendarProjection.Project(ev,source with{IncludeAllDay=false},"test","Asia/Taipei",DateTimeOffset.UtcNow));
        var declined=JsonSerializer.SerializeToElement(new{id="day",attendees=new[]{new{self=true,responseStatus="declined"}}});
        Assert.Null(GoogleCalendarProjection.Project(declined,source,"test","Asia/Taipei",DateTimeOffset.UtcNow));
    }
    [Fact] public void Calendar_reminders_resolve_defaults_overrides_and_no_popup_without_source_fallback()
    {
        var at=DateTimeOffset.Parse("2026-11-01T10:00:00+08:00");
        var defaults=JsonSerializer.SerializeToElement(new[]{new{method="popup",minutes=30},new{method="email",minutes=1440}});
        var source=new GoogleSource{Kind="Calendar",ReminderMinutes=99};
        AlarmTask Project(object reminders)=>GoogleCalendarProjection.Project(JsonSerializer.SerializeToElement(new{id="event",start=new{dateTime=at.ToString("O")},reminders}),source,"test","Asia/Taipei",DateTimeOffset.UtcNow,defaults)!;
        var inherited=Project(new{useDefault=true});Assert.True(inherited.Enabled);Assert.Equal(at.AddMinutes(-30).LocalDateTime,inherited.ScheduledAt);
        var own=Project(new{useDefault=false,overrides=new[]{new{method="popup",minutes=10},new{method="popup",minutes=60}}});
        Assert.Equal(at.AddHours(-1).LocalDateTime,own.ScheduledAt);Assert.True(own.IsGoogleCalendar);
        var none=Project(new{useDefault=false});Assert.False(none.Enabled);Assert.Equal(at.LocalDateTime,none.ScheduledAt);
        var email=Project(new{useDefault=false,overrides=new[]{new{method="email",minutes=20}}});Assert.False(email.Enabled);
    }
    [Fact] public async Task Calendar_local_reminders_survive_sync_and_reset_to_latest_google_values()
    {
        using var f=new Fixture();await f.Initialize(UsageModes.Company);
        var source=new GoogleSource{Kind="Calendar",AccountId="b",ResourceId="own",Name="個人日曆"};await f.Workspace.SaveSourceAsync(source);
        await f.Workspace.SyncAsync(source.Id);
        var repo=f.Get<ITaskRepository>();var service=f.Get<ITaskService>();
        var task=Assert.Single(await repo.GetAllAsync());Assert.True(task.Enabled);Assert.False(task.HasCalendarReminderOverride);
        Assert.Equal(f.Handler.EventAt.AddMinutes(-10).LocalDateTime,task.GoogleReminderAt);
        await service.SaveCalendarReminderAsync(task.Id,false,null);
        f.Handler.EventAt=f.Handler.EventAt.AddHours(2);f.Handler.PopupMinutes=45;await f.Workspace.SyncAsync(source.Id);
        task=(await repo.GetByIdAsync(task.Id))!;Assert.False(task.Enabled);Assert.Null(task.CalendarReminderAt);
        Assert.Equal(f.Handler.EventAt.AddMinutes(-45).LocalDateTime,task.ScheduledAt);
        var chosen=task.ScheduledAt.AddHours(-3);await service.SaveCalendarReminderAsync(task.Id,true,chosen);
        f.Handler.EventAt=f.Handler.EventAt.AddDays(1);f.Handler.PopupMinutes=null;await f.Workspace.SyncAsync(source.Id);
        task=(await repo.GetByIdAsync(task.Id))!;Assert.True(task.Enabled);Assert.Equal(chosen,task.ScheduledAt);Assert.False(task.GoogleReminderEnabled);
        Assert.Equal(f.Handler.EventAt.LocalDateTime,task.CalendarStartAt);
        await f.Get<IDatabaseInitializer>().InitializeAsync();Assert.Equal(chosen,(await repo.GetByIdAsync(task.Id))!.ScheduledAt);
        await service.ResetCalendarReminderAsync(task.Id);task=(await repo.GetByIdAsync(task.Id))!;
        Assert.False(task.Enabled);Assert.False(task.HasCalendarReminderOverride);Assert.Equal(f.Handler.EventAt.LocalDateTime,task.ScheduledAt);
        f.Handler.PopupMinutes=5;await f.Workspace.SyncAsync(source.Id);task=(await repo.GetByIdAsync(task.Id))!;
        Assert.True(task.Enabled);Assert.Equal(f.Handler.EventAt.AddMinutes(-5).LocalDateTime,task.ScheduledAt);
        Assert.False(f.Get<AdminSession>().IsAuthenticated);Assert.Equal(0,f.Handler.Writes);
        await service.SaveCalendarReminderAsync(task.Id,false,chosen);f.Handler.CancelEvent=true;await f.Workspace.SyncAsync(source.Id);
        Assert.Null(await repo.GetByIdAsync(task.Id));
        f.Handler.CancelEvent=false;await f.Workspace.SyncAsync(source.Id);Assert.False((await repo.GetByIdAsync(task.Id))!.HasCalendarReminderOverride);
    }
    [Fact] public async Task Calendar_task_ui_allows_personal_reminders_without_unlocking_other_cloud_tasks()
    {
        await MilestoneOneTests.RunSta(async()=>
        {
            using var f=new Fixture();await f.Initialize(UsageModes.Company);
            var source=new GoogleSource{Kind="Calendar",AccountId="b",ResourceId="own",Name="個人日曆"};await f.Workspace.SaveSourceAsync(source);await f.Workspace.SyncAsync();
            var vm=f.Get<MainViewModel>();var window=f.Get<MainWindow>();
            try
            {
                await vm.InitializeAsync();window.ShowActivated=false;window.ShowInTaskbar=false;window.Width=1200;window.Height=900;window.Show();vm.PageIndex=1;
                await vm.RefreshCommand.ExecuteAsync(null);window.UpdateLayout();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var row=vm.Tasks.Single(t=>t.Task.IsGoogleCalendar);vm.SetTaskSelection([row]);vm.SelectedTask=row;
                Assert.True(vm.CanEditSelectedTask);Assert.Equal("提醒設定",vm.EditTaskLabel);Assert.False(vm.DeleteTaskCommand.CanExecute(null));
                Assert.True(vm.ToggleRowCommand.CanExecute(row));Assert.All(vm.Tasks.Where(t=>!t.Task.IsGoogleCalendar),t=>Assert.False(vm.ToggleRowCommand.CanExecute(t)));
                await vm.ToggleRowCommand.ExecuteAsync(row);
                var saved=(await f.Get<ITaskRepository>().GetByIdAsync(row.Task.Id))!;Assert.False(saved.Enabled);
                var editor=new CalendarReminderWindow(f.Get<ITaskService>(),saved){Owner=window};
                editor.Loaded+=(_,_)=>editor.Dispatcher.BeginInvoke(()=>
                {
                    editor.UpdateLayout();Capture(editor,"calendar-reminder-disabled");
                    ((CheckBox)editor.FindName("ReminderEnabled")).IsChecked=true;
                    ((DatePicker)editor.FindName("ReminderDate")).SelectedDate=f.Handler.EventAt.LocalDateTime.Date.AddDays(1);
                    ((TextBox)editor.FindName("ReminderTime")).Text="08:15";
                    editor.UpdateLayout();Capture(editor,"calendar-reminder-editor");
                    Assert.InRange(((ScrollViewer)editor.FindName("EditorScroll")).ScrollableHeight,0,1);
                    var date=(DatePicker)editor.FindName("ReminderDate");
                    Assert.InRange(Math.Abs(date.ActualHeight-((TextBox)editor.FindName("ReminderTime")).ActualHeight),0,1);
                    AdaptiveBrushExtension.Apply(true);editor.UpdateLayout();Capture(editor,"calendar-reminder-dark");
                    date.IsDropDownOpen=true;editor.UpdateLayout();
                    var popup=(System.Windows.Controls.Primitives.Popup)date.Template.FindName("PART_Popup",date);
                    Assert.True(popup.IsOpen);
                    var calendar=Find<Calendar>(popup.Child)!;calendar.UpdateLayout();Capture(calendar,"calendar-reminder-picker-dark");
                    date.IsDropDownOpen=false;
                    AdaptiveBrushExtension.Apply(false);
                    editor.Height=440;editor.UpdateLayout();Capture(editor,"calendar-reminder-small");
                    Assert.True(((Button)editor.FindName("SaveButton")).IsVisible);
                    ((Button)editor.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                });
                Assert.True(editor.ShowDialog());
                saved=(await f.Get<ITaskRepository>().GetByIdAsync(row.Task.Id))!;Assert.True(saved.Enabled);Assert.Equal(new TimeSpan(8,15,0),saved.ScheduledAt.TimeOfDay);Assert.True(saved.HasCalendarReminderOverride);
                var restore=new CalendarReminderWindow(f.Get<ITaskService>(),saved){Owner=window};
                restore.Loaded+=(_,_)=>restore.Dispatcher.BeginInvoke(()=>((Button)restore.FindName("ResetButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
                Assert.True(restore.ShowDialog());Assert.False((await f.Get<ITaskRepository>().GetByIdAsync(row.Task.Id))!.HasCalendarReminderOverride);
                await vm.RefreshCommand.ExecuteAsync(null);window.UpdateLayout();Capture(window,"calendar-personal-task-reminders");
            }
            finally{AdaptiveBrushExtension.Apply(false);window.ForceClose();}
        });
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
        await f.Initialize();await Assert.ThrowsAsync<ArgumentException>(()=>f.Workspace.ImportClientAsync("{}"));
        await Assert.ThrowsAsync<ArgumentException>(()=>f.Workspace.ImportClientAsync("{\"type\":\"service_account\"}"));
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
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Workspace.ImportClientAsync("{\"installed\":{\"client_id\":\"test.apps.googleusercontent.com\",\"client_secret\":\"test-secret\"}}"));
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
                main.PageIndex=4;main.Preferences.SelectedPage=SettingsPage.Google;window.UpdateLayout();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var vm=main.Preferences.Google!;await vm.LoadAsync();Assert.Equal(2,vm.Accounts.Count);Assert.Equal(3,vm.Tabs.Count);
                vm.Tabs[2].Kind="Google 日曆";vm.Tabs[2].Name="家庭行程";
                var view=Find<GoogleWorkspaceView>(window)!;Assert.True(view.IsVisible);
                Assert.True(view.IsEnabled);
                main.Preferences.SelectedPage=SettingsPage.Sound;await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);Assert.False(view.IsVisible);
                main.Preferences.SelectedPage=SettingsPage.Google;await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);Assert.True(view.IsVisible);
                vm.Selected=vm.Tabs[1];vm.Selected.Name="未儲存草稿";vm.Selected=vm.Tabs[2];vm.Selected=vm.Tabs[1];Assert.Equal("未儲存草稿",vm.Selected.Name);Assert.True(vm.Selected.Locked);
                vm.Selected=vm.Tabs[0];window.UpdateLayout();await Task.Delay(180);Capture(window,"google-overview");
                vm.Selected=vm.Tabs[1];window.UpdateLayout();await Task.Delay(180);Capture(window,"google-source");
                Assert.Null(view.FindName("SourceTabs")); // Sources open in the same content pane, without a third navigation column.
                vm.Selected.Locked=false;
                var scroll=(ScrollViewer)view.FindName("SettingsScroll");
                var editor=(Expander)view.FindName("TaskEditorExpander");
                var save=(Button)view.FindName("SaveSourceButton");
                var sync=(Button)view.FindName("SyncSourceButton");
                window.UpdateLayout();Assert.True(save.IsEnabled);Assert.False(sync.IsEnabled);Assert.True(sync.ActualHeight>=32);
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
                    Assert.False(editor.IsVisible);window.UpdateLayout();scroll.ScrollToEnd();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    var before=save.TransformToAncestor(view).Transform(new Point());
                    scroll.ScrollToTop();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    Assert.Equal(before,save.TransformToAncestor(view).Transform(new Point()));
                    editor.IsExpanded=false;window.UpdateLayout();
                    Capture(window,$"google-management-{size.Width}x{size.Height}");
                    vm.Selected=vm.Tabs[0];window.UpdateLayout();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);Capture(window,$"google-accounts-{size.Width}x{size.Height}");
                    vm.Selected=vm.Tabs[1];window.UpdateLayout();
                }
                AdaptiveBrushExtension.Apply(true);window.UpdateLayout();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);Capture(window,"google-management-dark");
                scroll.ScrollToEnd();vm.Selected=vm.Tabs[0];await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);Assert.Equal(0,scroll.VerticalOffset);
                Capture(window,"google-accounts-dark");
                vm.Accounts.Clear();window.UpdateLayout();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.True(((Border)view.FindName("EmptyAccounts")).IsVisible);
                Assert.True(((Button)view.FindName("AddAccountButton")).IsEnabled);
                Capture(window,"google-accounts-empty-dark");
            }
            finally{AdaptiveBrushExtension.Apply(false);window.ForceClose();}
        });
    }
    [Fact] public async Task Google_dialogs_keep_cancellation_safe_and_apply_only_explicit_choices()
    {
        await MilestoneOneTests.RunSta(async()=>
        {
            using var f=new Fixture();await f.Initialize(UsageModes.Company);
            var vm=f.Get<GoogleWorkspaceViewModel>();await vm.LoadAsync();
            var initialLabel=vm.AccountLabel;
            var signIn=new GoogleSignInDialog(vm){ShowInTaskbar=false};
            signIn.Loaded+=(_,_)=>signIn.Dispatcher.BeginInvoke(()=>
            {
                var label=(TextBox)signIn.FindName("AccountLabelInput");
                var sheets=(CheckBox)signIn.FindName("SheetsToggle");
                var calendar=(CheckBox)signIn.FindName("CalendarToggle");
                var write=(CheckBox)signIn.FindName("WriteToggle");
                var submit=(Button)signIn.FindName("ContinueButton");
                write.IsChecked=true;sheets.IsChecked=false;calendar.IsChecked=false;
                Assert.False(write.IsEnabled);Assert.False(write.IsChecked);Assert.False(submit.IsEnabled);
                label.Text="私人帳號";calendar.IsChecked=true;Assert.True(submit.IsEnabled);
                label.Text=" ";Assert.False(submit.IsEnabled);label.Text="私人帳號";
                signIn.UpdateLayout();Capture(signIn,"google-add-account");
                AdaptiveBrushExtension.Apply(true);signIn.UpdateLayout();Capture(signIn,"google-add-account-dark");AdaptiveBrushExtension.Apply(false);
                signIn.Close();
            });
            Assert.NotEqual(true,signIn.ShowDialog());Assert.Equal(initialLabel,vm.AccountLabel);Assert.True(vm.UseSheets);
            var accept=new GoogleSignInDialog(vm){ShowInTaskbar=false};
            accept.Loaded+=(_,_)=>accept.Dispatcher.BeginInvoke(()=>
            {
                ((TextBox)accept.FindName("AccountLabelInput")).Text=" 公司 ";
                ((CheckBox)accept.FindName("WriteToggle")).IsChecked=true;
                ((Button)accept.FindName("ContinueButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            });
            Assert.True(accept.ShowDialog());Assert.Equal("公司",vm.AccountLabel);Assert.True(vm.WriteSheets);
            // The actual remove handler must show the custom dialog and preserve data when cancelled.
            var view=new GoogleWorkspaceView{DataContext=vm};
            var host=new Window{Content=view,Width=1000,Height=720,ShowInTaskbar=false};host.Show();
            try
            {
                vm.Selected=vm.Tabs[1];host.UpdateLayout();
                var taskEditor=FindAll<Button>(view).Single(b=>b.Content is string label&&label=="管理線上任務…");
                var editorOpened=false;
                _=host.Dispatcher.BeginInvoke(()=>
                {
                    var editorWindow=host.OwnedWindows.OfType<Window>().Single(w=>w.Title.StartsWith("線上任務 ·"));
                    Assert.NotNull(FindAll<Button>(editorWindow).Single(b=>b.Content is string text&&text=="讀取任務"));
                    Capture(editorWindow,"google-online-task-editor");editorOpened=true;editorWindow.Close();
                });
                taskEditor.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert.True(editorOpened);
                var originalCount=vm.Tabs.Count;
                var sourceMore=FindAll<Button>(view).Single(b=>b.Content is string label&&label=="⋯");
                sourceMore.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var remove=(MenuItem)sourceMore.ContextMenu!.Items[0];sourceMore.ContextMenu.IsOpen=false;
                var confirmed=false;
                _ = host.Dispatcher.BeginInvoke(()=>
                {
                    var dialog=host.OwnedWindows.OfType<GoogleActionDialog>().Single();
                    Assert.Equal("移除來源",((Button)dialog.FindName("ConfirmButton")).Content);
                    Assert.True(((Button)dialog.FindName("CancelButton")).IsDefault);
                    Assert.False(((Button)dialog.FindName("ConfirmButton")).IsDefault);
                    Capture(dialog,"google-remove-confirmation");
                    dialog.Close();confirmed=true;
                });
                remove.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Assert.True(confirmed);Assert.Equal(originalCount,vm.Tabs.Count);
                Assert.Equal(originalCount-1,(await f.Workspace.GetAsync()).Sources.Count);
                _ = host.Dispatcher.BeginInvoke(()=>
                {
                    var dialog=host.OwnedWindows.OfType<GoogleActionDialog>().Single();
                    ((Button)dialog.FindName("ConfirmButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                });
                remove.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                for(var i=0;i<50&&vm.Busy;i++)await Task.Delay(20);
                Assert.False(vm.Busy);Assert.Equal(originalCount-1,vm.Tabs.Count);
                Assert.Equal(originalCount-2,(await f.Workspace.GetAsync()).Sources.Count);
                // The account-row menu must act on its own account, not the previously selected one.
                host.UpdateLayout();
                vm.SelectedAccount=vm.Accounts.Single(a=>a.Id=="a");
                var list=(ItemsControl)view.FindName("AccountList");
                var menuButton=FindAll<Button>(list).Single(b=>b.ContextMenu is not null && b.DataContext is GoogleAccount {Id:"b"});
                menuButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var menu=menuButton.ContextMenu!;
                Assert.Equal("b",((GoogleAccount)menu.DataContext).Id);
                menu.IsOpen=false;
                _ = host.Dispatcher.BeginInvoke(()=>
                {
                    var dialog=host.OwnedWindows.OfType<GoogleActionDialog>().Single();
                    Assert.Equal("b@example.test",((TextBlock)dialog.FindName("Subject")).Text);
                    Assert.Equal("登出帳號",((Button)dialog.FindName("ConfirmButton")).Content);
                    ((Button)dialog.FindName("ConfirmButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                });
                menu.Items.OfType<MenuItem>().Single(m=>Equals(m.Header,"登出此帳號…")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                for(var i=0;i<50&&vm.Busy;i++)await Task.Delay(20);
                Assert.False(vm.Busy);Assert.Equal("a",Assert.Single(vm.Accounts).Id);
                Assert.Equal("a",Assert.Single((await f.Workspace.GetAsync()).Accounts).Id);
            }
            finally{host.Close();}
        });
    }
    private static IEnumerable<T> FindAll<T>(DependencyObject parent) where T:DependencyObject
    {
        if(parent is T value)yield return value;
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
            foreach(var child in FindAll<T>(VisualTreeHelper.GetChild(parent,i)))yield return child;
    }
    private static T? Find<T>(DependencyObject parent) where T:DependencyObject
    {if(parent is T found)return found;for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)if(Find<T>(VisualTreeHelper.GetChild(parent,i)) is {} child)return child;return null;}
    private static void Capture(FrameworkElement window,string name)
    {
        var directory=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../artifacts/screenshots"));Directory.CreateDirectory(directory);
        var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(directory,name+".png"));png.Save(file);
    }
    private static Dictionary<string,string> ParseQuery(string input)=>input.TrimStart('?').Split('&').Select(x=>x.Split('=',2)).ToDictionary(x=>Uri.UnescapeDataString(x[0]),x=>Uri.UnescapeDataString(x[1].Replace('+',' ')));
    private sealed class MemoryVault:IGoogleVault
    {
        private string json="{}";
        public Action? BeforeRead;
        public Task<GoogleVaultState> ReadAsync(CancellationToken ct){BeforeRead?.Invoke();return Task.FromResult(JsonSerializer.Deserialize<GoogleVaultState>(json)!);}
        public Task WriteAsync(GoogleVaultState state,CancellationToken ct){json=JsonSerializer.Serialize(state);return Task.CompletedTask;}
    }
    private sealed class Handler:HttpMessageHandler
    {
        public int Refreshes,Writes;public bool FailCompany,FailCalendar,InvalidRefresh,CancelEvent,TimeoutAfterWrite;
        public string? ExpectedChallenge;
        public DateTimeOffset EventAt=DateTimeOffset.UtcNow.AddDays(2);
        public int? PopupMinutes=10;
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
                if(FailCalendar)return Reply(new{},HttpStatusCode.ServiceUnavailable);
                if(request.RequestUri.Query.Contains("pageToken"))result=new{items=new[]{new{id="cancelled",status="cancelled"}},timeZone="Asia/Taipei"};
                else result=new{items=new[]{new{id="ev1",summary="會議",status=CancelEvent?"cancelled":"confirmed",start=new{dateTime=EventAt.ToString("O")},reminders=new{useDefault=false,overrides=PopupMinutes is {} minutes?new[]{new{method="popup",minutes}}:[]}}},timeZone="Asia/Taipei",nextPageToken="second"};
            }
            else throw new InvalidOperationException("Unexpected test request: "+path);
            return Reply(result);
        }
        private static HttpResponseMessage Reply(object value,HttpStatusCode status=HttpStatusCode.OK)=>new(status){Content=new StringContent(JsonSerializer.Serialize(value),Encoding.UTF8,"application/json")};
    }
    private sealed class ConfigurationClock:TimeProvider
    {
        private long elapsed;
        public override long TimestampFrequency=>TimeSpan.TicksPerSecond;
        public override long GetTimestamp()=>elapsed;
        public override DateTimeOffset GetUtcNow()=>DateTimeOffset.UtcNow.AddTicks(elapsed);
        public void Advance(TimeSpan duration)=>elapsed+=duration.Ticks;
    }
    private sealed class Fixture:IDisposable,IAppPaths
    {
        public string DataDirectory{get;}=Path.Combine(Path.GetTempPath(),"CloudAlarmGoogleTests",Guid.NewGuid().ToString("N"));
        public string DatabasePath=>Path.Combine(DataDirectory,"test.db");
        public MemoryVault Vault{get;}=new();public Handler Handler{get;}=new();
        public GoogleSource Company{get;}=new(){Name="公司任務",AccountId="a",ResourceId="company",TabId="0"};
        public GoogleSource Personal{get;}=new(){Name="個人任務",AccountId="b",ResourceId="personal",TabId="0"};
        private readonly IHost host;
        public Fixture(GoogleClient? builtIn=null,TimeProvider? clock=null)
        {
            host=new HostBuilder().ConfigureServices(s=>{CompositionRoot.ConfigureServices(s);if(clock is not null)s.AddSingleton(clock);s.AddSingleton<IAppPaths>(this);s.AddSingleton<IGoogleVault>(Vault);s.AddSingleton(new GoogleBuiltInClient(()=>builtIn));s.AddSingleton(new GoogleApi(new HttpClient(Handler)));}).Build();
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

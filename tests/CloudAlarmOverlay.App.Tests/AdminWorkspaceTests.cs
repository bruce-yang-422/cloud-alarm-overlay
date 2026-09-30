using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CloudAlarmOverlay.App.Services;
using CloudAlarmOverlay.App.Styles;
using CloudAlarmOverlay.App.ViewModels;
using CloudAlarmOverlay.App.Views;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using CloudAlarmOverlay.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CloudAlarmOverlay.App.Tests;
public sealed class AdminWorkspaceTests
{
    [Fact] public async Task Grouped_workspace_preserves_drafts_permissions_and_advanced_disclosure()
    {
        await MilestoneOneTests.RunSta(async()=>
        {
            using var fixture=new Fixture();await fixture.Initialize();
            var vm=fixture.Get<MainViewModel>();await vm.InitializeAsync();
            var window=fixture.Get<MainWindow>();window.Width=1344;window.Height=960;window.ShowActivated=false;window.ShowInTaskbar=false;window.Show();
            try
            {
                vm.PageIndex=5;window.UpdateLayout();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var view=Find<AdminView>(window)!;Assert.NotNull(view);
                Assert.Equal(9,vm.Admin.Navigation.Length);Assert.DoesNotContain(vm.Admin.Navigation,n=>n.Name.Contains("Google"));
                for(var i=0;i<9;i++)
                {
                    vm.Admin.SelectedTab=i;window.UpdateLayout();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    Assert.Equal(i,((TabControl)view.FindName("AdminPages")).SelectedIndex);
                    Assert.Equal(i==7,vm.Admin.ShowsLogFilter);
                    Capture(window,"admin-workspace-"+i);
                }
                vm.Admin.SelectedTab=1;window.UpdateLayout();var saved=vm.SheetAId;vm.SheetAId="draft-sheet";
                Assert.True(((Border)view.FindName("DraftFooter")).IsVisible);
                fixture.Dialogs.ConfirmResult=false;vm.Admin.SelectedTab=4;Assert.Equal(1,vm.Admin.SelectedTab);Assert.Equal("draft-sheet",vm.SheetAId);
                vm.PageIndex=4;Assert.Equal(5,vm.PageIndex);
                fixture.Dialogs.ConfirmResult=true;vm.Admin.SelectedTab=4;Assert.Equal(saved,vm.SheetAId);
                window.UpdateLayout();var advanced=(Expander)view.FindName("GoogleLoginAdvanced");var import=(Button)view.FindName("ImportGoogleClientButton");
                Assert.False(advanced.IsExpanded);Assert.False(import.IsVisible);advanced.IsExpanded=true;
                window.UpdateLayout();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);Assert.True(import.IsVisible);Assert.True(import.IsEnabled);
                Capture(window,"admin-workspace-oauth-expanded");
                vm.Admin.SelectedTab=3;window.UpdateLayout();var old=vm.Admin.FlashMilliseconds;vm.Admin.FlashMilliseconds=900;
                Assert.NotEqual("900",(await fixture.Get<ISettingsRepository>().GetAsync("FlashMilliseconds"))?.Value);
                await vm.Admin.SavePoliciesAsync();Assert.Equal("900",(await fixture.Get<ISettingsRepository>().GetAsync("FlashMilliseconds"))?.Value);
                vm.Admin.FlashMilliseconds=old; // restore the UI baseline before checking navigation.
                vm.Admin.SelectedTab=1;window.Width=1050;window.Height=680;window.UpdateLayout();
                Assert.True(((Button)view.FindName("NavigationToggle")).IsVisible);
                Assert.False(((Border)view.FindName("NavigationPanel")).IsVisible);
                ((Button)view.FindName("NavigationToggle")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));window.UpdateLayout();Assert.True(((Border)view.FindName("NavigationPanel")).IsVisible);
                Capture(window,"admin-workspace-narrow");
                window.Width=1344;window.Height=960;AdaptiveBrushExtension.Apply(true);window.UpdateLayout();Capture(window,"admin-workspace-dark");
                vm.Admin.Session.SignOut();await vm.Admin.SessionChangedAsync();window.UpdateLayout();
                await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>vm.Admin.SaveWorkspaceSectionAsync(5));
                Assert.False(((TabControl)view.FindName("AdminPages")).IsEnabled);
            }
            finally{AdaptiveBrushExtension.Apply(false);window.ForceClose();}
        });
    }
    [Fact] public async Task Json_preview_cancel_and_expiry_never_write_settings()
    {
        using var f=new Fixture();await f.Initialize();var admin=f.Get<AdminViewModel>();var settings=f.Get<ISettingsRepository>();
        f.Dialogs.Json="{\"FormatVersion\":1,\"UpdateManifestUrl\":\"https://example.com/version.json\"}";
        f.Dialogs.ConfirmResult=false;Assert.False(await admin.ImportSettingsJsonAsync());Assert.Null(await settings.GetAsync("UpdateManifestUrl"));Assert.Contains("程式更新網址",f.Dialogs.LastPrompt);
        f.Dialogs.ConfirmResult=true;f.Dialogs.OnConfirm=f.Get<AdminSession>().SignOut;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>admin.ImportSettingsJsonAsync());Assert.Null(await settings.GetAsync("UpdateManifestUrl"));
    }
    [Fact] public async Task Administration_displays_each_operation_result_once()
    {
        await MilestoneOneTests.RunSta(async()=>
        {
            using var f=new Fixture();await f.Initialize();var vm=f.Get<MainViewModel>();await vm.InitializeAsync();
            var window=f.Get<MainWindow>();window.ShowActivated=false;window.ShowInTaskbar=false;window.Show();
            try
            {
                vm.PageIndex=5;vm.Admin.SelectedTab=1;window.UpdateLayout();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                const string syncResult="同步完成：5 個分頁成功，0 個失敗。";
                vm.Status=vm.SyncMessage=vm.Admin.Message=syncResult;window.UpdateLayout();
                Assert.Single(VisibleResults(window,syncResult));Capture(window,"admin-workspace-single-result");
                vm.Admin.SelectedTab=2;window.UpdateLayout();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.Empty(VisibleResults(window,syncResult));
                vm.Admin.CalendarData.Message="日曆更新失敗，請稍後重試。";window.UpdateLayout();
                Assert.Single(VisibleResults(window,vm.Admin.CalendarData.Message));
                vm.Admin.SelectedTab=6;window.UpdateLayout();
                vm.Preferences.Maintenance.Message="備份已完成。";window.UpdateLayout();
                Assert.Single(VisibleResults(window,"備份已完成。"));
                vm.Admin.Message="匯入失敗，請確認 JSON 格式。";window.UpdateLayout();
                Assert.Single(VisibleResults(window,vm.Admin.Message));Assert.Empty(VisibleResults(window,"備份已完成。"));
                vm.PageIndex=1;window.UpdateLayout();Assert.Single(VisibleResults(window,syncResult));
            }
            finally{window.ForceClose();}
        });
    }
    private static IEnumerable<TextBlock> VisibleResults(DependencyObject node,string text)
    {
        if(node is TextBlock {IsVisible:true} block&&block.Text==text)yield return block;
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)
            foreach(var match in VisibleResults(VisualTreeHelper.GetChild(node,i),text))yield return match;
    }
    [Fact] public void Draft_reload_preserves_only_user_changes_and_discard_restores_latest_saved_values()
    {
        var vm=new Dummy();var state=new AdminDraftState();state.Track(1,vm,nameof(vm.First),nameof(vm.Second));
        vm.First="draft";state.BeginReload();vm.First="new saved";vm.Second="other saved";state.EndReload();
        Assert.Equal("draft",vm.First);Assert.Equal("other saved",vm.Second);Assert.True(state.IsDirty(1));
        state.Discard(1);Assert.Equal("new saved",vm.First);Assert.False(state.IsDirty(1));
        vm.First="another draft";state.BeginReload();state.EndReload();Assert.True(state.IsDirty(1));
        state.Discard(1);Assert.Equal("new saved",vm.First);
    }
    [Fact] public async Task Calendar_draft_survives_reopen_and_failed_save_then_can_be_discarded()
    {
        using var f=new Fixture();await f.Initialize();var calendar=f.Get<CalendarDataViewModel>();
        await calendar.LoadAsync();var saved=calendar.HolidaysUrl;
        var drafts=new AdminDraftState();drafts.Track(2,calendar,"Enabled","Frequency","HolidaysUrl","LunarUrl");
        calendar.HasPendingChanges=()=>drafts.IsDirty(2);
        calendar.SettingsLoaded+=()=>drafts.Accept(2);
        calendar.HolidaysUrl="invalid-url";await calendar.OpenAsync();
        Assert.Equal("invalid-url",calendar.HolidaysUrl);Assert.True(drafts.IsDirty(2));
        await Assert.ThrowsAnyAsync<Exception>(()=>calendar.SaveDraftAsync());Assert.True(drafts.IsDirty(2));
        Assert.Equal(saved,(await f.Get<CalendarDataService>().GetAsync()).Options.HolidaysUrl);
        drafts.Discard(2);await calendar.OpenAsync();Assert.Equal(saved,calendar.HolidaysUrl);Assert.False(drafts.IsDirty(2));
    }
    [Fact] public async Task Credential_dialog_clears_passwords_and_blocks_mutation_after_session_expiry()
    {
        await MilestoneOneTests.RunSta(async()=>
        {
            var authorized=true;var writes=0;
            var dialog=new AdminCredentialsDialog("backup","admin",()=>{if(!authorized)throw new UnauthorizedAccessException();},_=>{writes++;return Task.CompletedTask;});
            dialog.ShowActivated=false;dialog.ShowInTaskbar=false;dialog.Show();
            try
            {
                var password=(PasswordBox)dialog.FindName("CurrentPassword");password.Password="test-only";
                authorized=false;
                ((Button)dialog.FindName("ApplyButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.Equal(0,writes);Assert.Empty(password.Password);
                Assert.NotEmpty(((TextBlock)dialog.FindName("Error")).Text);
                Capture(dialog,"admin-workspace-credentials");
            }
            finally{dialog.Close();}
        });
    }
    private sealed class Dummy {public string First{get;set;}="old";public string Second{get;set;}="old";}
    private static T? Find<T>(DependencyObject node)where T:DependencyObject
    {if(node is T found)return found;for(var i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)if(Find<T>(VisualTreeHelper.GetChild(node,i)) is {} child)return child;return null;}
    private static void Capture(Window window,string name)
    {
        var folder=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../artifacts/screenshots"));Directory.CreateDirectory(folder);
        var content=(FrameworkElement)window.Content;var dpi=VisualTreeHelper.GetDpi(content);
        var bmp=new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth*dpi.DpiScaleX),(int)Math.Ceiling(content.ActualHeight*dpi.DpiScaleY),96*dpi.DpiScaleX,96*dpi.DpiScaleY,PixelFormats.Pbgra32);
        var drawing=new DrawingVisual();using(var dc=drawing.RenderOpen())dc.DrawRectangle(new VisualBrush(content),null,new Rect(0,0,content.ActualWidth,content.ActualHeight));bmp.Render(drawing);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bmp));using var file=File.Create(Path.Combine(folder,name+".png"));png.Save(file);
    }
    private sealed class TestDialogs:IUserDialogs
    {
        public bool ConfirmResult=true;public Action? OnConfirm;public string? Json;public string LastPrompt="";
        public bool Confirm(string message){LastPrompt=message;OnConfirm?.Invoke();return ConfirmResult;}
        public void Edit(AlarmTask? task,bool copy){}public void Export(string contents){}
        public Task<string?> OpenSettingsJsonAsync()=>Task.FromResult(Json);
    }
    private sealed class AutoStart:IAutoStartService {public bool IsEnabled()=>false;public void SetEnabled(bool enabled){}}
    private sealed class Fixture:IDisposable,IAppPaths
    {
        public string DataDirectory{get;}=Path.Combine(Path.GetTempPath(),"CloudAlarmAdminWorkspace",Guid.NewGuid().ToString("N"));
        public string DatabasePath=>Path.Combine(DataDirectory,"test.db");
        public TestDialogs Dialogs{get;}=new();private readonly IHost host;
        public Fixture()=>host=new HostBuilder().ConfigureServices(s=>{CompositionRoot.ConfigureServices(s);s.AddSingleton<IAppPaths>(this);s.AddSingleton<IUserDialogs>(Dialogs);s.AddSingleton<IAutoStartService,AutoStart>();}).Build();
        public T Get<T>()where T:notnull=>host.Services.GetRequiredService<T>();
        public async Task Initialize(){await Get<IDatabaseInitializer>().InitializeAsync();var auth=Get<IAuthenticationService>();await auth.EnsureDefaultAdministratorAsync();Assert.True(await auth.AuthenticateAsync("admin","12345"));}
        public void Dispose(){host.Dispose();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(DataDirectory))Directory.Delete(DataDirectory,true);}
    }
}

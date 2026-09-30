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
public sealed class SettingsWorkspaceTests
{
    [Fact] public async Task Settings_categories_guard_drafts_keep_device_last_and_render_at_desktop_sizes()
    {
        await MilestoneOneTests.RunSta(async()=>
        {
            using var f=new Fixture();await f.Initialize();var main=f.Get<MainViewModel>();await main.InitializeAsync();
            var window=f.Get<MainWindow>();window.Width=1344;window.Height=900;window.ShowActivated=false;window.ShowInTaskbar=false;window.Show();
            try
            {
                main.PageIndex=4;window.UpdateLayout();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var view=Find<PreferencesView>(window)!;var prefs=main.Preferences;
                Assert.Equal(8,prefs.Navigation.Length);Assert.Equal(SettingsPage.Device,prefs.Navigation.Last().Page);
                var tabs=(TabControl)view.FindName("SettingsTabs");
                foreach(var item in prefs.Navigation)
                {
                    prefs.SelectedPage=item.Page;window.UpdateLayout();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    Assert.Equal(item.Page,tabs.SelectedValue);Capture(window,"settings-workspace-"+item.Page);
                    if(item.Page==SettingsPage.Appearance)Assert.Null(Find<SoftwareUpdateView>(view));
                    if(item.Page==SettingsPage.Updates)Assert.NotNull(Find<SoftwareUpdateView>(view));
                }
                prefs.SelectedPage=SettingsPage.Quiet;window.UpdateLayout();prefs.QuietPeriods.Add(new(){Start="22:00",End="08:00"});
                Assert.True(((Border)view.FindName("DraftFooter")).IsVisible);
                f.Dialogs.ConfirmResult=false;prefs.SelectedPage=SettingsPage.Home;Assert.Equal(SettingsPage.Quiet,prefs.SelectedPage);
                main.PageIndex=1;Assert.Equal(4,main.PageIndex);
                f.Dialogs.ConfirmResult=true;prefs.SelectedPage=SettingsPage.Home;Assert.False(prefs.IsPageDirty(SettingsPage.Quiet));
                prefs.SelectedPage=SettingsPage.Quiet;prefs.QuietPeriods.Add(new(){Start="22:00",End="08:00"});await prefs.SavePageAsync();
                Assert.False(prefs.IsPageDirty(SettingsPage.Quiet));Assert.Contains("22:00",(await f.Get<ISettingsRepository>().GetAsync("QuietPeriods"))!.Value);
                foreach(var size in new[]{new Size(1050,680),new Size(1180,816),new Size(1440,900)})
                {
                    window.Width=size.Width;window.Height=size.Height;
                    foreach(var item in prefs.Navigation){prefs.SelectedPage=item.Page;window.UpdateLayout();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);Assert.Equal(item.Page,tabs.SelectedValue);}
                    prefs.SelectedPage=SettingsPage.Sound;window.UpdateLayout();Capture(window,$"settings-workspace-{size.Width}");
                }
                window.Width=1050;window.UpdateLayout();Assert.True(((Button)view.FindName("NavigationToggle")).IsVisible);
                ((Button)view.FindName("NavigationToggle")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert.True(((Border)view.FindName("NavigationPanel")).IsVisible);
                AdaptiveBrushExtension.Apply(true);prefs.SelectedPage=SettingsPage.Appearance;window.Width=1344;window.UpdateLayout();Capture(window,"settings-workspace-dark");
                foreach(var label in All<TextBlock>((ListBox)view.FindName("NavigationList")).Where(t=>prefs.Navigation.Any(n=>n.Name==t.Text)))
                    Assert.True(Assert.IsType<SolidColorBrush>(label.Foreground).Color.R>150,"Dark navigation text must remain readable.");
            }
            finally{AdaptiveBrushExtension.Apply(false);window.ForceClose();}
        });
    }
    [Fact] public async Task Group_validation_prevents_partial_write_and_discard_restores_saved_settings()
    {
        using var f=new Fixture();await f.Initialize();var prefs=f.Get<PreferencesViewModel>();await prefs.LoadAsync();var settings=f.Get<ISettingsRepository>();
        prefs.SelectedPage=SettingsPage.Quiet;var savedFlash=(await settings.GetAsync("FlashMilliseconds"))?.Value;
        prefs.FlashMilliseconds=800;prefs.QuietPeriods.Add(new(){Start="bad",End="08:00"});await prefs.SavePageAsync();
        Assert.Equal(savedFlash,(await settings.GetAsync("FlashMilliseconds"))?.Value);Assert.True(prefs.IsPageDirty(SettingsPage.Quiet));Assert.Contains("尚未儲存",prefs.Message);
        prefs.DiscardPage(SettingsPage.Quiet);Assert.False(prefs.IsPageDirty(SettingsPage.Quiet));
        prefs.SelectedPage=SettingsPage.Home;var oldLimit=(await settings.GetAsync(HomePinOptions.SettingKey))?.Value;
        prefs.HomePinLimit=4;prefs.Weather!.SelectedCounty="臺北市";prefs.Weather.SelectedDistrict=null;await prefs.SavePageAsync();
        Assert.Equal(oldLimit,(await settings.GetAsync(HomePinOptions.SettingKey))?.Value);Assert.True(prefs.IsPageDirty(SettingsPage.Home));
        prefs.DiscardPage(SettingsPage.Home);Assert.False(prefs.IsPageDirty(SettingsPage.Home));
        prefs.SelectedPage=SettingsPage.Editing;prefs.EmojiItems.Add("🎯");prefs.CountdownShareBranding=false;await prefs.SavePageAsync();
        Assert.False(prefs.IsPageDirty(SettingsPage.Editing));Assert.Equal("false",(await settings.GetAsync(CountdownShareSnapshot.BrandingSettingKey))!.Value);
        prefs.EmojiItems.Clear();prefs.DiscardPage(SettingsPage.Editing);Assert.Contains("🎯",prefs.EmojiItems);
    }
    [Fact] public void Source_draft_discard_preserves_source_parameters_and_ignores_status_refresh()
    {
        var source=new GoogleSource{Kind="Calendar",Name="日曆",AccountId="personal",ResourceId="calendar",IncludeAllDay=true,AllDayHour=8,ReminderMinutes=15};
        var draft=new GoogleSourceDraft(source);draft.Status="同步成功";draft.LastSync="today";Assert.False(draft.HasChanges);
        draft.Locked=false;draft.Kind="Google Sheets";draft.Name="變更";draft.IntervalMinutes=12;Assert.True(draft.HasChanges);
        draft.Restore();Assert.True(draft.Locked);Assert.False(draft.HasChanges);Assert.Equal("calendar",draft.ResourceId);Assert.Equal(8,draft.AllDayHour);Assert.True(draft.IncludeAllDay);
        draft.Name="已儲存";draft.Accept();draft.Name="草稿";draft.Restore();Assert.Equal("已儲存",draft.Name);
    }
    [Fact] public async Task Failed_group_save_keeps_only_unsaved_drafts_and_sound_failure_rolls_back()
    {
        using var f=new Fixture();await f.Initialize();var repository=new FailingSettings(f.Get<ISettingsRepository>());
        var prefs=new PreferencesViewModel(repository,f.Get<NotificationPreferences>(),f.Get<ISoundService>(),new EmojiLibrary(repository),f.Get<MaintenanceViewModel>(),f.Get<ChangeSignal>());
        await prefs.LoadAsync();prefs.SelectedPage=SettingsPage.Editing;
        prefs.EmojiItems.Add("🎯");prefs.CountdownShareBranding=false;repository.FailKey=CountdownShareSnapshot.BrandingSettingKey;
        await prefs.SavePageAsync();Assert.True(prefs.IsPageDirty(SettingsPage.Editing));Assert.Contains("已儲存：常用 emoji",prefs.Message);
        Assert.Contains("🎯",(await repository.GetAsync("EmojiLibrary"))!.Value);
        prefs.DiscardPage(SettingsPage.Editing);Assert.Contains("🎯",prefs.EmojiItems);Assert.True(prefs.CountdownShareBranding);
        prefs.SelectedPage=SettingsPage.Sound;var row=prefs.Sounds[0];var saved=row.Enabled;repository.FailKey="Sound:"+row.Level;
        row.Enabled=!saved;
        for(var i=0;i<50&&row.Enabled!=saved;i++)await Task.Delay(10);
        Assert.Equal(saved,row.Enabled);Assert.Contains("音效設定未儲存",prefs.Message);
    }
    private sealed class FailingSettings(ISettingsRepository inner):ISettingsRepository
    {
        public string FailKey="";
        public Task<Setting?> GetAsync(string key,CancellationToken cancellationToken=default)=>inner.GetAsync(key,cancellationToken);
        public Task SaveAsync(Setting setting,CancellationToken cancellationToken=default)=>setting.Key==FailKey?Task.FromException(new IOException("test write failure")):inner.SaveAsync(setting,cancellationToken);
    }
    private static IEnumerable<T> All<T>(DependencyObject node)where T:DependencyObject
    {
        if(node is T value)yield return value;
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)foreach(var child in All<T>(VisualTreeHelper.GetChild(node,i)))yield return child;
    }
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
        public bool ConfirmResult=true;public string LastPrompt="";
        public bool Confirm(string message){LastPrompt=message;return ConfirmResult;}
        public void Edit(AlarmTask? task,bool copy){}public void Export(string contents){}
        public Task<string?> OpenSettingsJsonAsync()=>Task.FromResult<string?>(null);
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

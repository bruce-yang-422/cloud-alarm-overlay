using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CloudAlarmOverlay.App.Styles;
using CloudAlarmOverlay.App.ViewModels;
using CloudAlarmOverlay.App.Views;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using CloudAlarmOverlay.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CloudAlarmOverlay.App.Tests;

public sealed class HomePinUiTests
{
    [Fact]
    public Task More_than_two_pins_scroll_without_stretching_left_dashboard_cards() => MilestoneOneTests.RunSta(async () =>
    {
        using var fixture=new Fixture();await fixture.Initialize();
        var tasks=fixture.Get<ITaskRepository>();var pins=fixture.Get<ITaskHomePinRepository>();
        await fixture.Get<ISettingsRepository>().SaveAsync(new Setting{Key=HomePinOptions.SettingKey,Value="5"});
        for(var i=0;i<5;i++)await tasks.SaveLocalAsync(Sample("scroll"+i));
        var main=fixture.Get<MainViewModel>();await main.InitializeAsync();
        var window=fixture.Get<MainWindow>();window.Width=1100;window.Height=780;
        try
        {
            window.Show();
            var next=(Border)window.FindName("NextReminderCard");var summary=(Border)window.FindName("TodaySummaryCard");
            var panel=(Border)window.FindName("PinnedCountdownCard");var scroll=(ScrollViewer)window.FindName("PinnedCounterScroll");
            foreach(var width in new[]{1100,1440})
            {
                window.Width=width;
                double nextHeight=0,summaryHeight=0;
                foreach(var count in new[]{2,3,4,5,1,0,2})
                {
                    for(var i=0;i<5;i++)await pins.SetPinnedAsync("scroll"+i,i<count);
                    await main.RefreshCommand.ExecuteAsync(null);await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);window.UpdateLayout();
                    if(nextHeight==0){nextHeight=next.ActualHeight;summaryHeight=summary.ActualHeight;}
                    Assert.Equal(nextHeight,next.ActualHeight,2);Assert.Equal(summaryHeight,summary.ActualHeight,2);
                    Assert.Equal(summary.TranslatePoint(new Point(0,summary.ActualHeight),window).Y,panel.TranslatePoint(new Point(0,panel.ActualHeight),window).Y,2);
                    Assert.Equal(count>2,scroll.ScrollableHeight>1);
                    Assert.Equal(count>2?Visibility.Visible:Visibility.Collapsed,scroll.ComputedVerticalScrollBarVisibility);
                    var cards=Descendants(panel).OfType<Border>().Where(b=>b.Name=="PinnedCounterItem").ToArray();
                    if(count>=2)Assert.Equal(scroll.ViewportHeight/2,cards[0].ActualHeight+6,2);
                    if(count==5)
                    {
                        scroll.ScrollToTop();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                        Capture(window,"pinned-scroll-top-"+width);
                        scroll.ScrollToEnd();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);window.UpdateLayout();
                        var bottom=cards[^1].TranslatePoint(new Point(0,cards[^1].ActualHeight),scroll).Y;
                        Assert.InRange(bottom,scroll.ViewportHeight-4,scroll.ViewportHeight+1);
                        Assert.True(scroll.VerticalOffset>0);
                        Capture(window,"pinned-scroll-bottom-"+width);
                    }
                }
            }
        }
        finally{window.ForceClose();}
    });

    [Fact]
    public Task Task_pin_feedback_animates_after_save_and_survives_row_selection() => MilestoneOneTests.RunSta(async () =>
    {
        using var fixture=new Fixture();await fixture.Initialize();
        var repository=fixture.Get<ITaskRepository>();
        foreach(var id in new[]{"first","second","third"})await repository.SaveLocalAsync(Sample(id));
        var main=fixture.Get<MainViewModel>();await main.InitializeAsync();main.OpenTasksCommand.Execute(null);
        var window=fixture.Get<MainWindow>();window.Width=1200;window.Height=900;
        try
        {
            window.Show();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);window.UpdateLayout();
            var grid=(DataGrid)window.FindName("TaskGrid");
            var row=main.Tasks.Single(t=>t.Task.Id=="first");grid.SelectedItem=row;
            Button FindButton(TaskRow item)=>Descendants(grid).OfType<Button>().Single(b=>ReferenceEquals(b.DataContext,item)&&b.Command==main.ToggleTaskPinCommand);
            var button=FindButton(row);
            Assert.Equal("釘選首頁",button.ToolTip);
            Assert.InRange(button.ActualWidth,24,30);
            await main.ToggleTaskPinCommand.ExecuteAsync(row);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Same(row,main.Tasks.Single(t=>t.Task.Id=="first"));Assert.Same(button,FindButton(row));
            Assert.Equal("取消釘選",button.ToolTip);
            var glyph=(TextBlock)button.Template.FindName("PinGlyph",button);
            Assert.True(((RotateTransform)glyph.RenderTransform).HasAnimatedProperties);
            await Task.Delay(320);
            Assert.Equal(-45,((RotateTransform)glyph.RenderTransform).Angle,1);
            foreach(var style in Enum.GetValues<ThemeColorStyle>())foreach(var dark in new[]{false,true})
            {
                AdaptiveBrushExtension.Apply(dark,style);await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.All(Descendants(grid).OfType<DataGridCell>().Where(c=>c.IsSelected),c=>Assert.Equal(Colors.Transparent,((SolidColorBrush)c.Background).Color));
                var unpinned=FindButton(main.Tasks.Single(t=>t.Task.Id=="second"));
                Assert.NotEqual(((SolidColorBrush)button.Background).Color,((SolidColorBrush)unpinned.Background).Color);
                Assert.Equal(((SolidColorBrush)button.Foreground).Color,((SolidColorBrush)((TextBlock)button.Template.FindName("PinGlyph",button)).Foreground).Color);
                if(style==ThemeColorStyle.Lavender)Capture(window,"task-pin-feedback-"+(dark?"dark":"light"));
            }
            button=FindButton(row);glyph=(TextBlock)button.Template.FindName("PinGlyph",button);
            await main.RefreshCommand.ExecuteAsync(null);await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Same(button,FindButton(row));Assert.Equal("取消釘選",button.ToolTip);
            await main.ToggleTaskPinCommand.ExecuteAsync(main.Tasks.Single(t=>t.Task.Id=="second"));
            var third=main.Tasks.Single(t=>t.Task.Id=="third");
            await main.ToggleTaskPinCommand.ExecuteAsync(third);await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.False(third.IsPinned);Assert.Contains("最多釘選 2",main.Status);
            Assert.Equal("釘選首頁",FindButton(third).ToolTip);
            await main.ToggleTaskPinCommand.ExecuteAsync(row);await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);await Task.Delay(260);
            Assert.Equal("釘選首頁",button.ToolTip);Assert.Equal(0,((RotateTransform)glyph.RenderTransform).Angle,1);
        }
        finally{window.ForceClose();AdaptiveBrushExtension.Apply(false,ThemeColorStyle.Default);}
    });

    [Fact]
    public Task Settings_offer_two_to_five_shared_cards_and_render_all_five() => MilestoneOneTests.RunSta(async () =>
    {
        using var fixture=new Fixture();await fixture.Initialize();
        var main=fixture.Get<MainViewModel>();await main.InitializeAsync();
        var preferences=main.Preferences;
        Assert.Equal(2,preferences.HomePinLimit);Assert.Equal(new[]{2,3,4,5},preferences.HomePinLimitChoices);
        preferences.HomePinLimit=5;await preferences.SaveHomePinLimitCommand.ExecuteAsync(null);
        var tasks=fixture.Get<ITaskRepository>();var pins=fixture.Get<ITaskHomePinRepository>();
        for(var i=0;i<3;i++){var task=Sample("task"+i);await tasks.SaveLocalAsync(task);await pins.SetPinnedAsync(task.Id,true);}
        for(var i=0;i<2;i++)await fixture.Get<ICountdownRepository>().SaveAsync(new CountdownItem{Id="count"+i,Title="倒數 "+i,TargetAt=DateTime.Today.AddDays(i+1),CreatedAt=DateTime.Today,IsPinned=true});
        await main.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(5,main.HomePins.Count);Assert.Contains("5 / 5",main.HomePinSummary);Assert.Contains("尚餘 0",main.HomePinSummary);
        preferences.HomePinLimit=2;await preferences.SaveHomePinLimitCommand.ExecuteAsync(null);
        Assert.Contains("請先取消",preferences.HomePinMessage);Assert.Equal(5,await pins.GetLimitAsync());
        await preferences.LoadAsync();Assert.Equal(5,preferences.HomePinLimit);
        var window=fixture.Get<MainWindow>();window.Width=1100;window.Height=900;
        try
        {
            window.Show();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);window.UpdateLayout();
            var cards=Descendants(window).OfType<Border>().Where(b=>b.Name=="PinnedCounterItem").ToArray();
            Assert.Equal(5,cards.Length);
            Assert.All(cards,c=>{Assert.True(c.ActualHeight>=140);Assert.Equal(cards[0].ActualHeight,c.ActualHeight,2);});
            Capture(window,"shared-pins-five");
            main.PageIndex=4;await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var view=Descendants(window).OfType<PreferencesView>().Single();
            var tabs=(TabControl)view.FindName("SettingsTabs");
            tabs.SelectedItem=tabs.Items.OfType<TabItem>().Single(t=>(string)t.Header=="首頁釘選");
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);window.UpdateLayout();
            var selector=(ComboBox)view.FindName("HomePinLimitSelector");
            Assert.True(selector.IsVisible);Assert.Equal(5,selector.SelectedItem);
            Capture(window,"shared-pin-settings");
            await main.UnpinHomeCommand.ExecuteAsync(main.HomePins[0]);
            Assert.Contains("4 / 5",main.HomePinSummary);Assert.Contains("尚餘 1",main.HomePinSummary);
        }
        finally{window.ForceClose();}
    });

    [Fact]
    public Task Mixed_home_cards_share_limit_render_and_survive_reopening() => MilestoneOneTests.RunSta(async () =>
    {
        using var fixture=new Fixture();await fixture.Initialize();
        var tasks=fixture.Get<ITaskRepository>();
        var task=Sample("shared-task");await tasks.SaveLocalAsync(task);await tasks.SaveLocalAsync(Sample("other"));
        var main=fixture.Get<MainViewModel>();await main.InitializeAsync();
        var vm=main.Countdowns;vm.Title="旅行出發";vm.Category="旅行";vm.PinOnHome=true;
        await vm.SaveCommand.ExecuteAsync(null);
        await main.ToggleTaskPinCommand.ExecuteAsync(main.Tasks.Single(t=>t.Task.Id==task.Id));
        Assert.Equal(2,main.HomePins.Count);Assert.Contains("共用",main.HomePinSummary);
        Assert.Single(main.HomePins.OfType<PinnedTaskRow>());
        Assert.True(main.Tasks.Single(t=>t.Task.Id==task.Id).IsPinned);
        await main.ToggleTaskPinCommand.ExecuteAsync(main.Tasks.Single(t=>t.Task.Id=="other"));
        Assert.Contains("最多釘選 2",main.Status);
        vm.Title="額外倒數";vm.PinOnHome=true;await vm.SaveCommand.ExecuteAsync(null);
        Assert.Contains("最多釘選 2",vm.Message);Assert.Single(vm.Items);

        var reopened=ActivatorUtilities.CreateInstance<MainViewModel>(fixture.Services);
        await reopened.InitializeAsync();Assert.Equal(2,reopened.HomePins.Count);
        var window=fixture.Get<MainWindow>();window.Width=1100;window.Height=780;
        try
        {
            window.Show();
            foreach(var dark in new[]{false,true})
            {
                AdaptiveBrushExtension.Apply(dark,ThemeColorStyle.Default);main.UpdateCountdown();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);window.UpdateLayout();
                var cards=Descendants(window).OfType<Border>().Where(b=>b.Name=="PinnedCounterItem").ToArray();
                Assert.Equal(2,cards.Length);Assert.Equal(cards[0].ActualHeight,cards[1].ActualHeight,2);
                Assert.Contains(Descendants(cards.Single(c=>c.DataContext is PinnedTaskRow)).OfType<TextBlock>(),b=>b.Text=="任務 · 本機");
                Capture(window,"shared-pins-"+(dark?"dark":"light"));
            }
            main.OpenTasksCommand.Execute(null);await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);window.UpdateLayout();
            var grid=(DataGrid)window.FindName("TaskGrid");
            var pinButton=Descendants(grid).OfType<Button>().Single(b=>b.DataContext is TaskRow row && row.Task.Id==task.Id && b.Command==main.ToggleTaskPinCommand);
            Assert.Same(main.ToggleTaskPinCommand,pinButton.Command);
            Assert.Equal("取消釘選",pinButton.ToolTip);
            var bounds=pinButton.TransformToAncestor(grid).TransformBounds(new Rect(pinButton.RenderSize));
            Assert.True(bounds.Left>=0 && bounds.Right<=grid.ActualWidth);
            Capture(window,"shared-pins-task-list");
            await main.UnpinHomeCommand.ExecuteAsync(main.HomePins.Single(row=>row is PinnedTaskRow));
            Assert.Single(main.HomePins);Assert.Equal(task,await tasks.GetByIdAsync(task.Id));
            await main.ToggleTaskPinCommand.ExecuteAsync(main.Tasks.Single(t=>t.Task.Id==task.Id));
            await tasks.DeleteLocalAsync(task.Id);await main.RefreshCommand.ExecuteAsync(null);
            Assert.Single(main.HomePins);Assert.Empty(main.HomePins.OfType<PinnedTaskRow>());
            var countdownPin=Assert.Single(main.HomePins);
            await main.UnpinHomeCommand.ExecuteAsync(countdownPin);
            await main.UnpinHomeCommand.ExecuteAsync(countdownPin);
            Assert.Empty(main.HomePins);Assert.Single(vm.Items);Assert.False(vm.Items.Single().Item.IsPinned);
        }
        finally{window.ForceClose();AdaptiveBrushExtension.Apply(false,ThemeColorStyle.Default);}
    });

    [Fact]
    public Task Repeating_task_uses_scheduler_and_reflects_reschedule_disable_and_cloud_updates() => MilestoneOneTests.RunSta(async () =>
    {
        using var fixture=new Fixture();await fixture.Initialize();
        var tasks=fixture.Get<ITaskRepository>();var pins=fixture.Get<ITaskHomePinRepository>();
        var now=DateTime.Now;
        var task=Sample("daily") with {ScheduledAt=now.AddDays(-1).AddMinutes(-1),Recurrence="Daily"};
        await tasks.SaveLocalAsync(task);await pins.SetPinnedAsync(task.Id,true);
        var main=fixture.Get<MainViewModel>();await main.InitializeAsync();
        var row=Assert.IsType<PinnedTaskRow>(Assert.Single(main.HomePins));
        Assert.Equal(await fixture.Get<ITaskSchedulingService>().GetNextOccurrenceAsync(task,now),row.NextAt);
        Assert.True(row.NextAt>now);Assert.StartsWith("剩餘",row.HomeSummary);
        var next=await fixture.Get<ITaskSchedulingService>().GetNextOccurrenceAsync(task,row.NextAt!.Value.AddSeconds(1));
        Assert.True(next>row.NextAt);Assert.True(row.NeedsReschedule(row.NextAt.Value));
        await tasks.SaveLocalAsync(task with {ScheduledAt=now.AddDays(4)});await main.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(now.AddDays(4),Assert.IsType<PinnedTaskRow>(Assert.Single(main.HomePins)).NextAt);
        await tasks.SaveLocalAsync(task with {Enabled=false});await main.RefreshCommand.ExecuteAsync(null);
        Assert.Equal("任務已停用",Assert.Single(main.HomePins).HomeSummary);Assert.False(Assert.Single(main.HomePins).ShowProgress);
        var cloud=Sample("cloud") with {Source=TaskSources.SheetA,ExternalId="sample"};
        await tasks.ReplaceCloudCacheAsync(TaskSources.SheetA,[cloud]);await pins.SetPinnedAsync(cloud.Id,true);
        await tasks.ReplaceCloudCacheAsync(TaskSources.SheetA,[cloud with {Title="更新後的雲端任務",ScheduledAt=cloud.ScheduledAt.AddDays(1)}]);
        await main.RefreshCommand.ExecuteAsync(null);
        Assert.Contains(main.HomePins,r=>r.Title=="更新後的雲端任務" && r.DisplayDate==cloud.ScheduledAt.AddDays(1));
    });

    [Fact]
    public Task Repeating_pin_rolls_forward_on_the_home_clock_tick() => MilestoneOneTests.RunSta(async () =>
    {
        using var fixture=new Fixture();await fixture.Initialize();
        var target=DateTime.Now.AddSeconds(2);
        var task=Sample("tick") with {ScheduledAt=target,Recurrence="Daily"};
        await fixture.Get<ITaskRepository>().SaveLocalAsync(task);
        await fixture.Get<ITaskHomePinRepository>().SetPinnedAsync(task.Id,true);
        var main=fixture.Get<MainViewModel>();await main.InitializeAsync();
        Assert.Equal(target,Assert.IsType<PinnedTaskRow>(Assert.Single(main.HomePins)).NextAt);
        await Task.Delay(TimeSpan.FromSeconds(2.1));
        main.UpdateCountdown();
        if(main.RefreshCommand.ExecutionTask is {} refresh)await refresh;
        var updated=Assert.IsType<PinnedTaskRow>(Assert.Single(main.HomePins));
        Assert.Equal(target.AddDays(1),updated.NextAt);Assert.False(updated.IsExpired);
    });

    [Fact]
    public void One_time_pin_shows_due_or_acknowledged_without_creating_a_countdown_reminder()
    {
        var now=new DateTime(2026,9,18,12,0,0);var task=Sample("once") with {ScheduledAt=now.AddMinutes(-5)};
        var overdue=new PinnedTaskRow(task,null);overdue.Update(now);
        Assert.Equal("已到期",overdue.HomeSummary);Assert.True(overdue.IsExpired);
        var completed=new PinnedTaskRow(task,null,now);completed.Update(now);
        Assert.Equal("已確認完成",completed.HomeSummary);Assert.False(completed.IsExpired);
        Assert.Equal(-1,completed.Item.ReminderDays);
    }

    private static AlarmTask Sample(string id)=>new() {Id=id,Title="追蹤工作截止日 · "+id,ScheduledAt=DateTime.Today.AddDays(2).AddHours(15),CreatedAt=DateTime.Today,UpdatedAt=DateTime.Today};
    private static IEnumerable<DependencyObject> Descendants(DependencyObject node)
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)
        {var child=VisualTreeHelper.GetChild(node,i);yield return child;foreach(var nested in Descendants(child))yield return nested;}
    }
    private static void Capture(Window window,string name)
    {
        var directory=Environment.GetEnvironmentVariable("CLOUD_ALARM_SCREENSHOT_DIR");if(directory is null)return;
        Directory.CreateDirectory(directory);var content=(FrameworkElement)window.Content;
        var bitmap=new RenderTargetBitmap((int)content.ActualWidth,(int)content.ActualHeight,96,96,PixelFormats.Pbgra32);
        bitmap.Render(content);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file=File.Create(Path.Combine(directory,name+".png"));encoder.Save(file);
    }
    private sealed class Fixture:IDisposable,IAppPaths
    {
        public string DataDirectory {get;}=Path.Combine(Path.GetTempPath(),"CloudAlarmHomePinUi",Guid.NewGuid().ToString("N"));
        public string DatabasePath=>Path.Combine(DataDirectory,"test.db");
        private readonly IHost host;
        public IServiceProvider Services=>host.Services;
        public Fixture()=>host=new HostBuilder().ConfigureServices(s=>{CompositionRoot.ConfigureServices(s);s.AddSingleton<IAppPaths>(this);}).Build();
        public T Get<T>() where T:notnull=>Services.GetRequiredService<T>();
        public Task Initialize()=>Get<IDatabaseInitializer>().InitializeAsync();
        public void Dispose(){host.Dispose();if(Directory.Exists(DataDirectory))Directory.Delete(DataDirectory,true);}
    }
}

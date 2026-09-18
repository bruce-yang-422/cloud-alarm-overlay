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

public sealed class CountdownUiTests
{
    [Fact]
    public Task Five_categories_slide_filter_and_count_without_losing_existing_values() => MilestoneOneTests.RunSta(async () =>
    {
        using var fixture = new Fixture(); await fixture.Initialize();
        var vm = fixture.Get<CountdownsViewModel>();
        Assert.Equal(new[] { "工作", "生活", "節日", "旅行", "其他" }, vm.Categories);
        foreach (var category in vm.Categories)
        { vm.Title=category; vm.Category=category; vm.TargetDate=DateTime.Today.AddDays(1); await vm.SaveCommand.ExecuteAsync(null); }
        Assert.Equal(5, vm.TotalCount); Assert.Equal(1,vm.TravelCount); Assert.Equal(1,vm.OtherCount);
        vm.CategoryFilter="旅行"; Assert.Equal("旅行",Assert.Single(vm.VisibleItems).Item.Category);
        vm.EditCommand.Execute(vm.VisibleItems[0]);
        var editor=new CountdownEditorWindow(vm);
        try
        {
            editor.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var selector=Descendants(editor).OfType<CloudAlarmOverlay.App.Controls.SlidingSegmentedControl>().Single();
            Assert.Equal("旅行",selector.SelectedItem);
            selector.SelectedItem="其他"; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal("其他",vm.Category);
            var indicator=(Border)selector.Template.FindName("PART_Indicator",selector);
            Assert.True(indicator.IsVisible); Assert.True(indicator.HasAnimatedProperties);
            Assert.Equal(2,Descendants(editor).OfType<CloudAlarmOverlay.App.Controls.TimeInput>().Count());
            vm.Mode="倒數時間"; vm.TargetTime="09:00"; vm.Reminder="提前 1 天";
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var input=Descendants(editor).OfType<CloudAlarmOverlay.App.Controls.TimeInput>().First();
            var clockButton=Descendants(input).OfType<Button>().Single();
            _ = editor.Dispatcher.BeginInvoke(() =>
            {
                var picker=editor.OwnedWindows.OfType<TimePickerWindow>().Single();
                ((TextBox)picker.FindName("Hours")).Text="15"; ((TextBox)picker.FindName("Minutes")).Text="37";
                ((Button)picker.FindName("Confirm")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            });
            clockButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("15:37",vm.TargetTime);
            _ = editor.Dispatcher.BeginInvoke(() =>
            {
                var picker=editor.OwnedWindows.OfType<TimePickerWindow>().Single();
                ((TextBox)picker.FindName("Hours")).Text="18"; picker.Close();
            });
            clockButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("15:37",vm.TargetTime);

        }
        finally {editor.Close();}
    });

    [Fact]
    public Task Clock_picker_supports_mouse_keyboard_validation_cancel_and_six_themes() => MilestoneOneTests.RunSta(async () =>
    {
        Assert.Equal(0,TimePickerWindow.ValueAt(new Point(145,27),false));
        Assert.Equal(12,TimePickerWindow.ValueAt(new Point(145,70),false));
        Assert.Equal(15,TimePickerWindow.ValueAt(new Point(220,145),false));
        var angle=17*Math.PI*2/60;
        Assert.Equal(17,TimePickerWindow.ValueAt(new Point(145+118*Math.Sin(angle),145-118*Math.Cos(angle)),true));
        try
        {
            foreach(var style in new[]{ThemeColorStyle.Default,ThemeColorStyle.Pink,ThemeColorStyle.Bamboo})
            foreach(var dark in new[]{false,true})
            {
                AdaptiveBrushExtension.Apply(dark,style);
                var picker=new TimePickerWindow("15:30"); picker.Show();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Capture(picker,$"time-picker-{style}-{dark}");
                var dial=(Canvas)picker.FindName("Dial");
                dial.Children.OfType<Button>().Single(b=>(int)b.Tag==23).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                dial.Children.OfType<Button>().Single(b=>(int)b.Tag==55).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal("23",((TextBox)picker.FindName("Hours")).Text);
                Assert.Equal("55",((TextBox)picker.FindName("Minutes")).Text);
                picker.Close(); Assert.Equal("15:30",picker.SelectedTime);
            }
            var accepted=new TimePickerWindow("09:00");
            accepted.Loaded+=(_,_)=>accepted.Dispatcher.BeginInvoke(() =>
            {
                var hours=(TextBox)accepted.FindName("Hours"); var minutes=(TextBox)accepted.FindName("Minutes");
                hours.Text="24"; ((Button)accepted.FindName("Confirm")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(accepted.IsVisible); Assert.NotEmpty(((TextBlock)accepted.FindName("Error")).Text);
                hours.Text="0"; minutes.Text="07";
                ((Button)accepted.FindName("Confirm")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            });
            Assert.True(accepted.ShowDialog()); Assert.Equal("00:07",accepted.SelectedTime);
        }
        finally {AdaptiveBrushExtension.Apply(false);}
    });

    [Fact]
    public Task Home_shortcuts_open_task_editor_or_navigate_without_creating_events() => MilestoneOneTests.RunSta(async () =>
    {
        using var fixture = new Fixture(); await fixture.Initialize();
        var main = fixture.Get<MainViewModel>();
        var window = fixture.Get<MainWindow>();
        try
        {
            window.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var task = (Button)window.FindName("HomeNewTask");
            task.Command.Execute(null);
            Assert.Equal(1, fixture.Dialogs.EditCount);
            Assert.Equal(0, main.PageIndex);
            ((Button)window.FindName("HomeCountdown")).Command.Execute(null);
            Assert.Equal(6, main.PageIndex); Assert.True(main.NavigationItems.Single(n => n.PageIndex == 6).IsSelected);
            main.PageIndex = 0;
            ((Button)window.FindName("HomePomodoro")).Command.Execute(null);
            Assert.Equal(3, main.PageIndex); Assert.True(main.NavigationItems.Single(n => n.PageIndex == 3).IsSelected);
            Assert.Equal(1, fixture.Dialogs.EditCount);
            Assert.Empty(await fixture.Get<ICountdownRepository>().GetAllAsync());
            Assert.Empty(window.OwnedWindows.OfType<CountdownEditorWindow>());
        }
        finally { window.ForceClose(); }
    });

    [Fact]
    public Task Editor_preview_updates_without_saving_and_handles_invalid_drafts() => MilestoneOneTests.RunSta(async () =>
    {
        var clock = new CountdownClock(new(2026, 9, 18, 12, 0, 0));
        using var fixture = new Fixture(s => s.AddSingleton<TimeProvider>(clock));
        await fixture.Initialize();
        var vm = fixture.Get<CountdownsViewModel>();
        vm.TargetDate = new(2026, 9, 20); vm.Title = "專案上線日";
        var editor = new CountdownEditorWindow(vm);
        try
        {
            editor.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Contains("2 天", ((TextBlock)editor.FindName("PreviewValue")).Text);
            vm.Direction = "正數"; vm.TargetDate = new(2026, 9, 1);
            Assert.Contains("17 天", ((TextBlock)editor.FindName("PreviewValue")).Text);
            vm.Direction = "倒數"; vm.Repeat = "每年（農曆）"; vm.LunarMonth = 1; vm.LunarDate = 1;
            Assert.Contains("2027/02/06", ((TextBlock)editor.FindName("PreviewDate")).Text);
            vm.Repeat = "每月";
            foreach (var month in vm.Months) month.IsSelected = false;
            Assert.Contains("至少選擇", ((TextBlock)editor.FindName("PreviewDate")).Text);
            vm.Months[9].IsSelected = true;
            Assert.DoesNotContain("至少選擇", ((TextBlock)editor.FindName("PreviewDate")).Text);
            vm.Mode = "倒數時間"; vm.TargetTime = "25:00";
            Assert.Equal("—", ((TextBlock)editor.FindName("PreviewValue")).Text);
            vm.TargetTime = "09:00";
            Assert.NotEqual("—", ((TextBlock)editor.FindName("PreviewValue")).Text);
            editor.Width = 640; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var preview = (FrameworkElement)editor.FindName("PreviewPanel");
            Assert.True(preview.IsVisible);
            Assert.Equal(1, Grid.GetRow(preview));
            Assert.Empty(Descendants(editor).OfType<Expander>());
            Assert.Contains(Descendants(editor).OfType<TextBox>(), t => System.Windows.Automation.AutomationProperties.GetName(t) == "倒數備註" && t.IsVisible);
            editor.Width = 1040; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal(0, Grid.GetRow(preview));
            Assert.Equal(2, Grid.GetColumn(preview));
            Assert.True(((TextBox)editor.FindName("EventTitle")).ActualWidth >= 300);
            Assert.Empty(await fixture.Get<ICountdownRepository>().GetAllAsync());
        }
        finally { editor.Close(); }
    });

    [Fact]
    public Task Compact_grid_shows_six_events_and_combines_category_and_status_filters() => MilestoneOneTests.RunSta(async () =>
    {
        using var fixture = new Fixture(); await fixture.Initialize();
        var vm = fixture.Get<CountdownsViewModel>();
        var names = new[] { "日本旅行", "結婚紀念日", "專案上線", "生日", "馬拉松比賽", "年終獎金入帳" };
        for (var i = 0; i < 6; i++)
        {
            vm.Title = names[i]; vm.Category = vm.Categories[i % 3];
            vm.TargetDate = DateTime.Today.AddDays(i + 3);
            await vm.SaveCommand.ExecuteAsync(null);
        }
        var view = new CountdownsView { DataContext = vm };
        var window = new Window { Content = view, Width = 1180, Height = 880 };
        try
        {
            window.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            Assert.Equal(3, view.CardColumns);
            var cards = Descendants(view).OfType<Border>().Where(b => b.Name == "CountdownEventCard").ToArray();
            Assert.Equal(6, cards.Length);
            var scroll = Descendants(view).OfType<ScrollViewer>().First();
            Assert.All(cards, card => Assert.True(card.TransformToAncestor(scroll).Transform(new Point()).Y + card.ActualHeight <= scroll.ViewportHeight));
            Capture(window, "countdown-compact-grid");
            vm.CategoryFilter = "工作"; Assert.Equal(2, vm.VisibleItems.Count);
            await vm.ToggleCompleteCommand.ExecuteAsync(vm.VisibleItems[0]);
            vm.Filter = "進行中"; Assert.Single(vm.VisibleItems);
            vm.ViewMode = "資料表"; Assert.Single(vm.VisibleItems);
            vm.Filter = "已完成"; Assert.Single(vm.VisibleItems);
            vm.CategoryFilter = "生活"; Assert.Empty(vm.VisibleItems);
            vm.Filter = "全部"; Assert.Equal(2, vm.VisibleItems.Count);
            vm.ViewMode = "卡片"; window.Width = 800; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); Assert.Equal(2, view.CardColumns);
            window.Width = 640; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); Assert.Equal(1, view.CardColumns);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Switching_card_and_table_preserves_filters_drafts_order_and_row_actions() => MilestoneOneTests.RunSta(async () =>
    {
        using var fixture=new Fixture();await fixture.Initialize();var vm=fixture.Get<CountdownsViewModel>();
        for(var i=1;i<=3;i++) { vm.Title="事件"+i;vm.TargetDate=DateTime.Today.AddDays(i);vm.PinOnHome=i==1;await vm.SaveCommand.ExecuteAsync(null); }
        await vm.ToggleTopCommand.ExecuteAsync(vm.Items[2]);
        vm.EditCommand.Execute(vm.Items[0]);vm.Title="未儲存草稿";
        var view=new CountdownsView { DataContext=vm };var window=new Window { Content=view,Width=940,Height=750 };
        try
        {
            window.Show();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);window.UpdateLayout();
            var cards=(ItemsControl)view.FindName("CountdownCards");var table=(DataGrid)view.FindName("CountdownTable");
            Assert.True(vm.IsCardView);Assert.True(cards.IsVisible);Assert.False(table.IsVisible);Assert.Equal(2,view.CardColumns);
            var order=vm.VisibleItems.Select(i=>i.Item.Id).ToArray();
            vm.ViewMode="資料表";await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);window.UpdateLayout();
            Assert.False(cards.IsVisible);Assert.True(table.IsVisible);Assert.Same(vm.VisibleItems,table.ItemsSource);
            Assert.Equal(order,table.Items.Cast<CountdownRow>().Select(i=>i.Item.Id));Assert.Equal("未儲存草稿",vm.Title);
            vm.Filter="已釘選";await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);window.UpdateLayout();
            var row=Assert.Single(table.Items.Cast<CountdownRow>());Assert.Equal("事件1",row.Title);
            table.BringIntoView();table.ScrollIntoView(row);await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);window.UpdateLayout();
            var pin=Descendants(table).OfType<Button>().Single(b=>b.Command==vm.TogglePinCommand);
            Assert.Same(row,pin.CommandParameter);await vm.TogglePinCommand.ExecuteAsync(pin.CommandParameter);
            Assert.Empty(table.Items);Assert.True(vm.IsFilteredEmpty);Assert.Equal("未儲存草稿",vm.Title);
            vm.ViewMode="卡片";Assert.True(vm.IsCardView);Assert.Equal("已釘選",vm.Filter);
            vm.Filter="全部";window.Width=730;await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);window.UpdateLayout();
            Assert.Equal(1,view.CardColumns);Assert.Equal(order,vm.VisibleItems.Select(i=>i.Item.Id));
        }
        finally {window.Close();}
    });

    [Fact]
    public void Home_countdown_shows_seconds_and_elapsed_percentage_without_negative_values()
    {
        var row = new CountdownRow(new CountdownItem { Title="旅行", Mode="Time", TargetAt=new(2026,9,20,14,0,0), CreatedAt=new(2026,9,18,14,0,0) });
        row.Update(new(2026,9,19,14,0,0));
        Assert.Equal("1",row.HomeDays);Assert.Equal("00",row.HomeHours);Assert.Equal("00",row.HomeMinutes);Assert.Equal("00",row.HomeSeconds);
        Assert.Equal("已經過 50%",row.HomeElapsedLabel);Assert.True(row.ShowHomeCountdown);Assert.True(row.HasHomeClock);
        row.Update(new DateTime(2026,9,20,13,59,59).AddMilliseconds(500));
        Assert.Equal("01",row.HomeSeconds);Assert.Equal("0",row.HomeDays);
        row.Update(new(2026,9,20,14,0,0));Assert.False(row.ShowHomeCountdown);Assert.Equal("已經過 100%",row.HomeElapsedLabel);
        Assert.DoesNotContain("-",row.HomeRemainingDetail);
        var day=new CountdownRow(new CountdownItem { TargetAt=new(2026,9,20),CreatedAt=new(2026,9,18) });
        day.Update(new(2026,9,20,23,59,59));Assert.Equal("0",day.HomeDays);Assert.True(day.ShowHomeCountdown);Assert.False(day.HasHomeClock);
        var completed=new CountdownRow(row.Item with { CompletedAt=new(2026,9,19) });
        completed.Update(new(2026,9,19));Assert.False(completed.ShowHomeCountdown);Assert.Equal("事件已標記完成",completed.HomeRemainingDetail);
    }

    [Fact]
    public Task Event_form_custom_time_completion_and_year_statistics_roundtrip() => MilestoneOneTests.RunSta(async () =>
    {
        using var fixture=new Fixture();await fixture.Initialize();
        var vm=fixture.Get<CountdownsViewModel>();
        vm.Title="生日";vm.Category="生活";vm.Repeat="每年";vm.TargetDate=DateTime.Today.AddDays(30);
        vm.Reminder="提前 3 天";vm.ReminderTime="18:45";vm.Notes="一起吃蛋糕";
        await vm.SaveCommand.ExecuteAsync(null);
        var row=Assert.Single(vm.Items);
        Assert.Equal(1125,row.Item.ReminderMinutes);Assert.Equal(3,row.Item.ReminderDays);
        Assert.Equal("Yearly",row.Item.Repeat);Assert.Equal("一起吃蛋糕",row.Item.Notes);
        Assert.Equal(1,vm.ActiveCount);Assert.Equal(1,vm.LifeCount);Assert.Equal(0,vm.WorkCount);
        await vm.ToggleCompleteCommand.ExecuteAsync(row);
        Assert.Equal(1,vm.CompletedCount);Assert.Equal(0,vm.ActiveCount);Assert.Equal(0,vm.ExpiredCount);
        Assert.Null(vm.Items[0].Item.NextReminder(DateTime.Now));Assert.True(vm.IsTimelineEmpty);
        vm.Filter="已完成";Assert.Single(vm.VisibleItems);
        await vm.ToggleCompleteCommand.ExecuteAsync(vm.Items[0]);Assert.Equal(1,vm.ActiveCount);Assert.True(vm.IsFilteredEmpty);
        vm.EditCommand.Execute(vm.Items[0]);Assert.Equal("18:45",vm.ReminderTime);Assert.Equal("生活",vm.Category);
        vm.ReminderTime="25:10";await vm.SaveCommand.ExecuteAsync(null);Assert.Contains("HH:mm",vm.Message);
        vm.ReminderTime="18:45";vm.Reminder="當天";vm.Mode="倒數時間";vm.TargetTime="10:00";
        await vm.SaveCommand.ExecuteAsync(null);Assert.Contains("不可晚於",vm.Message);
        vm.SelectedYear=2028;vm.Update(new(2028,12,31));Assert.Equal(100,vm.YearPercent);Assert.Contains("366 / 366",vm.YearProgressLabel);
        vm.Update(new(2027,12,31));Assert.Equal(0,vm.YearPercent);
        vm.SelectedYear=2030;Assert.Equal(0,vm.TotalCount);Assert.Equal(0,vm.WorkCount+vm.LifeCount+vm.HolidayCount);
    });

    [Fact]
    public async Task Scheduler_delivers_custom_countdown_once_and_skips_disabled_completed_and_deleted()
    {
        var clock=new CountdownClock(new(2026,9,18,14,34,50));var alarms=new CapturedAlarms();
        using var fixture=new Fixture(s=>{s.AddSingleton<TimeProvider>(clock);s.AddSingleton<IAlarmService>(alarms);});
        await fixture.Initialize();await fixture.Get<IDeviceIdentityService>().SetInitialIdentityAsync("TEST","測試");
        var repo=fixture.Get<ICountdownRepository>();var settings=fixture.Get<ISettingsRepository>();
        var sample=new CountdownItem { Id="remind",Title="生日",TargetAt=new(2026,9,19),CreatedAt=new(2026,9,18),ReminderDays=1,ReminderMinutes=875 };
        await repo.SaveAsync(sample);
        await repo.SaveAsync(sample with { Id="disabled",ReminderDays=-1 });
        await repo.SaveAsync(sample with { Id="completed",CompletedAt=clock.Now });
        await repo.SaveAsync(sample with { Id="deleted" });await repo.DeleteAsync("deleted");
        var worker=fixture.Get<IEnumerable<IHostedService>>().OfType<CloudAlarmOverlay.BackgroundServices.AlarmWorker>().Single();
        await worker.StartAsync(default);
        try
        {
            async Task WaitForCheckpoint(DateTime at)
            {
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));
                while ((await settings.GetAsync("AlarmCheckpoint"))?.Value != at.ToString("O",System.Globalization.CultureInfo.InvariantCulture))
                    await Task.Delay(10,timeout.Token);
            }
            await WaitForCheckpoint(clock.Now);
            clock.Now=new(2026,9,18,14,35,0);fixture.Get<ChangeSignal>().Notify();
            await WaitForCheckpoint(clock.Now);
            var task=Assert.Single(alarms.Items);Assert.Equal("countdown:remind",task.Id);Assert.Equal(clock.Now,task.ScheduledAt);
            clock.Now=clock.Now.AddSeconds(1);fixture.Get<ChangeSignal>().Notify();await WaitForCheckpoint(clock.Now);
            Assert.Single(alarms.Items);
        }
        finally {await worker.StopAsync(default);}
    }

    private sealed class CountdownClock(DateTime now) : TimeProvider
    {
        public DateTime Now=now;
        public override DateTimeOffset GetUtcNow()=>new(DateTime.SpecifyKind(Now,DateTimeKind.Utc));
        public override TimeZoneInfo LocalTimeZone=>TimeZoneInfo.Utc;
    }
    private sealed class CapturedAlarms : IAlarmService
    {
        public System.Collections.Concurrent.ConcurrentQueue<AlarmTask> Items {get;}=new();
        public Task EnqueueAsync(AlarmTask task,CancellationToken cancellationToken=default){Items.Enqueue(task);return Task.CompletedTask;}
    }

    [Fact]
    public Task List_top_is_independent_of_home_pins_and_survives_editing() => MilestoneOneTests.RunSta(async()=>
    {
        using var fixture=new Fixture();await fixture.Initialize();
        var vm=fixture.Get<CountdownsViewModel>();
        for(var i=1;i<=3;i++)
        {
            vm.Title="倒數"+i;vm.TargetDate=DateTime.Today.AddDays(i);vm.PinOnHome=i<=2;
            await vm.SaveCommand.ExecuteAsync(null);
        }
        var third=vm.Items.Single(i=>i.Title=="倒數3");
        await vm.TogglePinCommand.ExecuteAsync(third);
        Assert.Contains("最多釘選 2",vm.Message);Assert.Equal(2,vm.Pinned.Count);
        vm.EditCommand.Execute(third);
        await vm.ToggleTopCommand.ExecuteAsync(third);
        Assert.Equal(third.Item.Id,vm.VisibleItems[0].Item.Id);
        Assert.False(vm.VisibleItems[0].Item.IsPinned);
        vm.Title="編輯後仍置頂";await vm.SaveCommand.ExecuteAsync(null);
        await vm.LoadAsync();Assert.True(vm.VisibleItems[0].Item.IsTop);Assert.Equal("編輯後仍置頂",vm.VisibleItems[0].Title);
        Assert.Equal("倒數1",vm.Timeline[0].Title);Assert.Equal("倒數1",vm.Pinned[0].Title);
        await vm.ToggleTopCommand.ExecuteAsync(vm.VisibleItems[0]);Assert.Equal("倒數1",vm.VisibleItems[0].Title);
        vm.Title="第三項釘選";vm.PinOnHome=true;await vm.SaveCommand.ExecuteAsync(null);
        Assert.Contains("最多釘選 2",vm.Message);Assert.Equal(3,vm.Items.Count);
        await vm.TogglePinCommand.ExecuteAsync(vm.Pinned[0]);await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(4,vm.Items.Count);Assert.Equal(2,vm.Pinned.Count);
    });

    [Fact]
    public Task Dashboard_counts_filters_and_charts_follow_time_boundaries() => MilestoneOneTests.RunSta(async () =>
    {
        using var fixture=new Fixture(); await fixture.Initialize();
        var now=new DateTime(2026,9,18,12,0,0);
        var repo=fixture.Get<ICountdownRepository>();
        var rows=new[]
        {
            new CountdownItem { Id="today",Title="今天",Mode="Days",TargetAt=now.Date,CreatedAt=now.AddDays(-2),IsPinned=true },
            new CountdownItem { Id="expired-time",Title="剛到期",Mode="Time",TargetAt=now,CreatedAt=now.AddDays(-2) },
            new CountdownItem { Id="expired-day",Title="昨天",Mode="Days",TargetAt=now.Date.AddDays(-1),CreatedAt=now.AddDays(-3) },
            new CountdownItem { Id="week",Title="七天邊界",Mode="Time",TargetAt=now.AddDays(7),CreatedAt=now },
            new CountdownItem { Id="later",Title="八天後",Mode="Days",TargetAt=now.Date.AddDays(8),CreatedAt=now },
            new CountdownItem { Id="halfway",Title="時間過一半",Mode="Time",TargetAt=now.AddDays(2),CreatedAt=now.AddDays(-2),IsPinned=true },
            new CountdownItem { Id="ten",Title="十天後",Mode="Time",TargetAt=now.AddDays(10),CreatedAt=now },
            new CountdownItem { Id="twenty",Title="二十天後",Mode="Time",TargetAt=now.AddDays(20),CreatedAt=now }
        };
        foreach(var row in rows)await repo.SaveAsync(row);
        var vm=fixture.Get<CountdownsViewModel>(); await vm.LoadAsync(); vm.Update(now);
        Assert.Equal(8,vm.TotalCount); Assert.Equal(3,vm.DueSoonCount); Assert.Equal(2,vm.ExpiredCount); Assert.Equal(2,vm.PinnedCount);
        Assert.Equal(5,vm.Timeline.Count); Assert.Equal(10,vm.ChartScale);
        Assert.DoesNotContain(vm.Timeline,row=>row.Item.Id=="twenty");
        Assert.Equal(50,vm.Items.Single(i=>i.Item.Id=="halfway").ElapsedPercent);
        Assert.Equal("就是今天",vm.Items.Single(i=>i.Item.Id=="today").StatusLabel);
        vm.Filter="7 天內"; Assert.Equal(3,vm.VisibleItems.Count);
        vm.Filter="已到期"; Assert.Equal(2,vm.VisibleItems.Count);
        vm.Filter="已釘選"; Assert.Equal(2,vm.VisibleItems.Count);
        vm.Update(now.AddDays(30));
        Assert.Equal(8,vm.ExpiredCount); Assert.Equal(0,vm.DueSoonCount); Assert.True(vm.IsTimelineEmpty); Assert.Equal(1,vm.ChartScale);
        vm.Filter="7 天內"; Assert.True(vm.IsFilteredEmpty);
        Assert.All(vm.Items,item=>Assert.Equal(100,item.ElapsedPercent));
        foreach(var row in rows)await repo.DeleteAsync(row.Id);
        await vm.LoadAsync(); Assert.Equal(0,vm.TotalCount); Assert.Equal(0,vm.PinnedCount); Assert.True(vm.IsTimelineEmpty);
    });

    [Fact]
    public Task Manage_items_updates_home_pins_preserves_drafts_and_survives_reopening() => MilestoneOneTests.RunSta(async () =>
    {
        using var fixture = new Fixture(); await fixture.Initialize();
        var vm = fixture.Get<CountdownsViewModel>();
        await vm.LoadAsync(); Assert.True(vm.IsEmpty); Assert.True(vm.IsPinnedEmpty);
        vm.Title = "  中秋節 🌙  "; vm.TargetDate = new(2026,9,25); vm.PinOnHome = true;
        await vm.SaveCommand.ExecuteAsync(null);
        var first = Assert.Single(vm.Items); Assert.Equal("中秋節 🌙", first.Title);
        Assert.Same(first, Assert.Single(vm.Pinned));
        vm.EditCommand.Execute(first); vm.Title = "尚未儲存的內容";
        await vm.LoadAsync(); Assert.Equal("尚未儲存的內容", vm.Title);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal("尚未儲存的內容", Assert.Single(vm.Pinned).Title);
        vm.Title = "下午出發"; vm.Mode = "倒數時間"; vm.TargetDate = new(2026,9,20); vm.TargetTime = "15:30"; vm.PinOnHome = true;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.Pinned.Count); Assert.Equal("下午出發", vm.Pinned[0].Title);
        vm.Update(new(2026,9,20,15,29,59)); Assert.Equal("1 分鐘", vm.Pinned[0].Remaining);
        vm.Update(new(2026,9,26)); Assert.All(vm.Pinned, row => Assert.Equal("已到期",row.Remaining));
        await vm.TogglePinCommand.ExecuteAsync(vm.Pinned[0]);
        Assert.Single(vm.Pinned); Assert.Equal(2, vm.Items.Count);
        var reopened = new CountdownsViewModel(fixture.Get<ICountdownRepository>(),TimeProvider.System,fixture.Dialogs,fixture.Get<ChangeSignal>());
        await reopened.LoadAsync(); Assert.Equal(2, reopened.Items.Count); Assert.Single(reopened.Pinned);
        fixture.Dialogs.AllowDelete = false;
        await reopened.DeleteCommand.ExecuteAsync(reopened.Pinned[0]); Assert.Single(reopened.Pinned);
        fixture.Dialogs.AllowDelete = true;
        await reopened.DeleteCommand.ExecuteAsync(reopened.Pinned[0]); Assert.True(reopened.IsPinnedEmpty); Assert.Single(reopened.Items);
        reopened.Title = "無效時間"; reopened.Mode = "倒數時間"; reopened.TargetTime = "25:00";
        await reopened.SaveCommand.ExecuteAsync(null); Assert.Contains("HH:mm",reopened.Message); Assert.Single(reopened.Items);
        reopened.TargetTime = "09:30"; reopened.TargetDate = null;
        await reopened.SaveCommand.ExecuteAsync(null); Assert.Contains("日期",reopened.Message); Assert.Single(reopened.Items);
    });

    [Fact]
    public Task Home_layout_navigation_and_countdown_views_render_in_all_six_palettes() => MilestoneOneTests.RunSta(async () =>
    {
        using var fixture = new Fixture(); await fixture.Initialize();
        var main = fixture.Get<MainViewModel>(); var vm = main.Countdowns;
        vm.Title = "閱讀的日子"; vm.Direction="正數"; vm.DisplayFormat="年＋月＋天"; vm.TargetDate = DateTime.Today.AddYears(-1).AddMonths(-2).AddDays(-3); vm.PinOnHome = true; await vm.SaveCommand.ExecuteAsync(null);
        vm.Title = "旅行出發時間"; vm.Mode = "倒數時間"; vm.TargetDate = DateTime.Today.AddDays(1); vm.TargetTime = "15:30"; vm.PinOnHome = true; await vm.SaveCommand.ExecuteAsync(null);
        var window = fixture.Get<MainWindow>(); window.WindowState = WindowState.Normal; window.Width = 1100; window.Height = 780;
        try
        {
            window.Show();
            var next = (Border)window.FindName("NextReminderCard");
            var summary = (Border)window.FindName("TodaySummaryCard");
            var pinned = (Border)window.FindName("PinnedCountdownCard");
            Assert.Equal(Grid.GetColumn(next),Grid.GetColumn(summary)); Assert.True(Grid.GetRow(summary)>Grid.GetRow(next));
            Assert.Equal(2,Grid.GetColumn(pinned)); Assert.Equal(0,Grid.GetRow(pinned));
            foreach (var style in new[]{ThemeColorStyle.Default,ThemeColorStyle.Pink,ThemeColorStyle.Bamboo})
            foreach (var dark in new[]{false,true})
            {
                AdaptiveBrushExtension.Apply(dark,style);
                main.PageIndex=0; main.UpdateCountdown();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
                Assert.True(pinned.ActualWidth>200); Assert.True(summary.ActualWidth>400);
                var pinCards = Descendants(pinned).OfType<Border>().Where(b => b.Name == "PinnedCounterItem").ToArray();
                Assert.Equal(2, pinCards.Length);
                Assert.All(pinCards, card => Assert.True(card.ActualHeight < 155, $"Pinned height: {card.ActualHeight}"));
                AssertTitleContrast(pinned);
                Capture(window,$"countdown-home-{style}-{dark}");
                main.OpenCountdownsCommand.Execute(null);
                vm.Mode="倒數時間"; vm.Reminder="提前 1 天"; vm.ReminderTime="14:30";
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
                var view = Descendants(window).OfType<CountdownsView>().Single();
                Descendants(view).OfType<ScrollViewer>().First().ScrollToTop();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
                Assert.Same(vm,view.DataContext); Assert.Equal(6,main.PageIndex);
                Assert.Contains(Descendants(view).OfType<TextBlock>(), t=>t.Text=="閱讀的日子");
                Assert.Null(view.FindName("EditorExpander"));
                Assert.Empty(Descendants(view).OfType<DatePicker>());
                Capture(window,$"countdown-page-{style}-{dark}");
                Assert.True(((FrameworkElement)view.FindName("CompactStatistics")).ActualHeight < 160, $"Statistics height: {((FrameworkElement)view.FindName("CompactStatistics")).ActualHeight}");
                var cards = (ItemsControl)view.FindName("CountdownCards");
                var scroll = Descendants(view).OfType<ScrollViewer>().First();
                var visibleCards = Descendants(cards).OfType<Border>().Where(b => b.Name == "CountdownEventCard").ToArray();
                Assert.Equal(2, visibleCards.Length);
                Assert.All(visibleCards, card =>
                {
                    var point = card.TransformToAncestor(scroll).Transform(new Point());
                    Assert.True(point.Y >= 0 && point.Y + card.ActualHeight <= scroll.ViewportHeight,
                        $"倒數卡應完整出現在初始畫面：top={point.Y}, height={card.ActualHeight}, viewport={scroll.ViewportHeight}");
                });
                AssertTitleContrast(view);
                Capture(window,$"countdown-page-{style}-{dark}");
                ((FrameworkElement)view.FindName("CountdownCards")).BringIntoView();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);window.UpdateLayout();
                Capture(window,$"countdown-cards-{style}-{dark}");
                vm.ViewMode="資料表";
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);window.UpdateLayout();
                ((FrameworkElement)view.FindName("CountdownTable")).BringIntoView();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);window.UpdateLayout();
                AssertTitleContrast((DataGrid)view.FindName("CountdownTable"));
                Capture(window,$"countdown-table-{style}-{dark}");
                vm.ViewMode="卡片";
                vm.NewCommand.Execute(null); vm.Direction="正數"; vm.DisplayFormat="年＋月＋天"; vm.Mode="倒數時間"; vm.Reminder="提前 1 天"; vm.Repeat="每年（農曆）"; vm.LunarMonth=3; vm.LunarDate=23;
                var editor = new CountdownEditorWindow(vm) { Owner = window };
                editor.Show();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); editor.UpdateLayout();
                Assert.Contains(Descendants(editor).OfType<Button>(), b=>b.Content as string=="建立事件" && b.Command==vm.SaveCommand);
                Capture(editor,$"countdown-editor-{style}-{dark}");
                var picker=(DatePicker)editor.FindName("TargetDatePicker");
                picker.BringIntoView();
                picker.IsDropDownOpen=true;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var popup=(System.Windows.Controls.Primitives.Popup)picker.Template.FindName("PART_Popup",picker);
                var calendar=popup.Child as Calendar ?? Descendants(popup.Child).OfType<Calendar>().Single();
                calendar.UpdateLayout();
                Assert.Same(picker.CalendarStyle,calendar.Style);
                Assert.True(calendar.ActualWidth>=308);
                var header=Descendants(calendar).OfType<Button>().Single(b=>b.Name=="PART_HeaderButton");
                header.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(CalendarMode.Year,calendar.DisplayMode);
                calendar.DisplayMode=CalendarMode.Month;
                var date=DateTime.Today.AddDays(12);
                calendar.SelectedDate=date;
                Assert.Equal(date,vm.TargetDate);
                calendar.UpdateLayout();
                CaptureElement(calendar,$"countdown-calendar-{style}-{dark}");
                picker.IsDropDownOpen=false;
                editor.Close();
            }
        }
        finally { window.ForceClose(); AdaptiveBrushExtension.Apply(false); }
    });

    [Fact]
    public Task Popup_editor_validates_saves_and_cancels_without_changing_existing_events() => MilestoneOneTests.RunSta(async () =>
    {
        using var fixture = new Fixture(); await fixture.Initialize();
        var vm = fixture.Get<CountdownsViewModel>(); await vm.LoadAsync();
        var view = new CountdownsView { DataContext = vm };
        var owner = new Window { Content = view, Width = 940, Height = 750 };
        try
        {
            owner.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var create = Dispatcher.CurrentDispatcher.InvokeAsync(async () =>
            {
                var editor = owner.OwnedWindows.OfType<CountdownEditorWindow>().Single();
                try
                {
                    Assert.Equal("新增事件", vm.EditorHeading);
                    await vm.SaveCommand.ExecuteAsync(null);
                    Assert.True(editor.IsVisible); Assert.NotEmpty(vm.Message); Assert.Empty(vm.Items);
                    vm.Title = "彈出視窗事件";
                    await vm.SaveCommand.ExecuteAsync(null);
                    Assert.False(editor.IsVisible);
                }
                finally { editor.Close(); }
            }, DispatcherPriority.ApplicationIdle).Task.Unwrap();
            Descendants(view).OfType<Button>().Single(b => b.Content as string == "＋ 新增倒數").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await create;
            Assert.Single(vm.Items);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); owner.UpdateLayout();
            var cancel = Dispatcher.CurrentDispatcher.InvokeAsync(() =>
            {
                var editor = owner.OwnedWindows.OfType<CountdownEditorWindow>().Single();
                try
                {
                    Assert.Equal("彈出視窗事件", vm.Title);
                    Assert.Equal("編輯事件", vm.EditorHeading);
                    vm.Title = "未儲存的修改";
                }
                finally { editor.Close(); }
            }, DispatcherPriority.ApplicationIdle);
            Descendants(view).OfType<Button>().Single(b => b.Content as string == "編輯").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await cancel;
            Assert.Equal("彈出視窗事件", (await fixture.Get<ICountdownRepository>().GetAllAsync()).Single().Title);
        }
        finally { foreach(Window child in owner.OwnedWindows) child.Close(); owner.Close(); }
    });

    [Fact]
    public Task Count_up_editor_roundtrips_format_and_excludes_elapsed_items_from_due_soon() => MilestoneOneTests.RunSta(async () =>
    {
        using var fixture = new Fixture(); await fixture.Initialize();
        var vm = fixture.Get<CountdownsViewModel>(); await vm.LoadAsync();
        vm.Title="開始閱讀"; vm.Direction="正數"; vm.DisplayFormat="週＋天"; vm.TargetDate=new(2026,9,1); vm.PinOnHome=true;
        await vm.SaveCommand.ExecuteAsync(null); vm.Update(new(2026,9,18));
        var row=Assert.Single(vm.Items);
        Assert.Equal("已過 2 週 3 天",row.Remaining); Assert.Equal(row.Remaining,row.HomeSummary);
        Assert.False(row.IsExpired); Assert.False(row.ShowProgress); Assert.Equal("累計中",row.StatusLabel);
        Assert.Empty(vm.Timeline); vm.Filter="7 天內"; Assert.Empty(vm.VisibleItems);
        vm.EditCommand.Execute(row); Assert.Equal("正數",vm.Direction); Assert.Equal("週＋天",vm.DisplayFormat); Assert.Equal("起始日期",vm.DateLabel);
        vm.DisplayFormat="月＋天"; await vm.SaveCommand.ExecuteAsync(null);
        var saved=Assert.Single(await fixture.Get<ICountdownRepository>().GetAllAsync());
        Assert.Equal("Up",saved.Direction); Assert.Equal("MonthsDays",saved.DisplayFormat);
        var timeRow=new CountdownRow(saved with { Mode="Time", TargetAt=new(2026,9,18,10,0,0) });
        timeRow.Update(new(2026,9,18,11,2,3)); Assert.Contains("01 時 02 分 03 秒",timeRow.HomeSummary);
        timeRow.Update(new(2026,9,18,9,0,0)); Assert.Equal("尚未開始",timeRow.HomeSummary);
    });

    [Theory]
    [InlineData("每天","Daily")]
    [InlineData("每個工作日","Weekly:1,2,3,4,5")]
    [InlineData("每週","Weekly:2,4")]
    [InlineData("每月","Monthly:10:1,3,5,7,9,11")]
    [InlineData("每月農曆","LunarDay:1,15")]
    [InlineData("每年（農曆）","LunarDate:3:23:Both")]
    public Task Countdown_recurrence_selections_save_and_reopen(string repeat,string expected) => MilestoneOneTests.RunSta(async () =>
    {
        using var fixture=new Fixture(); await fixture.Initialize();
        var vm=fixture.Get<CountdownsViewModel>(); await vm.LoadAsync();
        vm.Title="神明生日或定期提醒"; vm.Repeat=repeat; vm.MonthDay=10; vm.LunarMonth=3; vm.LunarDate=23; vm.IncludeLeapMonth=true;
        foreach(var d in vm.Weekdays)d.IsSelected=d.Value is 2 or 4;
        foreach(var m in vm.Months)m.IsSelected=m.Value%2==1;
        vm.Reminder="提前 1 天"; vm.ReminderTime="08:30"; vm.SkipOnHoliday=true;
        await vm.SaveCommand.ExecuteAsync(null);
        var row=Assert.Single(vm.Items); Assert.Equal(expected,row.Item.EffectiveRecurrence); Assert.True(row.Item.SkipOnHoliday);
        vm.EditCommand.Execute(row); Assert.Equal(repeat,vm.Repeat);
        await vm.SaveCommand.ExecuteAsync(null); Assert.Equal(expected,Assert.Single(vm.Items).Item.EffectiveRecurrence);
        if(repeat=="每週")
        {
            vm.EditCommand.Execute(vm.Items[0]);foreach(var d in vm.Weekdays)d.IsSelected=false;
            await vm.SaveCommand.ExecuteAsync(null); Assert.NotEqual("倒數已儲存。",vm.Message);
            Assert.Equal(expected,Assert.Single(vm.Items).Item.EffectiveRecurrence);
        }
    });

    [Fact]
    public Task Task_editor_supports_specific_lunar_date_and_reopens_without_losing_rule() => MilestoneOneTests.RunSta(async () =>
    {
        using var fixture=new Fixture(); await fixture.Initialize();
        var service=fixture.Get<ITaskService>();
        var vm=new TaskEditorViewModel(service,null,false,fixture.Get<ITaskSchedulingService>()) { TaskTitle="農曆生日", Repeat="每年（農曆）", LunarMonth=3, LunarDate=23, IncludeLeapMonth=true };
        await vm.SaveCommand.ExecuteAsync(null); Assert.Empty(vm.Error);
        var task=Assert.Single(await fixture.Get<ITaskRepository>().GetAllAsync()); Assert.Equal("LunarDate:3:23:Both",task.Recurrence);
        var reopened=new TaskEditorViewModel(service,task,false);
        Assert.True(reopened.IsLunarDate); Assert.Equal(3,reopened.LunarMonth);Assert.Equal(23,reopened.LunarDate); Assert.True(reopened.IncludeLeapMonth);
    });

    [Fact]
    public Task Annual_gregorian_date_is_independent_of_start_date_and_roundtrips_in_both_editors() => MilestoneOneTests.RunSta(async () =>
    {
        using var fixture=new Fixture();await fixture.Initialize();
        var vm=fixture.Get<CountdownsViewModel>();await vm.LoadAsync();
        vm.Title="結婚紀念日";vm.TargetDate=new(2026,1,1);vm.Repeat="每年（國曆）";vm.AnnualMonth=5;vm.AnnualDay=20;
        await vm.SaveCommand.ExecuteAsync(null);var row=Assert.Single(vm.Items);
        Assert.Equal("Monthly:20:5",row.Item.EffectiveRecurrence);
        Assert.Equal(new DateTime(2027,5,20),row.Item.NextTarget(new(2026,5,21)));
        vm.EditCommand.Execute(row);Assert.True(vm.IsAnnual);Assert.Equal(5,vm.AnnualMonth);Assert.Equal(20,vm.AnnualDay);
        vm.AnnualMonth=2;vm.AnnualDay=30;await vm.SaveCommand.ExecuteAsync(null);Assert.Contains("有效的國曆月日",vm.Message);
        vm.AnnualDay=29;await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(new DateTime(2028,2,29),vm.Items[0].Item.NextTarget(new(2026,1,1)));
        var service=fixture.Get<ITaskService>();
        var taskEditor=new TaskEditorViewModel(service,null,false) { TaskTitle="每年紀念日",Date=new(2026,1,1),Repeat="每年（國曆）",AnnualMonth=5,AnnualDay=20 };
        await taskEditor.SaveCommand.ExecuteAsync(null);Assert.Empty(taskEditor.Error);
        var task=Assert.Single(await fixture.Get<ITaskRepository>().GetAllAsync());Assert.Equal("Monthly:20:5",task.Recurrence);
        var reopened=new TaskEditorViewModel(service,task,false);Assert.True(reopened.IsAnnual);Assert.Equal(5,reopened.AnnualMonth);Assert.Equal(20,reopened.AnnualDay);
        reopened.AnnualMonth=4;reopened.AnnualDay=31;await reopened.SaveCommand.ExecuteAsync(null);Assert.Contains("有效的國曆月日",reopened.Error);
    });

    private static void AssertTitleContrast(DependencyObject root)
    {
        foreach (var title in Descendants(root).OfType<TextBlock>().Where(t=>t.IsVisible && t.DataContext is CountdownRow row && (t.Text==row.Title || t.Text==row.HomeSummary || t.Text==row.HomeDays || t.Text==row.HomeHours || t.Text==row.HomeMinutes || t.Text==row.HomeSeconds)))
        {
            DependencyObject? parent=VisualTreeHelper.GetParent(title);
            while(parent is not null && parent is not Border { Background: SolidColorBrush { Color.A: 255 } })parent=VisualTreeHelper.GetParent(parent);
            var background=((SolidColorBrush)((Border)parent!).Background).Color;
            var foreground=((SolidColorBrush)title.Foreground).Color;
            static double Channel(byte value){var v=value/255d;return v<=.04045?v/12.92:Math.Pow((v+.055)/1.055,2.4);}
            static double L(Color c)=>.2126*Channel(c.R)+.7152*Channel(c.G)+.0722*Channel(c.B);
            Assert.True((Math.Max(L(foreground),L(background))+.05)/(Math.Min(L(foreground),L(background))+.05)>=4.5, $"文字 {title.Text}: {foreground} / {background}");
        }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
        {
            var child=VisualTreeHelper.GetChild(root,i); yield return child;
            foreach(var descendant in Descendants(child))yield return descendant;
        }
    }
    private static void Capture(Window window,string name)
        => CaptureElement(window,name);
    private static void CaptureElement(FrameworkElement window,string name)
    {
        var directory=Environment.GetEnvironmentVariable("CLOUD_ALARM_SCREENSHOT_DIR"); if(directory is null)return;
        Directory.CreateDirectory(directory);
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),(int)Math.Ceiling(window.ActualHeight),96,96,PixelFormats.Pbgra32);
        bitmap.Render(window); var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file=File.Create(Path.Combine(directory,name+".png")); encoder.Save(file);
    }
    private sealed class Dialogs : IUserDialogs
    {
        public bool AllowDelete=true;
        public bool Confirm(string message)=>AllowDelete;
        public int EditCount { get; private set; }
        public void Edit(AlarmTask? task,bool copy) { Assert.Null(task); Assert.False(copy); EditCount++; }
        public void Export(string contents)=>throw new NotSupportedException();
    }
    private sealed class Fixture : IDisposable,IAppPaths
    {
        private readonly IHost host;
        public Dialogs Dialogs {get;}=new();
        public string DataDirectory {get;}=Path.Combine(Path.GetTempPath(),"CloudAlarmCountdownUi",Guid.NewGuid().ToString("N"));
        public string DatabasePath=>Path.Combine(DataDirectory,"test.db");
        public Fixture(Action<IServiceCollection>? configure=null)=>host=new HostBuilder().ConfigureServices(s=>{CompositionRoot.ConfigureServices(s);s.AddSingleton<IAppPaths>(this);s.AddSingleton<IUserDialogs>(Dialogs);configure?.Invoke(s);}).Build();
        public T Get<T>() where T:notnull=>host.Services.GetRequiredService<T>();
        public Task Initialize()=>Get<IDatabaseInitializer>().InitializeAsync();
        public void Dispose(){host.Dispose();if(Directory.Exists(DataDirectory))Directory.Delete(DataDirectory,true);}
    }
}

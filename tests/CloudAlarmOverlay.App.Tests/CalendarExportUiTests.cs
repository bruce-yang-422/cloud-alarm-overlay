using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
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

public sealed class CalendarExportUiTests
{
    [Fact] public async Task Reversed_dates_show_the_actual_problem_and_can_be_corrected()
    {
        var exporter=new Exporter();var vm=new CalendarExportViewModel(exporter,new Files(),new Clock());
        vm.To=new(2026,11,1);
        Assert.Contains("不能早於起日",vm.Message);
        await vm.PreviewCommand.ExecuteAsync(null);
        Assert.Contains("迄日（2026/11/01）不能早於起日（2026/11/02）",vm.Message);
        Assert.Null(exporter.Options);Assert.False(vm.CanSave);
        vm.To=new(2027,11,1);await vm.PreviewCommand.ExecuteAsync(null);
        Assert.True(vm.CanSave);
    }
    [Fact] public Task Date_popups_are_opaque_within_dialog_and_accept_dates_in_every_theme()=>MilestoneOneTests.RunSta(async()=>
    {
        var vm=new CalendarExportViewModel(new Exporter(),new Files(),new Clock());
        var window=new CalendarExportWindow(vm){Width=520,Height=740,ShowInTaskbar=false};
        try
        {
            window.Show();window.UpdateLayout();
            foreach(var style in Enum.GetValues<ThemeColorStyle>())
            foreach(var dark in new[]{false,true})
            foreach(var name in new[]{"FromDate","ToDate"})
            {
                AdaptiveBrushExtension.Apply(dark,style);
                var picker=(DatePicker)window.FindName(name);
                picker.IsDropDownOpen=true;window.UpdateLayout();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var popup=(Popup)picker.Template.FindName("PART_Popup",picker);
                Assert.True(popup.IsOpen);
                var calendar=Descendants(popup.Child).OfType<Calendar>().Single();calendar.UpdateLayout();
                var surface=(Border)calendar.Template.FindName("PopupSurface",calendar);
                Assert.Equal(255,Assert.IsType<SolidColorBrush>(surface.Background).Color.A);
                if(style==ThemeColorStyle.Bamboo&&name=="ToDate")CapturePopup(calendar,$"calendar-export-picker-bamboo-{(dark?"dark":"light")}");
                var dpi=VisualTreeHelper.GetDpi(window);
                var left=window.PointToScreen(new Point()).X;
                var position=calendar.PointToScreen(new Point());
                Assert.True(position.X>=left,$"{style}/{dark}/{name}: calendar outside left edge");
                Assert.True(position.X+calendar.ActualWidth*dpi.DpiScaleX<=left+window.ActualWidth*dpi.DpiScaleX,$"{style}/{dark}/{name}: calendar outside right edge");
                var next=Descendants(calendar).OfType<Button>().Single(b=>b.Name=="PART_NextButton");
                var month=calendar.DisplayDate;
                next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(month.AddMonths(1).Month,calendar.DisplayDate.Month);
                calendar.UpdateLayout();
                var day=Descendants(calendar).OfType<CalendarDayButton>().First(b=>!b.IsInactive&&b.IsEnabled&&b.DataContext is DateTime date&&date!=picker.SelectedDate);
                Assert.True(day.IsHitTestVisible);Assert.True(day.IsVisible);
                var expected=(DateTime)day.DataContext;
                var hit=calendar.InputHitTest(day.TranslatePoint(new Point(day.ActualWidth/2,day.ActualHeight/2),calendar)) as DependencyObject;
                Assert.True(hit==day||Ancestors(hit).Contains(day),"The calendar day must receive pointer input.");
                day.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=UIElement.MouseLeftButtonDownEvent});
                day.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=UIElement.MouseLeftButtonUpEvent});
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.Equal(expected,picker.SelectedDate);
                Assert.Equal(expected,name=="FromDate"?vm.From:vm.To);
                Assert.False(picker.IsDropDownOpen);
            }
        }
        finally{window.Close();AdaptiveBrushExtension.Apply(false);}
    });
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            foreach(var child in Descendants(VisualTreeHelper.GetChild(root,i)))yield return child;
    }
    private static IEnumerable<DependencyObject> Ancestors(DependencyObject? child)
    {
        while(child is not null){yield return child;child=VisualTreeHelper.GetParent(child);}
    }
    private static void CapturePopup(FrameworkElement element,string name)
    {
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),(int)Math.Ceiling(element.ActualHeight),96,96,PixelFormats.Pbgra32);
        bitmap.Render(element);
        var directory=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../artifacts/screenshots"));Directory.CreateDirectory(directory);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(directory,name+".png"));png.Save(file);
    }
    [Fact] public async Task Multi_year_presets_custom_dates_and_changed_options_invalidate_preview()
    {
        var exporter=new Exporter();var files=new Files();var vm=new CalendarExportViewModel(exporter,files,new Clock());
        Assert.Equal(new DateTime(2026,11,2),vm.From);Assert.Equal(new DateTime(2027,11,1),vm.To);
        foreach(var (preset,years) in new[]{("3 年",3),("5 年",5),("10 年",10)})
        {vm.Range=preset;Assert.Equal(vm.From!.Value.AddYears(years).AddDays(-1),vm.To);}
        vm.To=new(2046,11,1);Assert.Equal("自訂日期",vm.Range);
        await vm.PreviewCommand.ExecuteAsync(null);Assert.True(vm.CanSave);Assert.Equal(new DateOnly(2046,11,1),exporter.Options!.To);
        vm.IncludeReminders=false;Assert.False(vm.CanSave);Assert.False(vm.SaveCommand.CanExecute(null));
        await vm.SaveCommand.ExecuteAsync(null);Assert.Null(files.Content);
        await vm.PreviewCommand.ExecuteAsync(null);await vm.SaveCommand.ExecuteAsync(null);
        Assert.Contains("BEGIN:VCALENDAR",files.Content!);Assert.EndsWith(".ics",files.Filename);Assert.Contains("已匯出",vm.Message);
    }
    [Fact] public async Task Cancelled_save_keeps_preview_without_reporting_success()
    {
        var vm=new CalendarExportViewModel(new Exporter(),new Files{Accept=false},new Clock());
        await vm.PreviewCommand.ExecuteAsync(null);await vm.SaveCommand.ExecuteAsync(null);
        Assert.Contains("已取消儲存",vm.Message);Assert.True(vm.CanSave);
        vm.IncludeTasks=false;vm.IncludeCountdowns=false;await vm.PreviewCommand.ExecuteAsync(null);
        Assert.Contains("至少選擇",vm.Message);Assert.False(vm.CanSave);
    }
    [Fact] public async Task Cancelling_expansion_restores_editing_without_a_saveable_partial_file()
    {
        var exporter=new Exporter{Wait=true};var vm=new CalendarExportViewModel(exporter,new Files(),new Clock());
        var preview=vm.PreviewCommand.ExecuteAsync(null);await exporter.Started.Task;
        Assert.False(vm.CanEdit);Assert.True(vm.CanCancel);vm.CancelCommand.Execute(null);await preview;
        Assert.True(vm.CanEdit);Assert.False(vm.CanSave);Assert.Contains("已取消預覽",vm.Message);
    }
    [Fact] public Task Dialog_shows_long_ranges_and_holiday_warning_in_light_dark_and_narrow_layouts()=>MilestoneOneTests.RunSta(async()=>
    {
        var vm=new CalendarExportViewModel(new Exporter(),new Files(),new Clock());
        var window=new CalendarExportWindow(vm){ShowInTaskbar=false,ShowActivated=false};
        try
        {
            window.Show();vm.Range="5 年";await vm.PreviewCommand.ExecuteAsync(null);
            foreach(var dark in new[]{false,true})
            {
                AdaptiveBrushExtension.Apply(dark);window.Width=620;window.Height=740;window.UpdateLayout();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.Contains("假日資料",((TextBlock)window.FindName("CoverageWarning")).Text);
                Assert.True(((Button)window.FindName("SaveButton")).IsEnabled);
                Capture(window,$"calendar-export-{(dark?"dark":"light")}");
                window.Width=520;window.Height=540;window.UpdateLayout();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var root=(FrameworkElement)window.Content;var save=(FrameworkElement)window.FindName("SaveButton");
                var bounds=save.TransformToAncestor(root).TransformBounds(new Rect(save.RenderSize));Assert.True(bounds.Bottom<=root.ActualHeight);
                Capture(window,$"calendar-export-{(dark?"dark":"light")}-narrow");
            }
        }
        finally{window.Close();AdaptiveBrushExtension.Apply(false);}
    });
    [Fact] public async Task Real_export_service_integrates_with_dialog_and_generates_a_parseable_sample()
    {
        var paths=new Paths();using var host=new HostBuilder().ConfigureServices(s=>{CompositionRoot.ConfigureServices(s);s.AddSingleton<IAppPaths>(paths);}).Build();
        try
        {
            await host.Services.GetRequiredService<IDatabaseInitializer>().InitializeAsync();
            await host.Services.GetRequiredService<ITaskRepository>().SaveLocalAsync(new(){Id="ics-demo",Title="專案進度確認",Description="工作項目, 分工; 備註\\\n中文與 emoji 😀",ScheduledAt=new(2026,11,2,10,0,0),Recurrence="Daily",CreatedAt=new(2026,1,1),UpdatedAt=new(2026,1,1)});
            await host.Services.GetRequiredService<ICountdownRepository>().SaveAsync(new(){Id="ics-launch",Title="專案上線",TargetAt=new(2026,11,4),CreatedAt=new(2026,1,1),ReminderDays=1,ReminderMinutes=540});
            var files=new Files();var vm=new CalendarExportViewModel(host.Services.GetRequiredService<ICalendarIcsExporter>(),files,new Clock());
            vm.To=new(2026,11,4);await vm.PreviewCommand.ExecuteAsync(null);Assert.Contains("4 個事件",vm.Message);
            await vm.SaveCommand.ExecuteAsync(null);Assert.NotNull(files.Content);
            var samples=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../artifacts/samples"));Directory.CreateDirectory(samples);
            await File.WriteAllTextAsync(Path.Combine(samples,"calendar-export.ics"),files.Content);
        }
        finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(paths.DataDirectory))Directory.Delete(paths.DataDirectory,true);}
    }
    [Fact] public async Task Task_and_countdown_entry_points_choose_the_expected_initial_sources()
    {
        var dialogs=new Dialogs();var paths=new Paths();using var host=new HostBuilder().ConfigureServices(s=>{CompositionRoot.ConfigureServices(s);s.AddSingleton<IUserDialogs>(dialogs);s.AddSingleton<IAppPaths>(paths);}).Build();
        await host.Services.GetRequiredService<MainViewModel>().ExportCalendarCommand.ExecuteAsync(null);
        Assert.Equal((true,false),dialogs.Sources);
        await host.Services.GetRequiredService<CountdownsViewModel>().ExportCalendarCommand.ExecuteAsync(null);
        Assert.Equal((false,true),dialogs.Sources);
    }
    private sealed class Dialogs:IUserDialogs
    {
        public (bool,bool) Sources;
        public Task ExportCalendarAsync(bool includeTasks=true,bool includeCountdowns=true){Sources=(includeTasks,includeCountdowns);return Task.CompletedTask;}
        public bool Confirm(string message)=>false;
        public void Edit(AlarmTask? task,bool copy)=>throw new NotSupportedException();
        public void Export(string contents)=>throw new NotSupportedException();
    }
    private sealed class Clock:TimeProvider
    {
        public override DateTimeOffset GetUtcNow()=>new(2026,11,2,0,0,0,TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone=>TimeZoneInfo.FindSystemTimeZoneById("Asia/Taipei");
    }
    private sealed class Exporter:ICalendarIcsExporter
    {
        public CalendarExportOptions? Options;public bool Wait;
        public TaskCompletionSource Started {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<CalendarExportResult> ExportAsync(CalendarExportOptions options,CancellationToken ct=default)
        {
            Options=options;Started.TrySetResult();if(Wait)await Task.Delay(Timeout.Infinite,ct);
            return new("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nEND:VCALENDAR\r\n",2,1,["假日資料目前只涵蓋 2026–2027 年，超出部分可能不完整。"]);
        }
    }
    private sealed class Files:IIcsFileSaver
    {
        public bool Accept=true;public string? Content;public string Filename="";
        public Task<bool> SaveAsync(string content,string filename){Content=content;Filename=filename;return Task.FromResult(Accept);}
    }
    private sealed class Paths:IAppPaths
    {
        public string DataDirectory {get;}=Path.Combine(Path.GetTempPath(),"CalendarExportUi",Guid.NewGuid().ToString("N"));
        public string DatabasePath=>Path.Combine(DataDirectory,"test.db");
    }
    private static void Capture(Window window,string name)
    {
        var root=(FrameworkElement)window.Content;
        var width=root.ActualWidth+root.Margin.Left+root.Margin.Right;var height=root.ActualHeight+root.Margin.Top+root.Margin.Bottom;
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(width),(int)Math.Ceiling(height),96,96,PixelFormats.Pbgra32);
        var drawing=new DrawingVisual();using(var context=drawing.RenderOpen())
        {
            context.DrawRectangle(window.Background,null,new Rect(0,0,width,height));
        }
        bitmap.Render(drawing);
        bitmap.Render(root);
        var directory=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../artifacts/screenshots"));Directory.CreateDirectory(directory);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(directory,name+".png"));png.Save(file);
    }
}

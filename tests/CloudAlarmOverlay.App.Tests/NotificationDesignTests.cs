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
public sealed class NotificationDesignTests
{
    [Theory]
    [InlineData("012-345-678",true)]
    [InlineData("012345678",true)]
    [InlineData("12345678",false)]
    [InlineData("012-345-679",false)]
    [InlineData("01234567A",false)]
    [InlineData("01-2345-678",false)]
    public void Numeric_confirmation_accepts_only_matching_display_or_plain_digits(string input,bool accepted)
    {
        var task=new AlarmTask {Id="numeric",Title="確認",Level=AlarmLevels.Max,ScheduledAt=DateTime.Now,CreatedAt=DateTime.Now,UpdatedAt=DateTime.Now};
        var vm=new AlarmViewModel(task,"012-345-678",true){Input=input};
        var confirmed=false;var incorrect=false;
        vm.Confirmed+=()=>confirmed=true;vm.Incorrect+=()=>incorrect=true;
        vm.ConfirmCommand.Execute(null);
        Assert.Equal(accepted,confirmed);Assert.Equal(!accepted,incorrect);
    }
    [Fact] public void Recommended_palettes_have_readable_text_and_buttons_in_both_modes()
    {
        foreach(var level in new[]{AlarmLevels.Low,AlarmLevels.Mid,AlarmLevels.High,AlarmLevels.Max})
        foreach(var mode in new[]{"亮色","暗色"})
        foreach(var scheme in new[]{"依提醒等級","海灣藍","森林綠","暮紫"})
        {
            var p=NotificationPalette.Create(level,mode,scheme);
            Assert.NotEqual("#000000",p.Background);
            Assert.True(Contrast(p.Background,p.Text)>=4.5);
            Assert.True(Contrast(p.Background,p.Muted)>=4.5);
            Assert.True(Contrast(p.Surface,p.Text)>=4.5);
            Assert.True(Contrast(p.Accent,p.ButtonText)>=4.5);
        }
        Assert.NotEqual(NotificationPalette.Create(AlarmLevels.Mid).Background,NotificationPalette.Create(AlarmLevels.Mid,"暗色").Background);
        foreach(var mode in new[]{"亮色","暗色"})
        {
            Assert.NotEqual(NotificationPalette.Create(AlarmLevels.High,mode).Accent,NotificationPalette.Create(AlarmLevels.Max,mode).Accent);
            Assert.Equal(NotificationPalette.Create(AlarmLevels.Max,mode),NotificationPalette.Create(AlarmLevels.Max,mode,"海灣藍"));
        }
    }
    [Fact] public async Task Expanded_details_span_columns_and_notifications_show_notes_in_both_modes()
    {
        await MilestoneOneTests.RunSta(async()=>
        {
            var paths=new Paths();using var host=new HostBuilder().ConfigureServices(s=>{CompositionRoot.ConfigureServices(s);s.AddSingleton<IAppPaths>(paths);}).Build();
            TaskEditorWindow? editor=null;
            try
            {
                await host.Services.GetRequiredService<IDatabaseInitializer>().InitializeAsync();
                var now=DateTime.Now;
                var task=new AlarmTask{Id="design-test",Title="確認本週專案進度",Description="請整理本週已完成的工作與待處理事項。\n會議前先確認負責人及預定完成日期。",Note="會議室 B，請攜帶筆記。\n如需協助，請聯絡組長。",ScheduledAt=now,CreatedAt=now,UpdatedAt=now};
                Assert.Equal(AlarmLevels.Low,new TaskEditorViewModel(host.Services.GetRequiredService<ITaskService>(),null,false).Level);
                var vm=new TaskEditorViewModel(host.Services.GetRequiredService<ITaskService>(),task,false);
                editor=new TaskEditorWindow{DataContext=vm,ShowInTaskbar=false,ShowActivated=false};editor.Show();editor.UpdateLayout();
                var dateToggle=(System.Windows.Controls.Primitives.ToggleButton)editor.FindName("DateToggle");
                dateToggle.IsChecked=true;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var datePopup=(System.Windows.Controls.Primitives.Popup)editor.FindName("DatePopup");
                Assert.True(datePopup.IsOpen);
                var calendarSurface=(Border)datePopup.Child;
                var calendar=(Calendar)calendarSurface.Child;
                calendar.ApplyTemplate();
                var calendarItem=(System.Windows.Controls.Primitives.CalendarItem)calendar.Template.FindName("PART_CalendarItem",calendar);
                calendarItem.ApplyTemplate();
                var monthView=(Grid)calendarItem.Template.FindName("PART_MonthView",calendarItem);
                Assert.Equal(49,monthView.Children.Count);
                Assert.Equal(7,monthView.Children.OfType<TextBlock>().Count());
                var displayMonth=calendar.DisplayDate;
                ((Button)calendarItem.Template.FindName("PART_NextButton",calendarItem)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(displayMonth.AddMonths(1).Month,calendar.DisplayDate.Month);
                ((Button)calendarItem.Template.FindName("PART_HeaderButton",calendarItem)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(CalendarMode.Year,calendar.DisplayMode);
                Assert.Equal(Visibility.Visible,((Grid)calendarItem.Template.FindName("PART_YearView",calendarItem)).Visibility);
                calendar.DisplayMode=CalendarMode.Month;
                ((Button)calendarItem.Template.FindName("PART_PreviousButton",calendarItem)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(displayMonth.Month,calendar.DisplayDate.Month);
                Screenshot(calendarSurface,"editor-calendar");
                var selectedDate=vm.Date!.Value.Date.AddDays(1);
                calendar.SelectedDate=selectedDate;
                Assert.Equal(selectedDate,vm.Date!.Value.Date);
                Assert.False(datePopup.IsOpen);
                var details=(Border)editor.FindName("DetailsSection");
                Assert.Equal(3,Grid.GetColumnSpan(details));Assert.Equal(1,Grid.GetRow(details));
                var form=(Grid)editor.FindName("FormColumns");Assert.True(details.ActualWidth>form.ActualWidth-4);
                ((ScrollViewer)editor.FindName("FormScroll")).ScrollToBottom();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Screenshot(editor,"editor-details-wide");
                editor.Width=600;editor.UpdateLayout();Assert.Equal(2,Grid.GetRow(details));
                Assert.True(details.ActualWidth>form.ActualWidth-4);
                var prefs=host.Services.GetRequiredService<PreferencesViewModel>();await prefs.LoadAsync();
                var settings=host.Services.GetRequiredService<ISettingsRepository>();
                await settings.SaveAsync(new(){Key="NotificationColorMode",Value="暗色"});
                await settings.SaveAsync(new(){Key="NotificationColorScheme",Value="暮紫"});
                await prefs.LoadAsync();Assert.Equal("暗色",await host.Services.GetRequiredService<NotificationPreferences>().ColorModeAsync());
                foreach(var mode in new[]{"亮色","暗色"})
                foreach(var level in new[]{AlarmLevels.Low,AlarmLevels.Mid,AlarmLevels.High,AlarmLevels.Max})
                {
                    var alarmVm=new AlarmViewModel(task with{Level=level},"1234",true);
                    var window=new AlarmWindow(alarmVm,level,colorMode:mode);
                    try
                    {
                        window.Show();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);window.UpdateLayout();
                        Assert.Equal(task.Note,((CloudAlarmOverlay.App.Controls.MarkdownView)window.FindName("NoteText")).Text);
                        Assert.Equal(task.Description,((CloudAlarmOverlay.App.Controls.MarkdownView)window.FindName("DescriptionText")).Text);
                        Assert.True(alarmVm.HasNote);Assert.True(alarmVm.HasDescription);
                        Assert.Equal((Color)ColorConverter.ConvertFromString(NotificationPalette.Create(level,mode).Background),((SolidColorBrush)window.Background).Color);
                        var button=(Button)window.FindName("ConfirmButton");
                        Assert.True(button.IsVisible);Assert.False(window.Completion.IsCompleted);
                        Screenshot(window,$"notice-{mode}-{level}");
                        alarmVm.Input="1234";
                        alarmVm.ConfirmCommand.Execute(null);Assert.True(await window.Completion);
                    }
                    finally{if(window.IsVisible)window.Finish(false);}
                }
                Assert.False(new AlarmViewModel(task with{Note=" ",Description=null},"",false).HasNote);
            }
            finally{editor?.Close();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(paths.DataDirectory))Directory.Delete(paths.DataDirectory,true);}
        });
    }
    private static double Contrast(string first,string second)
    {
        static double L(string hex){var c=(Color)ColorConverter.ConvertFromString(hex);double F(byte b){double n=b/255d;return n<=.04045?n/12.92:Math.Pow((n+.055)/1.055,2.4);}return .2126*F(c.R)+.7152*F(c.G)+.0722*F(c.B);}
        var a=L(first);var b=L(second);return (Math.Max(a,b)+.05)/(Math.Min(a,b)+.05);
    }
    private static void Screenshot(Window window,string name)
    {
        var folder=Environment.GetEnvironmentVariable("CLOUD_ALARM_SCREENSHOT_DIR");if(folder is null)return;
        var content=(FrameworkElement)window.Content;var bitmap=new RenderTargetBitmap((int)content.ActualWidth,(int)content.ActualHeight,96,96,PixelFormats.Pbgra32);
        var drawing=new DrawingVisual();using(var dc=drawing.RenderOpen())dc.DrawRectangle(new VisualBrush(content),null,new Rect(0,0,content.ActualWidth,content.ActualHeight));
        bitmap.Render(drawing);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));Directory.CreateDirectory(folder);using var file=File.Create(Path.Combine(folder,name+".png"));png.Save(file);
    }
    private static void Screenshot(FrameworkElement element,string name)
    {
        var folder=Environment.GetEnvironmentVariable("CLOUD_ALARM_SCREENSHOT_DIR");if(folder is null)return;
        element.UpdateLayout();
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),(int)Math.Ceiling(element.ActualHeight),96,96,PixelFormats.Pbgra32);
        bitmap.Render(element);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));Directory.CreateDirectory(folder);using var file=File.Create(Path.Combine(folder,name+".png"));png.Save(file);
    }
    private sealed class Paths:IAppPaths
    {
        public string DataDirectory {get;}=Path.Combine(Path.GetTempPath(),"CloudAlarmDesignTests",Guid.NewGuid().ToString("N"));
        public string DatabasePath=>Path.Combine(DataDirectory,"test.db");
    }
}

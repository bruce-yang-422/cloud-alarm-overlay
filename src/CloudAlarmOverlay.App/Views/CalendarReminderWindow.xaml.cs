using System.Windows;
using System.Windows.Input;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Services;

namespace CloudAlarmOverlay.App.Views;

public partial class CalendarReminderWindow : Window
{
    private readonly ITaskService tasks;
    private readonly AlarmTask task;
    private bool busy;
    public CalendarReminderWindow(ITaskService tasks,AlarmTask task)
    {
        if(!task.IsGoogleCalendar)throw new ArgumentException("請選擇已同步的 Google 日曆行程。");
        this.tasks=tasks;this.task=task;
        InitializeComponent();MaxHeight=Math.Max(440,SystemParameters.WorkArea.Height-32);
        EventTitle.Text=task.Title;
        EventStart.Text=$"開始於 {task.CalendarStartAt:yyyy/MM/dd HH:mm} · 本機時間";
        ReminderEnabled.IsChecked=task.Enabled;
        ReminderDate.SelectedDate=task.ScheduledAt.Date;
        ReminderHour.Value=task.ScheduledAt.Hour;
        ReminderMinute.Value=task.ScheduledAt.Minute;
        GoogleDefaults.Text=task.GoogleReminderEnabled==true?$"{task.GoogleReminderAt:yyyy/MM/dd HH:mm:ss}":"未設定彈出式提醒";
        ReminderMode.Text=task.HasCalendarReminderOverride?"目前已自訂":"目前跟隨 Google";
        Closing+=(_,e)=>{if(busy)e.Cancel=true;};
    }
    private async Task ApplyAsync(bool reset)
    {
        if(busy)return;
        busy=true;Editor.IsEnabled=false;SaveButton.IsEnabled=false;CancelButton.IsEnabled=false;ErrorText.Text="";
        try
        {
            if(reset)await tasks.ResetCalendarReminderAsync(task.Id);
            else
            {
                if(ReminderDate.SelectedDate is not {} date)
                    throw new ArgumentException("請選擇提醒日期、小時與分鐘。");
                // Preserve seconds from Google when the displayed time has not changed.
                var time=ReminderHour.Value==task.ScheduledAt.Hour && ReminderMinute.Value==task.ScheduledAt.Minute
                    ?task.ScheduledAt.TimeOfDay:new TimeSpan(ReminderHour.Value,ReminderMinute.Value,0);
                var at=date.Date.Add(time);
                // Toggling alone must not freeze a time that is still following Google.
                await tasks.SaveCalendarReminderAsync(task.Id,ReminderEnabled.IsChecked==true,at==task.ScheduledAt?null:at);
            }
            busy=false;DialogResult=true;
        }
        catch(Exception ex){ErrorText.Text=ex.Message;}
        finally{busy=false;Editor.IsEnabled=true;SaveButton.IsEnabled=true;CancelButton.IsEnabled=true;}
    }
    private async void Save(object sender,RoutedEventArgs e)=>await ApplyAsync(false);
    private async void Reset(object sender,RoutedEventArgs e)=>await ApplyAsync(true);
    private void DragHeader(object sender,MouseButtonEventArgs e){if(e.LeftButton==MouseButtonState.Pressed)DragMove();}
}

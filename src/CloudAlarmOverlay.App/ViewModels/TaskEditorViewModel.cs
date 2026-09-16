using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Services;
namespace CloudAlarmOverlay.App.ViewModels;
public partial class TaskEditorViewModel : ObservableObject
{
    private readonly ITaskService service;
    private readonly AlarmTask original;
    public event Action? Saved;
    public System.Collections.Generic.IEnumerable<string> Emojis { get; init; } = CloudAlarmOverlay.App.Services.EmojiLibrary.Defaults.Split('\n');
    public string Heading {get;}
    [ObservableProperty] private string taskTitle="";
    [ObservableProperty] private string description="";
    [ObservableProperty] private string note="";
    [ObservableProperty] private DateTime? date=DateTime.Today;
    [ObservableProperty] private int selectedHour = 9;
    [ObservableProperty] private int selectedMinute;
    [ObservableProperty] private int selectedSecond;
    public int[] Hours { get; } = Enumerable.Range(0, 24).ToArray();
    public int[] MinutesAndSeconds { get; } = Enumerable.Range(0, 60).ToArray();
    [ObservableProperty] private string level=AlarmLevels.Low;
    [ObservableProperty] private bool enabled=true;
    [ObservableProperty] private bool skipOnHoliday;
    [ObservableProperty] private string repeat="不重複";
    [ObservableProperty] private string repeatDays="1,2,3,4,5";
    [ObservableProperty] private string error="";
    public string[] Levels {get;}=[AlarmLevels.Low,AlarmLevels.Mid,AlarmLevels.High];
    public string[] Repeats {get;}=["不重複","每天","每個工作日","每週","每月","農曆"];
    public bool RequiresDays=>Repeat is "每週" or "每月" or "農曆";
    public bool IsWeekly=>Repeat=="每週";
    public bool IsMonthly=>Repeat=="每月";
    public bool IsLunar=>Repeat=="農曆";
    public DayChoice[] Weekdays {get;}=Enumerable.Range(1,7).Select(i=>new DayChoice(i,new[]{"週一","週二","週三","週四","週五","週六","週日"}[i-1])).ToArray();
    public DayChoice[] LunarDays {get;}=Enumerable.Range(1,30).Select(i=>new DayChoice(i,LunarLabel(i))).ToArray();
    public int[] MonthDays {get;}=Enumerable.Range(1,31).ToArray();
    private bool updatingDays;
    public int MonthDay
    {
        get=>int.TryParse(RepeatDays,out var day)?day:1;
        set {if(IsMonthly)RepeatDays=value.ToString(CultureInfo.InvariantCulture);}
    }
    private static string LunarLabel(int day)=>day<=10?"初"+new[]{"一","二","三","四","五","六","七","八","九","十"}[day-1]:day==20?"二十":day==30?"三十":(day<20?"十":"廿")+new[]{"一","二","三","四","五","六","七","八","九"}[day%10-1];
    partial void OnRepeatDaysChanged(string value)
    {
        updatingDays=true;
        try
        {
            var selected=value.Split(',').ToHashSet();
            foreach(var choice in IsWeekly?Weekdays:IsLunar?LunarDays:[])
                choice.IsSelected=selected.Contains(choice.Value.ToString(CultureInfo.InvariantCulture));
            OnPropertyChanged(nameof(MonthDay));
        }
        finally {updatingDays=false;}
    }
    partial void OnRepeatChanged(string value)
    {
        RepeatDays=value=="每月"?(Date??DateTime.Today).Day.ToString(CultureInfo.InvariantCulture):value=="農曆"?"1,15":"1,2,3,4,5";
        OnRepeatDaysChanged(RepeatDays);
        OnPropertyChanged(nameof(RequiresDays));OnPropertyChanged(nameof(IsWeekly));OnPropertyChanged(nameof(IsMonthly));OnPropertyChanged(nameof(IsLunar));
    }
    public TaskEditorViewModel(ITaskService service,AlarmTask? task,bool copy)
    {
        this.service=service;
        foreach(var choice in Weekdays.Concat(LunarDays))
            choice.PropertyChanged+=(_,args)=>
            {
                if(!updatingDays&&args.PropertyName==nameof(DayChoice.IsSelected))
                    RepeatDays=string.Join(",",(IsWeekly?Weekdays:LunarDays).Where(d=>d.IsSelected).Select(d=>d.Value));
            };
        Heading=task is null?"新增本機任務":copy?"複製為本機任務":"編輯本機任務";
        var at=DateTime.Now.AddMinutes(5);
        original=task is null?new AlarmTask{Id=Guid.NewGuid().ToString("N"),Level=AlarmLevels.Low,Title="",ScheduledAt=at,CreatedAt=DateTime.Now,UpdatedAt=DateTime.Now}:
            copy?task with{Id=Guid.NewGuid().ToString("N"),ExternalId=null,Source=TaskSources.Local,IsTriggered=false,CreatedAt=DateTime.Now,ScheduledAt=task.ScheduledAt>at?task.ScheduledAt:at}:task;
        TaskTitle=original.Title;Description=original.Description??"";Note=original.Note??"";Date=original.ScheduledAt.Date;
        SelectedHour=original.ScheduledAt.Hour;SelectedMinute=original.ScheduledAt.Minute;SelectedSecond=original.ScheduledAt.Second;Level=original.Level;Enabled=original.Enabled;SkipOnHoliday=original.SkipOnHoliday;
        if(Level==AlarmLevels.Max)Level=AlarmLevels.High;
        var parts=original.Recurrence.Split(':');RepeatDays=parts.Length>1?parts[1]:"1,2,3,4,5";
        Repeat=parts[0] switch{"Daily"=>"每天","Weekly"=>parts[1]=="1,2,3,4,5"?"每個工作日":"每週","Monthly"=>"每月","LunarDay"=>"農曆",_=>"不重複"};
        if(parts.Length>1)RepeatDays=parts[1];
    }
    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            if(RequiresDays&&string.IsNullOrWhiteSpace(RepeatDays))throw new ArgumentException("請至少勾選一天。");
            if (Date is null) throw new ArgumentException("請選擇提醒日期。");
            if (SelectedHour is < 0 or > 23 || SelectedMinute is < 0 or > 59 || SelectedSecond is < 0 or > 59)
                throw new ArgumentException("請選擇有效的提醒時間。");
            var clock = new TimeSpan(SelectedHour, SelectedMinute, SelectedSecond);
            var recurrence=Repeat switch{"每天"=>"Daily","每個工作日"=>"Weekly:1,2,3,4,5","每週"=>"Weekly:"+RepeatDays.Replace(" ",""),
                "每月"=>"Monthly:"+RepeatDays.Trim(),"農曆"=>"LunarDay:"+RepeatDays.Replace(" ",""),_=>"None"};
            await service.SaveLocalAsync(original with{Title=TaskTitle.Trim(),Description=Description,Note=Note,ScheduledAt=Date.Value.Date+clock,
                Level=Level,Enabled=Enabled,SkipOnHoliday=SkipOnHoliday,Recurrence=recurrence});
            Saved?.Invoke();
        }
        catch(Exception ex){Error=ex.Message;}
    }
}
public partial class DayChoice(int value,string label):ObservableObject
{
    public int Value {get;}=value;
    public string Label {get;}=label;
    public string ShortLabel=>Label.StartsWith("週",StringComparison.Ordinal)?Label[1..]:Label;
    [ObservableProperty] private bool isSelected;
}

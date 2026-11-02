using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CloudAlarmOverlay.App.Services;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Services;

namespace CloudAlarmOverlay.App.ViewModels;

public partial class CalendarExportViewModel:ObservableObject
{
    private readonly ICalendarIcsExporter exporter;
    private readonly IIcsFileSaver files;
    private bool settingRange;
    private CancellationTokenSource? cancellation;
    private CalendarExportResult? preview;
    private CalendarExportOptions? previewOptions;
    public CalendarExportViewModel(ICalendarIcsExporter exporter,IIcsFileSaver files,TimeProvider clock,bool includeTasks=true,bool includeCountdowns=true)
    {
        this.exporter=exporter;this.files=files;
        IncludeTasks=includeTasks;IncludeCountdowns=includeCountdowns;
        From=clock.GetLocalNow().Date;
    }
    public string[] Ranges {get;}=["1 年","3 年","5 年","10 年","自訂日期"];
    [ObservableProperty] private string range="1 年";
    [ObservableProperty] private DateTime? from;
    [ObservableProperty] private DateTime? to;
    [ObservableProperty] private bool includeTasks=true;
    [ObservableProperty] private bool includeCountdowns=true;
    [ObservableProperty] private bool includeDisabledTasks;
    [ObservableProperty] private bool includeCompleted;
    [ObservableProperty] private bool includeReminders=true;
    [ObservableProperty] private string message="先選擇來源與日期範圍，再預覽事件數量。";
    [ObservableProperty] private string warnings="";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPreview))]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(CanCancel))]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyCanExecuteChangedFor(nameof(PreviewCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool busy;
    public bool CanEdit=>!Busy;
    public bool CanPreview=>!Busy;
    public bool CanSave=>!Busy&&preview is {EventCount:>0};
    public bool CanCancel=>cancellation is not null;
    public string TimeZoneLabel=>"時間依本機時區匯出："+TimeZoneInfo.Local.DisplayName;
    public string LunarRangeLabel=>"農曆可計算期間："+new ChineseLunisolarCalendar().MinSupportedDateTime.ToString("yyyy/MM/dd",CultureInfo.InvariantCulture)+
        "–"+new ChineseLunisolarCalendar().MaxSupportedDateTime.ToString("yyyy/MM/dd",CultureInfo.InvariantCulture);
    private void InvalidatePreview()
    {
        cancellation?.Cancel();preview=null;previewOptions=null;Warnings="";
        Message=From is {} begin&&To is {} end&&end.Date<begin.Date
            ?$"迄日（{end:yyyy/MM/dd}）不能早於起日（{begin:yyyy/MM/dd}），請調整日期。"
            :"條件已變更，請重新預覽。";
        OnPropertyChanged(nameof(CanSave));SaveCommand.NotifyCanExecuteChanged();
    }
    partial void OnRangeChanged(string value){ApplyRange();InvalidatePreview();}
    partial void OnFromChanged(DateTime? value){ApplyRange();InvalidatePreview();}
    partial void OnToChanged(DateTime? value){if(!settingRange)Range="自訂日期";InvalidatePreview();}
    partial void OnIncludeTasksChanged(bool value)=>InvalidatePreview();
    partial void OnIncludeCountdownsChanged(bool value)=>InvalidatePreview();
    partial void OnIncludeDisabledTasksChanged(bool value)=>InvalidatePreview();
    partial void OnIncludeCompletedChanged(bool value)=>InvalidatePreview();
    partial void OnIncludeRemindersChanged(bool value)=>InvalidatePreview();
    private void ApplyRange()
    {
        if(From is not {} start||Range=="自訂日期")return;
        var years=Range switch{"3 年"=>3,"5 年"=>5,"10 年"=>10,_=>1};
        settingRange=true;
        try{To=start.Year+years<=9998?start.Date.AddYears(years).AddDays(-1):new DateTime(9998,12,31);}
        finally{settingRange=false;}
    }
    [RelayCommand(CanExecute=nameof(CanPreview))] private async Task PreviewAsync()
    {
        if(Busy)return;
        using var tokenSource=new CancellationTokenSource();cancellation=tokenSource;
        Busy=true;preview=null;previewOptions=null;Warnings="";Message="正在展開排程並產生行事曆預覽…";
        try
        {
            if(From is not {} begin||To is not {} end)throw new ArgumentException("請選擇起日與迄日。");
            var options=new CalendarExportOptions(DateOnly.FromDateTime(begin),DateOnly.FromDateTime(end))
                {IncludeTasks=IncludeTasks,IncludeCountdowns=IncludeCountdowns,IncludeDisabledTasks=IncludeDisabledTasks,IncludeCompleted=IncludeCompleted,IncludeReminders=IncludeReminders};
            options.Validate();
            var result=await exporter.ExportAsync(options,tokenSource.Token);
            tokenSource.Token.ThrowIfCancellationRequested();
            preview=result;previewOptions=options;Warnings=string.Join("\n\n",result.Warnings);
            Message=result.EventCount==0?"這個範圍內沒有符合條件的事件，請調整來源或日期。":$"共 {result.EventCount:N0} 個事件：本機任務 {result.TaskEvents:N0} 個，倒數／正數 {result.CountdownEvents:N0} 個。";
        }
        catch(OperationCanceledException){Message="已取消預覽。";}
        catch(Exception ex){Message="無法匯出："+ex.Message;}
        finally{cancellation=null;Busy=false;}
    }
    [RelayCommand(CanExecute=nameof(CanSave))] private async Task SaveAsync()
    {
        if(!CanSave||preview is not {} result||previewOptions is not {} options)return;
        Busy=true;
        try
        {
            var saved=await files.SaveAsync(result.Content,$"CloudAlarmOverlay_{options.From:yyyyMMdd}_{options.To:yyyyMMdd}.ics");
            Message=saved?$"已匯出 {result.EventCount:N0} 個事件，可匯入 Outlook 或 Google 日曆。":"已取消儲存，預覽仍保留。";
        }
        catch(Exception ex){Message="儲存失敗："+ex.Message;}
        finally{Busy=false;}
    }
    [RelayCommand(CanExecute=nameof(CanCancel))] public void Cancel()=>cancellation?.Cancel();
}

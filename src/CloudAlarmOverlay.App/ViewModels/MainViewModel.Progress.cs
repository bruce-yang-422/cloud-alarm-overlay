using System.Text.Json;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Recurrence;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CloudAlarmOverlay.App.ViewModels;

public partial class MainViewModel
{
    private IReadOnlyList<TaskRow> progressRows=[];
    private IReadOnlyList<TaskCompletion> completionRecords=[];
    private IReadOnlyList<TaskCompletion> undoCompletions=[];
    [ObservableProperty] private string taskSection="待處理";
    public string[] TaskSections {get;}=["待處理","已完成","已結束","全部"];
    public int PendingTaskCount=>progressRows.Count(r=>MatchesTaskScope(r)&&!r.IsCompleted&&!r.IsEnded);
    public int CompletedTaskCount=>progressRows.Count(r=>MatchesTaskScope(r)&&r.IsCompleted);
    public int EndedTaskCount=>progressRows.Count(r=>MatchesTaskScope(r)&&r.IsEnded);
    public string TaskSectionSummary=>$"待處理 {PendingTaskCount} · 已完成 {CompletedTaskCount} · 已結束 {EndedTaskCount}";
    public string TaskSectionHint=>TaskSection switch
    {
        "已完成"=>"本機完成紀錄；可恢復待處理。重複任務只完成單次，後續排程仍會保留。",
        "已結束"=>"活動時間已結束，並不代表事情已完成；仍可手動標記完成。",
        _=>"逾期項目優先顯示。完成與提醒開關分開；外部任務只記錄本機進度，不回寫雲端。"
    };
    public string TaskEmptyMessage=>TaskSection=="已完成"?"尚無符合條件的完成紀錄。":TaskSection=="已結束"?"尚無符合條件的已結束活動。":"目前沒有符合條件的任務；可切換分區或清除搜尋與來源篩選。";
    [ObservableProperty] private string taskProgressMessage="";
    public bool HasTaskProgressMessage=>TaskProgressMessage.Length>0;
    public bool CanUndoTaskCompletion=>undoCompletions.Count>0;
    partial void OnTaskProgressMessageChanged(string value)=>OnPropertyChanged(nameof(HasTaskProgressMessage));
    partial void OnTaskSectionChanged(string value)
    {
        SetTaskSelection([]);FilterTasks();OnPropertyChanged(nameof(TaskSectionHint));OnPropertyChanged(nameof(TaskEmptyMessage));
    }
    private async Task LoadTaskProgressAsync(DateTime now)
    {
        completionRecords=await taskCompletions.GetAllAsync();
        var holidays=await taskHolidays.GetAllAsync();var rows=new List<TaskRow>();
        foreach(var task in allTasks)
        {
            DateTime? occurrence=task.CalendarStartAt??task.ScheduledAt;
            if(task.Recurrence!="None")
            {
                occurrence=RecurrenceCalendar.Next(task.Recurrence,task.ScheduledAt,now.Date,true,null,holidays,task.SkipOnHoliday);
                while(occurrence is {} at&&completionRecords.Any(c=>c.Matches(task,at)))
                    occurrence=RecurrenceCalendar.Next(task.Recurrence,task.ScheduledAt,at,false,null,holidays,task.SkipOnHoliday);
            }
            if(occurrence is not {} scheduled||completionRecords.Any(c=>c.Matches(task,scheduled)))continue;
            var ended=task.Recurrence=="None"&&((task.ActivityEndAt??task.CalendarStartAt) is {} end&&end<=now);
            rows.Add(new(task,scheduled){OccurrenceAt=TaskCompletion.Occurrence(task,scheduled),IsEnded=ended});
        }
        foreach(var completion in completionRecords)
        {
            var live=allTasks.FirstOrDefault(t=>t.Id==completion.TaskId&&t.Source==completion.Source);
            var snapshot=completion.Source.StartsWith("Google:",StringComparison.Ordinal)&&live is not null?live:JsonSerializer.Deserialize<AlarmTask>(completion.SnapshotJson);
            if(snapshot is not null)rows.Add(new(snapshot,completion.OccurrenceAt){OccurrenceAt=completion.OccurrenceAt,CompletedAt=completion.CompletedAt,IsAvailable=live is not null});
        }
        progressRows=rows;
        foreach(var name in new[]{nameof(PendingTaskCount),nameof(CompletedTaskCount),nameof(EndedTaskCount),nameof(TaskSectionSummary),nameof(TaskEmptyMessage)})OnPropertyChanged(name);
    }
    private IEnumerable<AlarmTask> CalendarProgressTasks()=>progressRows.Select(r=>r.Task)
        .Where(t=>(SourceFilter=="全部來源"||t.Source==SourceFilter||SourceFilter=="Google"&&t.Source.StartsWith("Google:",StringComparison.Ordinal))&&
            (t.Title.Contains(Search,StringComparison.OrdinalIgnoreCase)||t.Description?.Contains(Search,StringComparison.OrdinalIgnoreCase)==true||t.Note?.Contains(Search,StringComparison.OrdinalIgnoreCase)==true))
        .DistinctBy(t=>(t.Id,t.Source,t.ScheduledAt,t.ActivityStartAt,t.ActivityEndAt));
    private bool MatchesTaskScope(TaskRow row)=>(SourceFilter=="全部來源"||row.Task.Source==SourceFilter||SourceFilter=="Google"&&row.Task.Source.StartsWith("Google:",StringComparison.Ordinal))&&
        (row.Title.Contains(Search,StringComparison.OrdinalIgnoreCase)||row.Task.Description?.Contains(Search,StringComparison.OrdinalIgnoreCase)==true||row.Task.Note?.Contains(Search,StringComparison.OrdinalIgnoreCase)==true);
    private IEnumerable<TaskRow> FilteredProgressRows()
        =>progressRows.Where(r=>MatchesTaskScope(r)&&(TaskSection=="全部"||TaskSection=="已完成"&&r.IsCompleted||TaskSection=="已結束"&&r.IsEnded||TaskSection=="待處理"&&!r.IsCompleted&&!r.IsEnded))
            .OrderBy(r=>r.IsCompleted?2:r.IsEnded?1:0).ThenByDescending(r=>r.IsCompleted?r.CompletedAt:DateTime.MinValue).ThenBy(r=>r.DisplayAt).ThenBy(r=>r.Title);
    private bool CanCompleteSelectedTasks()=>SelectedTasks.Any(r=>!r.IsCompleted&&r.IsAvailable);
    private bool CanRestoreSelectedTasks()=>SelectedTasks.Any(r=>r.IsCompleted&&r.IsAvailable);
    public bool HasCompletableSelection=>CanCompleteSelectedTasks();
    public bool HasRestorableSelection=>CanRestoreSelectedTasks();
    [RelayCommand(CanExecute=nameof(CanCompleteSelectedTasks))]
    private async Task CompleteSelectedTasksAsync()
    {
        var rows=SelectedTasks.Where(r=>!r.IsCompleted&&r.IsAvailable).ToArray();
        try
        {
            var completed=new List<TaskCompletion>();
            foreach(var row in rows)
            {
                await taskCompletions.CompleteAsync(row.Task,row.OccurrenceAt);
                completed.Add(new(){Source=row.Task.Source,TaskId=row.Task.Id,OccurrenceAt=row.OccurrenceAt});
            }
            undoCompletions=completed;OnPropertyChanged(nameof(CanUndoTaskCompletion));
            await RefreshAsync();TaskProgressMessage=$"已完成 {rows.Length} 項，可在「已完成」查看。重複任務保留後續排程；外部資料未變更。";
        }
        catch(Exception ex){Status="無法標記完成："+ex.Message;}
    }
    [RelayCommand(CanExecute=nameof(CanRestoreSelectedTasks))]
    private async Task RestoreSelectedTasksAsync()
    {
        try
        {
            var rows=SelectedTasks.Where(r=>r.IsCompleted&&r.IsAvailable).ToArray();
            foreach(var row in rows)await taskCompletions.RestoreAsync(row.Task.Source,row.Task.Id,row.OccurrenceAt);
            undoCompletions=[];OnPropertyChanged(nameof(CanUndoTaskCompletion));
            await RefreshAsync();TaskProgressMessage=$"已恢復 {rows.Length} 項。活動已結束時回到「已結束」，其餘回到「待處理」。";
        }
        catch(Exception ex){Status="無法恢復任務："+ex.Message;}
    }
    [RelayCommand] private async Task UndoTaskCompletionAsync()
    {
        try
        {
            foreach(var item in undoCompletions)await taskCompletions.RestoreAsync(item.Source,item.TaskId,item.OccurrenceAt);
            undoCompletions=[];OnPropertyChanged(nameof(CanUndoTaskCompletion));
            await RefreshAsync();TaskProgressMessage="已復原這次完成操作。";
        }
        catch(Exception ex){Status="無法復原："+ex.Message;}
    }
    [RelayCommand] private void DismissTaskProgressMessage(){TaskProgressMessage="";undoCompletions=[];OnPropertyChanged(nameof(CanUndoTaskCompletion));}
    private bool CanToggleCalendarCompletion(CalendarEntry? entry)=>entry is not null&&allTasks.Any(t=>t.Id==entry.Task.Id&&t.Source==entry.Task.Source);
    [RelayCommand(CanExecute=nameof(CanToggleCalendarCompletion))]
    private async Task ToggleCalendarCompletionAsync(CalendarEntry? entry)
    {
        if(entry is null||!CanToggleCalendarCompletion(entry))return;
        var at=entry.Task.Recurrence=="None"?entry.Task.ScheduledAt:entry.Start;
        SetTaskSelection([new TaskRow(entry.Task,at){OccurrenceAt=TaskCompletion.Occurrence(entry.Task,at),CompletedAt=entry.CompletedAt}]);
        if(entry.CompletedAt.HasValue)await RestoreSelectedTasksAsync();else await CompleteSelectedTasksAsync();
        SetTaskSelection([]);
    }
}

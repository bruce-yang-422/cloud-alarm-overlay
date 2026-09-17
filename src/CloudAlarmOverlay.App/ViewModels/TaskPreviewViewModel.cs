using System.Text.Json;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Recurrence;
namespace CloudAlarmOverlay.App.ViewModels;

public sealed class TaskPreviewViewModel
{
    public string Title {get;}
    public string Description {get;}
    public string Note {get;}
    public string Summary {get;}
    public string HistoryDetails {get;}
    public string Notice {get;}
    public TaskPreviewViewModel(AlarmTask? task,AcknowledgementLog? entry=null)
    {
        Title=entry?.TaskName??task?.Title??"任務詳細訊息";
        Description=string.IsNullOrWhiteSpace(task?.Description)?"（未填寫詳細內容）":task.Description;
        Note=string.IsNullOrWhiteSpace(task?.Note)?"（未填寫補充說明）":task.Note;
        Summary=task is null?$"來源：{entry?.Source??"未知"}":$"{task.Level} · {task.Source} · {RecurrenceRule.Describe(task.Recurrence)}\n{(task.Recurrence=="None"?"排定時間":"起始時間")}：{task.ScheduledAt:yyyy/MM/dd HH:mm}";
        Notice=entry is null?"唯讀預覽":task is null?"此筆舊紀錄沒有完整內容快照，無法取得當時的詳細訊息。":"顯示當次提醒保存的內容";
        if(entry is not null)
        {
            var row=new HistoryRow(entry);
            HistoryDetails=$"預定時間：{row.Scheduled}\n觸發時間：{row.Triggered}\n確認時間：{row.Acknowledged}\n結果：{row.Result}";
            if(task is null)Description=Note="當時內容不可取得";
        }
        else HistoryDetails="";
    }
    public static TaskPreviewViewModel FromHistory(AcknowledgementLog entry)
    {
        AlarmTask? snapshot=null;
        if(!string.IsNullOrWhiteSpace(entry.TaskSnapshotJson))
            try {snapshot=JsonSerializer.Deserialize<AlarmTask>(entry.TaskSnapshotJson);}
            catch(JsonException) { }
        return new(snapshot,entry);
    }
}

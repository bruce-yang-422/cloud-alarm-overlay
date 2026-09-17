using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using CloudAlarmOverlay.Core.Recurrence;
namespace CloudAlarmOverlay.Core.Services;
internal sealed class TaskService(ITaskRepository tasks,ChangeSignal changes) : ITaskService
{
    public async Task SaveLocalAsync(AlarmTask task,CancellationToken cancellationToken=default)
    {
        if(task.Source!=TaskSources.Local) throw new InvalidOperationException("雲端任務為唯讀。");
        if(task.Level==AlarmLevels.Max) throw new ArgumentException("強制通知僅由 IT 在雲端指派；本機任務最高可選緊急提醒。");
        if(string.IsNullOrWhiteSpace(task.Title)||task.Title.Length>50) throw new ArgumentException("任務名稱限 1–50 字。");
        if(task.Description?.Length>1000||task.Note?.Length>500) throw new ArgumentException("詳細內容限 1,000 字，補充說明及備註限 500 字（含 Markdown 語法）。");
        if(task.Level is not (AlarmLevels.Low or AlarmLevels.Mid or AlarmLevels.High or AlarmLevels.Max)) throw new ArgumentException("提醒等級無效。");
        RecurrenceRule.Validate(task.Recurrence);
        if(task.Recurrence=="None" && await tasks.GetByIdAsync(task.Id,cancellationToken) is null && task.ScheduledAt<DateTime.Now)
            throw new ArgumentException("新增任務時間不可早於現在。");
        await tasks.SaveLocalAsync(task with { RequireAcknowledgement=task.Level!=AlarmLevels.Low,UpdatedAt=DateTime.Now },cancellationToken);
        changes.Notify();
    }
    public async Task DeleteLocalAsync(string id,CancellationToken cancellationToken=default)
    {
        await tasks.DeleteLocalAsync(id,cancellationToken);
        changes.Notify();
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Services;
namespace CloudAlarmOverlay.App.ViewModels;
public partial class AlarmViewModel(AlarmTask task,string code,bool preview,string? caption=null,bool allowSnooze=false,int snoozeCount=0):ObservableObject
{
    public bool CanSnooze => allowSnooze && !preview && caption is null && snoozeCount<SnoozePolicy.MaximumCount;
    public int[] SnoozeChoices { get; } = [5,10,15,30];
    [ObservableProperty] private int snoozeMinutes=5;
    public string SnoozeLabel => $"忙碌中，稍後提醒（已延後 {snoozeCount}/3 次）";
    public event Action<int>? Snoozed;
    [RelayCommand] private void Snooze() { if(CanSnooze && SnoozePolicy.IsValidMinutes(SnoozeMinutes)) Snoozed?.Invoke(SnoozeMinutes); }
    public string Title=>task.Title;
    public string Description=>task.Description??"";
    public string Note=>task.Note??"";
    public bool HasDescription=>!string.IsNullOrWhiteSpace(Description);
    public bool HasNote=>!string.IsNullOrWhiteSpace(Note);
    public bool IsPomodoro=>caption=="番茄鐘";
    public string Caption=>caption??(preview?"通知預覽 · ":"")+CloudAlarmOverlay.App.Styles.AlarmLevelLabelConverter.Label(task.Level)+(task.Level==AlarmLevels.Max?" · 公司／系統發布":"");
    public bool RequiresCode=>task.Level==AlarmLevels.Max;
    public string Code=>code;
    public string Instruction=>caption is not null?"閱讀後按下確認，繼續番茄鐘流程。":RequiresCode?"正式確認請輸入 9 位數字確認碼；可輸入 xxx-xxx-xxx 或連續 9 位數字。"+(CanSnooze?" 忙碌時可直接按「稍後提醒」，不需輸入確認碼。":""):task.Level==AlarmLevels.Low?"10 秒後自動關閉；滑鼠移入可暫停。":"閱讀後，按下確認即可完成提醒確認。";
    [ObservableProperty] private string input="";
    [ObservableProperty] private string error="";
    public event Action? Confirmed;
    public event Action? Incorrect;
    [RelayCommand] private void Confirm()
    {
        var entered=Input.Trim();
        if(RequiresCode&&entered!=Code&&entered!=Code.Replace("-",""))
        {Error="確認碼不正確，請再試一次。";Incorrect?.Invoke();return;}
        Confirmed?.Invoke();
    }
}

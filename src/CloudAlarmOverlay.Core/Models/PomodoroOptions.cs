namespace CloudAlarmOverlay.Core.Models;
public sealed record PomodoroOptions
{
    public int FocusMinutes {get;init;}=25;
    public int BreakMinutes {get;init;}=5;
    public int LongBreakMinutes {get;init;}=15;
    public int Interval {get;init;}=4;
    public int DailyGoal {get;init;}=8;
    public bool LongBreakEnabled {get;init;}=true;
    public bool SoundEnabled {get;init;}=true;
    public string SoundName {get;init;}=".Default";
    public void Validate()
    {
        if(FocusMinutes is <5 or >90 || BreakMinutes is <1 or >30 || LongBreakMinutes is <5 or >60 || Interval is <2 or >8 || DailyGoal is <1 or >30)
            throw new ArgumentException("請檢查專注、休息時間與週期間隔的範圍。");
    }
}
public sealed record PomodoroState(string Phase="Focus",string Status="Idle",TimeSpan Remaining=default,DateTime? StartedAt=null,int Cycle=0,int Round=1);

using System.Text.Json;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
namespace CloudAlarmOverlay.Core.Services;
internal sealed class PomodoroService(IPomodoroRepository repository,TimeProvider clock):IPomodoroService
{
    private readonly SemaphoreSlim gate=new(1,1);
    private bool initialized;
    private long startedTimestamp;
    private TimeSpan remainingAtStart;
    private PomodoroLogEntry? active;
    private bool longBreakDue;
    public PomodoroOptions Options {get;private set;}=new();
    public PomodoroState State {get;private set;}=new(Remaining:TimeSpan.FromMinutes(25));
    public string NextPhase=>State.Phase!="Focus"?"Focus":(State.Status=="AwaitingConfirmation"?longBreakDue:Options.LongBreakEnabled&&State.Cycle==Options.Interval-1)?"LongBreak":"Break";
    public event Action? Changed;
    private DateTime Now=>clock.GetLocalNow().DateTime;
    private async Task Run(Func<Task> action,CancellationToken ct)
    {await gate.WaitAsync(ct);try{await action();}finally{gate.Release();}Changed?.Invoke();}
    public Task InitializeAsync(CancellationToken cancellationToken=default)=>Run(async()=>
    {
        if(initialized)return;
        var json=(await repository.GetSettingsAsync(cancellationToken)).FirstOrDefault(x=>x.Key=="Options")?.Value;
        if(json is not null){Options=JsonSerializer.Deserialize<PomodoroOptions>(json)??new();Options.Validate();}
        foreach(var row in (await repository.GetLogsAsync(DateTime.MinValue,DateTime.MaxValue,cancellationToken)).Where(x=>x.IsActive))
            await repository.SaveLogAsync(row with{IsActive=false,Result="Interrupted"},cancellationToken);
        State=new(Remaining:TimeSpan.FromMinutes(Options.FocusMinutes));initialized=true;
    },cancellationToken);
    private async Task Begin(string phase,CancellationToken ct)
    {
        var minutes=phase=="Focus"?Options.FocusMinutes:phase=="Break"?Options.BreakMinutes:Options.LongBreakMinutes;
        var row=new PomodoroLogEntry{Id=Guid.NewGuid().ToString("N"),Type=phase,StartedAt=Now,PlannedMinutes=minutes,Result="Interrupted",IsActive=true};
        await repository.SaveLogAsync(row,ct);active=row;
        State=State with{Phase=phase,Status="Running",Remaining=TimeSpan.FromMinutes(minutes),StartedAt=row.StartedAt};
        startedTimestamp=clock.GetTimestamp();remainingAtStart=State.Remaining;
    }
    public Task StartAsync(CancellationToken cancellationToken=default)=>Run(async()=>
    {
        if(State.Status=="Idle")await Begin("Focus",cancellationToken);
        else if(State.Status=="Paused"){startedTimestamp=clock.GetTimestamp();remainingAtStart=State.Remaining;State=State with{Status="Running"};}
    },cancellationToken);
    public Task PauseAsync(CancellationToken cancellationToken=default)=>Run(async()=>
    {if(State.Status!="Running")return;await Update(cancellationToken);if(State.Status=="Running")State=State with{Status="Paused"};},cancellationToken);
    private async Task End(bool completed,CancellationToken ct)
    {
        if(active is null)return;
        await repository.SaveLogAsync(active with{IsActive=false,EndedAt=Now,CompletedAt=completed?Now:null,Result=completed?"Completed":"Interrupted"},ct);
        active=null;
        if(completed&&State.Phase=="Focus")
        {
            var cycle=State.Cycle+1;longBreakDue=Options.LongBreakEnabled&&cycle>=Options.Interval;
            State=State with{Cycle=cycle%Options.Interval};
        }
        State=State with{Status="AwaitingConfirmation",Remaining=TimeSpan.Zero};
    }
    private async Task Update(CancellationToken ct)
    {
        if(State.Status!="Running")return;
        var left=remainingAtStart-clock.GetElapsedTime(startedTimestamp);
        if(left<=TimeSpan.Zero)await End(true,ct);else State=State with{Remaining=left};
    }
    public Task TickAsync(CancellationToken cancellationToken=default)=>Run(()=>Update(cancellationToken),cancellationToken);
    public Task SkipAsync(CancellationToken cancellationToken=default)=>Run(async()=>
    {if(State.Status is "Running" or "Paused")await End(false,cancellationToken);},cancellationToken);
    public Task ResetAsync(CancellationToken cancellationToken=default)=>Run(async()=>
    {
        if(active is not null)await End(false,cancellationToken);
        longBreakDue=false;State=new(Remaining:TimeSpan.FromMinutes(Options.FocusMinutes));
    },cancellationToken);
    public Task ConfirmAsync(CancellationToken cancellationToken=default)=>Run(async()=>
    {
        if(State.Status!="AwaitingConfirmation")return;
        if(State.Phase=="Focus"){await Begin(longBreakDue?"LongBreak":"Break",cancellationToken);longBreakDue=false;}
        else State=State with{Phase="Focus",Status="Idle",StartedAt=null,Remaining=TimeSpan.FromMinutes(Options.FocusMinutes),Round=State.Round+1};
    },cancellationToken);
    public Task SaveOptionsAsync(PomodoroOptions options,CancellationToken cancellationToken=default)=>Run(async()=>
    {
        if(State.Status!="Idle")throw new InvalidOperationException("計時結束後才能調整設定。");
        options.Validate();await repository.SaveSettingAsync(new(){Key="Options",Value=JsonSerializer.Serialize(options)},cancellationToken);
        Options=options;State=State with{Remaining=TimeSpan.FromMinutes(options.FocusMinutes),Cycle=0};
    },cancellationToken);
}
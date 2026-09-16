namespace CloudAlarmOverlay.Core.Services;
public sealed class RuntimeState(ChangeSignal signal)
{
    private long resumeTicks;
    public DateTime ResumedAt=>new(Interlocked.Read(ref resumeTicks));
    public void Resume(){Interlocked.Exchange(ref resumeTicks,DateTime.Now.Ticks);signal.Notify();}
}

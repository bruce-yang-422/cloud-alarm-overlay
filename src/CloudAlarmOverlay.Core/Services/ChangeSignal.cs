namespace CloudAlarmOverlay.Core.Services;
public sealed class ChangeSignal : IDisposable
{
    private readonly SemaphoreSlim _wake = new(0, 1);
    public event Action? Changed;
    public void Notify()
    {
        if (_wake.CurrentCount == 0) { try { _wake.Release(); } catch (SemaphoreFullException) { } }
        Changed?.Invoke();
    }
    public Task<bool> WaitAsync(TimeSpan delay, CancellationToken ct) => _wake.WaitAsync(delay, ct);
    public void Dispose() => _wake.Dispose();
}

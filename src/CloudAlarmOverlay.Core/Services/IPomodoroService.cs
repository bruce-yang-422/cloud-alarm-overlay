using CloudAlarmOverlay.Core.Models;
namespace CloudAlarmOverlay.Core.Services;
public interface IPomodoroService
{
    PomodoroState State {get;}
    PomodoroOptions Options {get;}
    string NextPhase {get;}
    event Action? Changed;
    Task InitializeAsync(CancellationToken cancellationToken=default);
    Task StartAsync(CancellationToken cancellationToken=default);
    Task PauseAsync(CancellationToken cancellationToken=default);
    Task ResetAsync(CancellationToken cancellationToken=default);
    Task SkipAsync(CancellationToken cancellationToken=default);
    Task TickAsync(CancellationToken cancellationToken=default);
    Task ConfirmAsync(CancellationToken cancellationToken=default);
    Task SaveOptionsAsync(PomodoroOptions options,CancellationToken cancellationToken=default);
}
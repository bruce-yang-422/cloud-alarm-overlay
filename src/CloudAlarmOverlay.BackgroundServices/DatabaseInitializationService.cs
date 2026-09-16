using CloudAlarmOverlay.Core.Services;
using Microsoft.Extensions.Hosting;

namespace CloudAlarmOverlay.BackgroundServices;

/// <summary>The only active hosted service in Milestone 0. No polling or network activity.</summary>
public sealed class DatabaseInitializationService(IDatabaseInitializer initializer,IAuthenticationService authentication) : IHostedService
{
    // Microsoft.Data.Sqlite performs synchronous I/O; keep it off the WPF dispatcher.
    public Task StartAsync(CancellationToken cancellationToken)
        => Task.Run(async () =>
        {
            await initializer.InitializeAsync(cancellationToken);
            await authentication.EnsureDefaultAdministratorAsync(cancellationToken);
        }, cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

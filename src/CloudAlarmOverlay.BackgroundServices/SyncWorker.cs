using CloudAlarmOverlay.Core.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
namespace CloudAlarmOverlay.BackgroundServices;

public sealed class SyncWorker(ISyncService sync, SyncConfiguration configuration, ILogger<SyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var started = System.Diagnostics.Stopwatch.StartNew();
            var interval = 45;
            try
            {
                await sync.SyncAsync(stoppingToken);
                interval = (await configuration.LoadAsync(stoppingToken)).IntervalSeconds;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "背景同步失敗"); }
            var remaining = TimeSpan.FromSeconds(Math.Clamp(interval, 30, 60)) - started.Elapsed;
            await Task.Delay(remaining > TimeSpan.Zero ? remaining : TimeSpan.FromSeconds(1), stoppingToken);
        }
    }
}

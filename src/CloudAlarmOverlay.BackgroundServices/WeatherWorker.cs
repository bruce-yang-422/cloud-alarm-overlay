using CloudAlarmOverlay.Core.Services;
using Microsoft.Extensions.Hosting;
namespace CloudAlarmOverlay.BackgroundServices;
public sealed class WeatherWorker(IWeatherService weather, RuntimeState runtime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var resumed = runtime.ResumedAt;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var current = runtime.ResumedAt;
                await weather.RefreshAsync(current != resumed, stoppingToken);
                resumed = current;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch { /* Retry local settings failures without altering system health. */ }
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }
}

using CloudAlarmOverlay.Core.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CloudAlarmOverlay.BackgroundServices;

public sealed class CalendarDataWorker(CalendarDataService calendar,ILogger<CalendarDataWorker> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while(!stoppingToken.IsCancellationRequested)
        {
            try{await calendar.UpdateAsync(false,stoppingToken);}
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception ex){logger.LogError(ex,"日曆資料更新失敗");}
            await Task.Delay(TimeSpan.FromMinutes(1),stoppingToken);
        }
    }
}

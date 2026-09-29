using CloudAlarmOverlay.Core.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
namespace CloudAlarmOverlay.BackgroundServices;

public sealed class GoogleWorkspaceWorker(IGoogleWorkspace workspace,ILogger<GoogleWorkspaceWorker> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while(!stoppingToken.IsCancellationRequested)
        {
            try{await workspace.SyncAsync(automatic:true,ct:stoppingToken);}
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception){logger.LogWarning("Google 背景同步暫不可用，將於下次檢查重試。");}
            await Task.Delay(TimeSpan.FromMinutes(1),stoppingToken);
        }
    }
}

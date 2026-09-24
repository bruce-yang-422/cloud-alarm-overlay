using System.Globalization;
using System.IO;
using CloudAlarmOverlay.Core.Repositories;
using CloudAlarmOverlay.Core.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CloudAlarmOverlay.App.Services;

public sealed class RuntimeLogCleanup(IAppPaths paths, ISettingsRepository settings, TimeProvider clock)
{
    public async Task<int> CleanupAsync(CancellationToken ct = default)
    {
        var days = LogRetentionPolicy.Read((await settings.GetAsync(LogRetentionPolicy.Key, ct))?.Value);
        var cutoff = DateOnly.FromDateTime(clock.GetLocalNow().DateTime).AddDays(1 - days);
        var directory = new DirectoryInfo(Path.Combine(paths.DataDirectory, "logs"));
        if (!directory.Exists || (directory.Attributes & FileAttributes.ReparsePoint) != 0 ||
            (new DirectoryInfo(paths.DataDirectory).Attributes & FileAttributes.ReparsePoint) != 0) return 0;
        var deleted = 0;
        foreach (var file in directory.EnumerateFiles("runtime-*.log", new EnumerationOptions
        { RecurseSubdirectories = false, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
        {
            ct.ThrowIfCancellationRequested();
            var name = file.Name;
            if (name.Length != 20 || !name.StartsWith("runtime-", StringComparison.Ordinal) ||
                !name.EndsWith(".log", StringComparison.Ordinal) ||
                !DateOnly.TryParseExact(name.AsSpan(8, 8), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) || date >= cutoff) continue;
            try
            {
                file.Refresh();
                if (!file.Exists || (file.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                file.Delete();
                deleted++;
            }
            catch (IOException) { /* A locked file can be retried next hour. */ }
            catch (UnauthorizedAccessException) { }
        }
        return deleted;
    }
}

public sealed class RuntimeLogCleanupWorker(RuntimeLogCleanup cleanup, ILogger<RuntimeLogCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await cleanup.CleanupAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning(ex, "執行階段日誌清理失敗，稍後重試。"); }
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }
}

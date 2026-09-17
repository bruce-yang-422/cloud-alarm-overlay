using CloudAlarmOverlay.Core.Models;

namespace CloudAlarmOverlay.Core.Services;

/// <summary>Contract scaffold. Business implementation follows in a later milestone.</summary>
public interface ISyncService
{
    Task<SyncRunResult> SyncAsync(CancellationToken cancellationToken = default);
}

public sealed record SyncRunResult(IReadOnlyList<SyncLogEntry> Entries, string? SkippedReason = null,
    int DownloadedTaskCount = 0, int IncludedTaskCount = 0);

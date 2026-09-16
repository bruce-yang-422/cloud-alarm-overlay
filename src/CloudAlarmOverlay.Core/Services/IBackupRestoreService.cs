using CloudAlarmOverlay.Core.Models;

namespace CloudAlarmOverlay.Core.Services;

/// <summary>Contract scaffold. Business implementation follows in a later milestone.</summary>
public interface IBackupRestoreService
{
    Task CreateBackupAsync(string path, CancellationToken cancellationToken = default);
    Task RestoreAsync(string path, CancellationToken cancellationToken = default);
    Task CreateBackupAsync(string path, BackupCredentials? administrator, CancellationToken cancellationToken = default);
    Task<BackupPreview> InspectAsync(string path, CancellationToken cancellationToken = default);
    Task<BackupResult> RestoreAsync(string path, bool replaceAdministrator, BackupCredentials? administrator, CancellationToken cancellationToken = default);
}
public sealed record BackupCredentials(string Username, string Password);
public sealed record BackupPreview(string DeviceId, string? DisplayName, int Tasks, int History, bool ContainsAdministrator);
public sealed record BackupResult(int ImportedTasks, int SkippedTasks, int ImportedHistory);

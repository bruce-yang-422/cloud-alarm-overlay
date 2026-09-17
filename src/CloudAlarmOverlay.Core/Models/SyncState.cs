namespace CloudAlarmOverlay.Core.Models;

public sealed record SyncState
{
    public required string Source { get; init; }
    public string? Fingerprint { get; init; }
    public required string ConfigFingerprint { get; init; }
    public required string Status { get; init; }
    public string? Message { get; init; }
    public DateTime LastCheckedAt { get; init; }
    public DateTime? LastSuccessAt { get; init; }
    public long? ActiveLogId { get; init; }
}

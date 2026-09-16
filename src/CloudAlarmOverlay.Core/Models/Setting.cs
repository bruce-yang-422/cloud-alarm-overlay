namespace CloudAlarmOverlay.Core.Models;

public sealed record Setting
{
    public required string Key { get; init; }
    public string? Value { get; init; }
    public bool Locked { get; init; }
}


namespace CloudAlarmOverlay.Core.Models;

public sealed record PomodoroSetting
{
    public required string Key { get; init; }
    public string? Value { get; init; }
}


namespace CloudAlarmOverlay.Core.Models;

public sealed record Employee
{
    public long Id { get; init; }
    public required string DeviceId { get; init; }
    public string? Name { get; init; }
    public string? Department { get; init; }
    public string? MaxAllowedLevel { get; init; }
    // Normalized TEXT: TRUE / FALSE / null; CSV also contains 是 / 否 / -.
    public string? RequireAckOverride { get; init; }
}


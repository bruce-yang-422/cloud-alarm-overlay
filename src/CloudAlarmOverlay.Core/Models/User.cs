namespace CloudAlarmOverlay.Core.Models;

public sealed record User
{
    public long Id { get; init; }
    public required string Username { get; init; }
    public string? DisplayName { get; init; }
    public required string PasswordHash { get; init; }
    public required string Salt { get; init; }
    public bool Enabled { get; init; } = true;
}


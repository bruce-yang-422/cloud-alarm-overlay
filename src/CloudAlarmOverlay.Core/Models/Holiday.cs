namespace CloudAlarmOverlay.Core.Models;

public sealed record Holiday
{
    public long Id { get; init; }
    public required DateOnly Date { get; init; }
    public required string Type { get; init; }
    public string? Note { get; init; }
    public string Source { get; init; } = "Sheet";
}


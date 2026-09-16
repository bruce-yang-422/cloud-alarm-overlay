namespace CloudAlarmOverlay.Core.Models;

public sealed record LunarCalendarEntry
{
    public long Id { get; init; }
    public required DateOnly Date { get; init; }
    public string? LunarDate { get; init; }
    public required int LunarDay { get; init; }
    public string? SolarTerm { get; init; }
}


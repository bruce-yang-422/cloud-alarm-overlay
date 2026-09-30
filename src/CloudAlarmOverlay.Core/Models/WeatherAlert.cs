namespace CloudAlarmOverlay.Core.Models;

public enum WeatherAlertKind { Earthquake, Typhoon, Tsunami, Rain, Thunderstorm, Wind, Heat, Cold, Fog, Other }

public sealed record WeatherAlert(string Id, WeatherAlertKind Kind, string Title, string Summary,
    DateTimeOffset PublishedAt, DateTimeOffset EffectiveAt, DateTimeOffset ExpiresAt)
{
    public Uri? CapUri { get; init; }
    public IReadOnlyList<WeatherAlertArea> Areas { get; init; } = [];
    public string SeverityLabel { get; init; } = "";
}

public sealed record WeatherAlertArea(string Name, IReadOnlyList<string> Codes,
    IReadOnlyList<string> Polygons, IReadOnlyList<string> Circles);

public sealed record WeatherAlertSnapshot(DateTimeOffset CheckedAt, IReadOnlyList<WeatherAlert> Items);

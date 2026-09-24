using CloudAlarmOverlay.Core.Models;
namespace CloudAlarmOverlay.Core.Services;
public interface IWeatherService
{
    WeatherOptions Options { get; }
    WeatherLocation? EffectiveLocation { get; }
    WeatherSnapshot? Snapshot { get; }
    bool IsStale { get; }
    Task LoadAsync(CancellationToken ct = default);
    Task SaveAsync(WeatherOptions options, CancellationToken ct = default);
    Task<IReadOnlyList<WeatherLocation>> SearchAsync(string city, CancellationToken ct = default);
    Task RefreshAsync(bool force = false, CancellationToken ct = default);
}

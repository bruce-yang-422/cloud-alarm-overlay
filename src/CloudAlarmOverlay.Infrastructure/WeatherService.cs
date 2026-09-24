using System.Text.Json;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using CloudAlarmOverlay.Core.Services;
namespace CloudAlarmOverlay.Infrastructure;

public sealed class WeatherService(ISettingsRepository settings, OpenMeteoWeatherClient client, TimeProvider clock) : IWeatherService, IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private CancellationTokenSource requests = new();
    private DateTimeOffset nextUpdate;
    private bool loaded;
    public WeatherOptions Options { get; private set; } = new();
    public WeatherLocation? EffectiveLocation { get; private set; }
    public WeatherSnapshot? Snapshot { get; private set; }
    public bool IsStale { get; private set; } = true;
    private static T? Read<T>(string? json)
    {
        try { return json is null ? default : JsonSerializer.Deserialize<T>(json); }
        catch (JsonException) { return default; }
    }
    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (loaded) return;
        await gate.WaitAsync(ct);
        try
        {
            if (loaded) return;
            Options = Read<WeatherOptions>((await settings.GetAsync("WeatherOptions", ct))?.Value) ?? new();
            EffectiveLocation = Options.Location ?? Read<WeatherLocation>((await settings.GetAsync("WeatherDefaultLocation", ct))?.Value);
            var cached = Read<WeatherSnapshot>((await settings.GetAsync("WeatherCache", ct))?.Value);
            Snapshot = cached?.Location == EffectiveLocation ? cached : null;
            loaded = true;
        }
        finally { gate.Release(); }
    }
    public async Task SaveAsync(WeatherOptions options, CancellationToken ct = default)
    {
        options.Location?.Validate();
        await settings.SaveAsync(new() { Key = "WeatherOptions", Value = JsonSerializer.Serialize(options) }, ct);
        // Cancel outstanding HTTP before waiting for the refresh lock.
        requests.Cancel();
        await gate.WaitAsync(ct);
        try
        {
            requests.Dispose(); requests = new(); Options = options;
            EffectiveLocation = options.Location ?? Read<WeatherLocation>((await settings.GetAsync("WeatherDefaultLocation", ct))?.Value);
            if (Snapshot?.Location != EffectiveLocation) Snapshot = null;
            nextUpdate = default; IsStale = true; loaded = true;
        }
        finally { gate.Release(); }
    }
    public async Task<IReadOnlyList<WeatherLocation>> SearchAsync(string city, CancellationToken ct = default)
    {
        await LoadAsync(ct);
        if (!Options.Enabled) throw new InvalidOperationException("請先開啟並儲存天氣，再搜尋地點。");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, requests.Token);
        return await client.SearchAsync(city, linked.Token);
    }
    public async Task RefreshAsync(bool force = false, CancellationToken ct = default)
    {
        await LoadAsync(ct);
        await gate.WaitAsync(ct);
        try
        {
            if (!Options.Enabled || EffectiveLocation is not {} location || (!force && clock.GetUtcNow() < nextUpdate)) return;
            nextUpdate = clock.GetUtcNow().AddMinutes(30);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, requests.Token);
            try
            {
                var snapshot = await client.FetchAsync(location, clock.GetUtcNow(), linked.Token);
                linked.Token.ThrowIfCancellationRequested();
                await settings.SaveAsync(new() { Key = "WeatherCache", Value = JsonSerializer.Serialize(snapshot) }, linked.Token);
                Snapshot = snapshot; IsStale = false;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { IsStale = true; } // Weather never affects scheduling or sync health.
        }
        finally { gate.Release(); }
    }
    public void Dispose() { requests.Cancel(); requests.Dispose(); gate.Dispose(); }
}

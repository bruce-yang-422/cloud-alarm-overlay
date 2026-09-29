using System.Text.Json;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Services;

namespace CloudAlarmOverlay.Data.Repositories;

internal sealed class CalendarDataStore(Database db):ICalendarDataStore
{
    private sealed record Cached(string Json,CalendarDataState State);
    private Cached? cached;
    public async Task<CalendarDataState> ReadAsync(CancellationToken ct=default)
    {
        var json=(await db.QueryAsync<string>("SELECT Value FROM Settings WHERE Key=@key;",new{key=CalendarDataState.Key},ct)).SingleOrDefault();
        if(json is null)return new();
        var current=Volatile.Read(ref cached);if(current?.Json==json)return current.State;
        var state=JsonSerializer.Deserialize<CalendarDataState>(json,CalendarJson.Options)??throw new FormatException("日曆快取無效。");
        Volatile.Write(ref cached,new(json,state));return state;
    }
    public async Task SaveAsync(CalendarDataState state,CancellationToken ct=default)
    {
        var json=JsonSerializer.Serialize(state,CalendarJson.Options);
        await db.ExecuteAsync("INSERT INTO Settings(Key,Value,Locked) VALUES(@key,@json,1) ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value,Locked=1;",new{key=CalendarDataState.Key,json},ct);
        Volatile.Write(ref cached,new(json,state));
    }
}

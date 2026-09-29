using CloudAlarmOverlay.Core.Models;

namespace CloudAlarmOverlay.Core.Services;

public interface ICalendarDataStore
{
    Task<CalendarDataState> ReadAsync(CancellationToken ct=default);
    Task SaveAsync(CalendarDataState state,CancellationToken ct=default);
}
public interface ICalendarDataSource
{
    Task<string> DownloadAsync(string url,CancellationToken ct=default);
}
public sealed class CalendarDataService(ICalendarDataStore store,ICalendarDataSource source,AdminSession session,TimeProvider clock,ChangeSignal changes)
{
    private readonly SemaphoreSlim gate=new(1,1);
    private bool initialized;
    public DateTimeOffset Now=>clock.GetLocalNow();
    public Task<CalendarDataState> GetAsync(CancellationToken ct=default)=>store.ReadAsync(ct);
    public async Task InitializeAsync(CancellationToken ct=default)
    {
        await gate.WaitAsync(ct);
        try{await Initialize(ct);}finally{gate.Release();}
    }
    private async Task Initialize(CancellationToken ct)
    {
        if(initialized)return;
        var state=await store.ReadAsync(ct);
        if(state.Source=="BuiltIn"||state.Holidays is null||state.Lunar is null)
        {
            async Task<string> Read(string kind)
            {
                using var stream=typeof(CalendarDataService).Assembly.GetManifestResourceStream($"CloudAlarmOverlay.Core.Data.Calendar.SheetA_{kind}.json")??throw new InvalidOperationException("安裝包缺少內建日曆 JSON。");
                using var reader=new StreamReader(stream);return await reader.ReadToEndAsync(ct);
            }
            var holidays=CalendarJson.ReadHolidays(await Read("Holidays"));
            var lunar=CalendarJson.ReadLunar(await Read("LunarCalendar"));
            CalendarJson.ValidatePair(holidays,lunar);
            if(state.Holidays?.Version!=holidays.Version||state.Lunar?.Version!=lunar.Version)
                await store.SaveAsync(state with{Holidays=holidays,Lunar=lunar,Source="BuiltIn",AppliedAt=Now},ct);
        }
        initialized=true;
    }
    public async Task SaveOptionsAsync(CalendarUpdateOptions options,CancellationToken ct=default)
    {
        session.RequireAdmin();options.Validate();
        options=options with{HolidaysUrl=CalendarUpdateOptions.NormalizeUrl(options.HolidaysUrl),LunarUrl=CalendarUpdateOptions.NormalizeUrl(options.LunarUrl)};
        await gate.WaitAsync(ct);
        try
        {
            session.RequireAdmin();await Initialize(ct);var state=await store.ReadAsync(ct);
            var sourceChanged=state.Options.HolidaysUrl!=options.HolidaysUrl||state.Options.LunarUrl!=options.LunarUrl;
            await store.SaveAsync(state with{Options=options,LastAttemptAt=sourceChanged?null:state.LastAttemptAt},ct);
        }
        finally{gate.Release();}
    }
    public async Task<CalendarDataState> UpdateAsync(bool manual,CancellationToken ct=default)
    {
        if(manual)session.RequireAdmin();
        await gate.WaitAsync(ct);
        try
        {
            if(manual)session.RequireAdmin();await Initialize(ct);var state=await store.ReadAsync(ct);
            if(!state.Options.Enabled)
            {
                if(manual)throw new InvalidOperationException("請先開啟並儲存「允許網路更新」。");
                return state;
            }
            if(!manual&&!state.Options.IsDue(Now,state.LastAttemptAt))return state;
            state.Options.Validate();
            // Persist the attempt before I/O, so crashes and failures cannot trigger a polling storm.
            state=state with{LastAttemptAt=Now,LastResult="更新中；若中斷則保留原資料"};
            await store.SaveAsync(state,ct);
            CalendarDataState updated;
            try
            {
                var downloads=await Task.WhenAll(source.DownloadAsync(state.Options.HolidaysUrl,ct),source.DownloadAsync(state.Options.LunarUrl,ct));
                var holidays=CalendarJson.ReadHolidays(downloads[0]);var lunar=CalendarJson.ReadLunar(downloads[1]);CalendarJson.ValidatePair(holidays,lunar);
                updated=state with{Holidays=holidays,Lunar=lunar,Source="GitHub",AppliedAt=Now,LastSuccessAt=Now,LastResult="更新成功"};
            }
            catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
            catch(Exception ex)
            {
                updated=state with{LastResult=ex switch
                {
                    HttpRequestException h=>$"下載失敗（{h.StatusCode?.ToString()??"連線錯誤"}），保留原資料。",
                    OperationCanceledException=>"下載逾時，保留原資料。",
                    FormatException=>ex.Message,
                    _=>"更新失敗，保留原資料。"
                }};
            }
            await store.SaveAsync(updated,ct);changes.Notify();return updated;
        }
        finally{gate.Release();}
    }
}

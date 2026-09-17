using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
namespace CloudAlarmOverlay.Core.Services;
internal sealed class SyncService(SyncConfiguration config,ISheetCsvClient client,ICsvSheetParser parser,
    ITaskRepository tasks,IEmployeeRepository employees,IHolidayRepository holidays,ILunarCalendarRepository lunar,
    IDeviceIdentityService identity,IAudienceFilterService audience,ISyncLogRepository logs,ChangeSignal changes) : ISyncService,IDisposable
{
    private readonly SemaphoreSlim gate=new(1,1);
    public async Task<SyncRunResult> SyncAsync(CancellationToken cancellationToken=default)
    {
        await gate.WaitAsync(cancellationToken);
        var requests=new Dictionary<string,Task<string>>();
        try
        {
            var device=await identity.GetLocalAsync(cancellationToken);
            if(device is null)return new([],"尚未設定這台電腦的裝置 ID。");
            var options=await config.LoadAsync(cancellationToken);
            options.Validate();
            if(options.SheetAId==""&&options.SheetBId=="")return new([],"尚未儲存任何同步來源。");
            if(options.SheetAId!="")
            {
                requests["SheetA/Employees"]=client.DownloadAsync(options.SheetAId,options.EmployeesGid,cancellationToken);
                requests["SheetA/Holidays"]=client.DownloadAsync(options.SheetAId,options.HolidaysGid,cancellationToken);
                requests["SheetA/LunarCalendar"]=client.DownloadAsync(options.SheetAId,options.LunarGid,cancellationToken);
                requests["SheetA/Tasks"]=client.DownloadAsync(options.SheetAId,options.TasksAGid,cancellationToken);
            }
            if(options.SheetBId!="")requests["SheetB/Tasks"]=client.DownloadAsync(options.SheetBId,options.TasksBGid,cancellationToken);
            IReadOnlyList<Employee> currentEmployees=[];
            var results=new List<SyncLogEntry>();
            var downloadedTaskCount=0;
            var includedTaskCount=0;
            async Task Tab<T>(string source,Func<string,IReadOnlyList<T>> parse,Func<IReadOnlyList<T>,Task<int>> save)
            {
                SyncLogEntry entry;
                try
                {
                    var rows=parse(await requests[source]);
                    var cached=await save(rows);
                    if(source.EndsWith("/Tasks",StringComparison.Ordinal))
                    {
                        downloadedTaskCount+=rows.Count;
                        includedTaskCount+=cached;
                    }
                    entry=new SyncLogEntry{Time=DateTime.Now,Source=source,Status="成功",RecordCount=rows.Count,
                        Message=source.EndsWith("/Tasks",StringComparison.Ordinal)?$"已下載 {rows.Count} 筆，符合本機 {cached} 筆。":"已更新本機快取"};
                }
                catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested){throw;}
                catch(Exception ex)
                {
                    entry=new SyncLogEntry{Time=DateTime.Now,Source=source,Status="失敗",Message=ex.Message};
                }
                await logs.AppendAsync(entry,cancellationToken);
                results.Add(entry);
            }
            if(options.SheetAId!="")
            {
                await Tab("SheetA/Employees",parser.ParseEmployees,async rows=>{
                    await employees.ReplaceCacheAsync(rows,cancellationToken);currentEmployees=rows;return rows.Count;});
                await Tab("SheetA/Holidays",parser.ParseHolidays,async rows=>{await holidays.ReplaceCacheAsync(rows,cancellationToken);return rows.Count;});
                await Tab("SheetA/LunarCalendar",parser.ParseLunarCalendar,async rows=>{await lunar.ReplaceCacheAsync(rows,cancellationToken);return rows.Count;});
                await Tab("SheetA/Tasks",text=>parser.ParseTasks(text,TaskSources.SheetA),async rows=>{
                    var included=rows.Where(t=>audience.IsIncluded(t,device,currentEmployees)).ToArray();
                    await tasks.ReplaceCloudCacheAsync(TaskSources.SheetA,included,cancellationToken);return included.Length;});
            }
            if(options.SheetBId!="")
                await Tab("SheetB/Tasks",text=>parser.ParseTasks(text,TaskSources.SheetB),async rows=>{
                    var included=rows.Where(t=>audience.IsIncluded(t,device,currentEmployees)).ToArray();
                    await tasks.ReplaceCloudCacheAsync(TaskSources.SheetB,included,cancellationToken);return included.Length;});
            changes.Notify();
            return new(results,null,downloadedTaskCount,includedTaskCount);
        }
        finally
        {
            // Observe every parallel download, including when shutdown cancels parsing early.
            try{await Task.WhenAll(requests.Values);}catch(Exception){/* Individual tab failures are logged above. */}
            gate.Release();
        }
    }
    public void Dispose()=>gate.Dispose();
}

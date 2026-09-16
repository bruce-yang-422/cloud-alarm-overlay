using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
namespace CloudAlarmOverlay.Core.Services;
internal sealed class SyncService(SyncConfiguration config,ISheetCsvClient client,ICsvSheetParser parser,
    ITaskRepository tasks,IEmployeeRepository employees,IHolidayRepository holidays,ILunarCalendarRepository lunar,
    IDeviceIdentityService identity,IAudienceFilterService audience,ISyncLogRepository logs,ChangeSignal changes) : ISyncService,IDisposable
{
    private readonly SemaphoreSlim gate=new(1,1);
    public async Task SyncAsync(CancellationToken cancellationToken=default)
    {
        if(!await gate.WaitAsync(0,cancellationToken))return;
        var requests=new Dictionary<string,Task<string>>();
        try
        {
            var device=await identity.GetLocalAsync(cancellationToken);
            if(device is null)return;
            var options=await config.LoadAsync(cancellationToken);
            options.Validate();
            if(options.SheetAId!="")
            {
                requests["SheetA/Employees"]=client.DownloadAsync(options.SheetAId,options.EmployeesGid,cancellationToken);
                requests["SheetA/Holidays"]=client.DownloadAsync(options.SheetAId,options.HolidaysGid,cancellationToken);
                requests["SheetA/LunarCalendar"]=client.DownloadAsync(options.SheetAId,options.LunarGid,cancellationToken);
                requests["SheetA/Tasks"]=client.DownloadAsync(options.SheetAId,options.TasksAGid,cancellationToken);
            }
            if(options.SheetBId!="")requests["SheetB/Tasks"]=client.DownloadAsync(options.SheetBId,options.TasksBGid,cancellationToken);
            IReadOnlyList<Employee> currentEmployees=[];
            async Task Tab<T>(string source,string id,string gid,Func<string,IReadOnlyList<T>> parse,Func<IReadOnlyList<T>,Task> save)
            {
                try
                {
                    var rows=parse(await requests[source]);
                    await save(rows);
                    await logs.AppendAsync(new SyncLogEntry{Time=DateTime.Now,Source=source,Status="成功",RecordCount=rows.Count,Message="已更新本機快取"},cancellationToken);
                }
                catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested){throw;}
                catch(Exception ex)
                {
                    await logs.AppendAsync(new SyncLogEntry{Time=DateTime.Now,Source=source,Status="失敗",Message=ex.Message},cancellationToken);
                }
            }
            if(options.SheetAId!="")
            {
                await Tab("SheetA/Employees",options.SheetAId,options.EmployeesGid,parser.ParseEmployees,async rows=>{
                    await employees.ReplaceCacheAsync(rows,cancellationToken);currentEmployees=rows;});
                await Tab("SheetA/Holidays",options.SheetAId,options.HolidaysGid,parser.ParseHolidays,rows=>holidays.ReplaceCacheAsync(rows,cancellationToken));
                await Tab("SheetA/LunarCalendar",options.SheetAId,options.LunarGid,parser.ParseLunarCalendar,rows=>lunar.ReplaceCacheAsync(rows,cancellationToken));
                await Tab("SheetA/Tasks",options.SheetAId,options.TasksAGid,text=>parser.ParseTasks(text,TaskSources.SheetA),
                    rows=>tasks.ReplaceCloudCacheAsync(TaskSources.SheetA,rows.Where(t=>audience.IsIncluded(t,device,currentEmployees)).ToArray(),cancellationToken));
            }
            if(options.SheetBId!="")
                await Tab("SheetB/Tasks",options.SheetBId,options.TasksBGid,text=>parser.ParseTasks(text,TaskSources.SheetB),
                    rows=>tasks.ReplaceCloudCacheAsync(TaskSources.SheetB,rows.Where(t=>audience.IsIncluded(t,device,currentEmployees)).ToArray(),cancellationToken));
            changes.Notify();
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

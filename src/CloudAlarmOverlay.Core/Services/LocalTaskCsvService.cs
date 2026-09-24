using System.Globalization;
using System.Text;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using CsvHelper;
using CsvHelper.Configuration;
namespace CloudAlarmOverlay.Core.Services;

public sealed record TaskImportRow(int RowNumber, string Id, string Title, AlarmTask? Task, string? Error, bool Duplicate, DateTime? NextReminder)
{
    public bool Valid => Error is null;
    public string Status => Error ?? (Duplicate ? "重複編號：可略過或改用新編號" : NextReminder is null ? "有效（尚無可用提醒日期）" : "有效");
}
public sealed record TaskImportResult(int Imported, int Skipped);
public interface ILocalTaskImportStore
{
    Task<bool> InsertAsync(AlarmTask task, CancellationToken ct = default);
}
public sealed class LocalTaskCsvService(ICsvSheetParser parser, ITaskRepository tasks, ITaskSchedulingService schedule,
    ILocalTaskImportStore store, ChangeSignal changes)
{
    private static readonly string[] Headers = ["Id","Time","Title","Description","Level","Enabled","RequireAck","Recurrence","SkipOnHoliday","TargetDeviceOrName","ExcludeDeviceOrName","Note"];
    private static readonly string[] Descriptions = ["編號","提醒時間","名稱","詳細內容","等級","啟用","需要確認","重複規則","假日略過","通知對象（本機忽略）","排除對象（本機忽略）","備註"];
    public static string Decode(byte[] bytes)
    {
        try { return new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); }
        catch (DecoderFallbackException) { throw new FormatException("檔案不是有效的 UTF-8。請在 Excel 另存為「CSV UTF-8（逗號分隔）」後再匯入（不支援 Big5）。"); }
    }
    private static string Write(IEnumerable<IEnumerable<string?>> rows)
    {
        using var text = new StringWriter(CultureInfo.InvariantCulture);
        using var csv = new CsvWriter(text, CultureInfo.InvariantCulture);
        foreach (var row in rows) { foreach (var value in row) csv.WriteField(value); csv.NextRecord(); }
        return text.ToString();
    }
    public static string Export(IEnumerable<AlarmTask> tasks) => Write(new[] { Headers, Descriptions }.Concat(tasks.Where(t=>t.Source==TaskSources.Local).Select(t => new string?[] {
        t.Id,t.ScheduledAt.ToString("yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture),t.Title,t.Description,t.Level,t.Enabled?"TRUE":"FALSE",
        t.RequireAcknowledgement?"TRUE":"FALSE",t.Recurrence,t.SkipOnHoliday?"TRUE":"FALSE","","",t.Note })));
    public static string Template(DateTime now) => Export(new[] {
        new AlarmTask {Id="sample-1",Title="繳費提醒",ScheduledAt=now.Date.AddDays(7).AddHours(9),CreatedAt=now,UpdatedAt=now},
        new AlarmTask {Id="sample-2",Title="每月報表",ScheduledAt=now.Date.AddDays(1).AddHours(10),Recurrence="Monthly:10",CreatedAt=now,UpdatedAt=now}
    });
    public async Task<IReadOnlyList<TaskImportRow>> PreviewAsync(string text, CancellationToken ct = default)
    {
        using var input = new StringReader(text.TrimStart('\uFEFF'));
        using var csv = new CsvReader(input, new CsvConfiguration(CultureInfo.InvariantCulture) { IgnoreBlankLines=false });
        if(!csv.Read()) throw new FormatException("CSV 缺少標題列。");
        csv.ReadHeader(); var headers=csv.HeaderRecord!;
        if(headers.Distinct().Count()!=headers.Length) throw new FormatException("CSV 標題重複。");
        foreach(var key in new[]{"Id","Time","Title","Enabled"}) if(!headers.Contains(key)) throw new FormatException("CSV 缺少必要欄位："+key);
        if(!csv.Read())throw new FormatException("CSV 缺少第二列中文說明。");
        var description=headers.Select((_,i)=>csv.GetField(i)??"").ToArray();
        var descriptionTime=description[Array.IndexOf(headers,"Time")];
        var descriptionEnabled=description[Array.IndexOf(headers,"Enabled")].Trim().ToUpperInvariant();
        if(DateTime.TryParse(descriptionTime,CultureInfo.InvariantCulture,DateTimeStyles.None,out _) || descriptionEnabled is "TRUE" or "FALSE" or "是" or "否")
            throw new FormatException("第二列必須為中文欄位說明，請使用下載範本，避免略過第一筆任務。");
        var seen=(await tasks.GetAllAsync(ct)).Select(t=>t.Id).ToHashSet(StringComparer.Ordinal);
        var result=new List<TaskImportRow>();
        while(csv.Read())
        {
            ct.ThrowIfCancellationRequested();
            var values=csv.Parser.Record!.ToArray();
            if(values.All(string.IsNullOrWhiteSpace))continue;
            string Field(string name) => Array.IndexOf(headers,name) is var index && index>=0 && index<values.Length ? values[index].Trim() : "";
            var id=Field("Id"); var title=Field("Title"); var rowNumber=csv.Context.Parser!.Row;
            if(result.Count>=10000)throw new FormatException("單次最多匯入 10,000 筆任務。");
            try
            {
                if(values.Length!=headers.Length)throw new FormatException("欄位數量與標題列不符，含逗號的內容需用雙引號包住。");
                if(string.IsNullOrWhiteSpace(id))throw new FormatException("編號不可空白。");
                var task=parser.ParseTasks(Write(new[]{headers,description,values}),TaskSources.SheetA).Single() with {
                    Id=id,ExternalId=null,Source=TaskSources.Local,TargetDeviceOrName=null,ExcludeDeviceOrName=null,Note=Field("Note")};
                Validate(task,DateTime.Now);
                var next=await schedule.GetNextOccurrenceAsync(task with {Enabled=true},DateTime.Now,ct);
                result.Add(new(rowNumber,id,title,task,null,!seen.Add(id),next));
            }
            catch(Exception ex) when(ex is FormatException or ArgumentException or InvalidOperationException)
            { result.Add(new(rowNumber,id,title,null,ex.Message,false,null)); }
        }
        return result;
    }
    public static void Validate(AlarmTask task,DateTime now)
    {
        if(task.Source!=TaskSources.Local || task.Level is not (AlarmLevels.Low or AlarmLevels.Mid or AlarmLevels.High))
            throw new ArgumentException("本機任務最高可選緊急提醒；不可匯入強制通知。");
        if(string.IsNullOrWhiteSpace(task.Title)||task.Title.Length>50||task.Description?.Length>1000||task.Note?.Length>500)
            throw new ArgumentException("名稱限 1–50 字、內容限 1,000 字、備註限 500 字。");
        Recurrence.RecurrenceRule.Validate(task.Recurrence);
        if(task.Recurrence=="None" && task.ScheduledAt<=now)throw new ArgumentException("單次任務時間已過，請修改時間後再匯入。");
    }
    public async Task<TaskImportResult> ImportAsync(IEnumerable<TaskImportRow> selected,bool newIds,CancellationToken ct=default)
    {
        var imported=0;var skipped=0;
        foreach(var row in selected)
        {
            if(!row.Valid||row.Task is not {} task){skipped++;continue;}
            try { Validate(task,DateTime.Now); } catch(ArgumentException) {skipped++;continue;}
            task=task with {CreatedAt=DateTime.Now,UpdatedAt=DateTime.Now,IsTriggered=false,RequireAcknowledgement=task.Level!=AlarmLevels.Low};
            if(await store.InsertAsync(task,ct)){imported++;continue;}
            if(newIds && await store.InsertAsync(task with {Id=Guid.NewGuid().ToString("N")},ct))imported++;
            else skipped++;
        }
        changes.Notify();return new(imported,skipped);
    }
}

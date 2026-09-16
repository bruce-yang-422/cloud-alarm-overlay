using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Recurrence;
using CloudAlarmOverlay.Core.Services;
using CsvHelper;
using Dapper;
using Microsoft.Data.Sqlite;

namespace CloudAlarmOverlay.Data;

public sealed class BackupRestoreService(Database db, IAuthenticationService authentication, AdminSession session, ChangeSignal changes) : IBackupRestoreService
{
    private const long MaxArchiveBytes = 64 * 1024 * 1024;
    private static readonly HashSet<string> AllowedSettings = ["KeepWindowAspectRatio", "FlashMilliseconds", "QuietPeriods", "EmojiLibrary", "ThemeMode", "NotificationColorMode", "NotificationColorScheme", .. new[] { AlarmLevels.Low, AlarmLevels.Mid, AlarmLevels.High, AlarmLevels.Max, "低級", "中級", "高級", "最高級" }.Select(x => "Sound:" + x)];
    public Task CreateBackupAsync(string path, CancellationToken cancellationToken = default) => CreateBackupAsync(path, null, cancellationToken);
    public async Task RestoreAsync(string path, CancellationToken cancellationToken = default) => await RestoreAsync(path, false, null, cancellationToken);
    private async Task VerifyAsync(BackupCredentials? credentials, CancellationToken ct)
    {
        if (credentials is null || !await authentication.AuthenticateAsync(credentials.Username, credentials.Password, ct))
            throw new UnauthorizedAccessException("請輸入正確的目前管理者帳號與密碼。");
    }
    public async Task CreateBackupAsync(string path, BackupCredentials? administrator, CancellationToken cancellationToken = default)
    {
        if(!string.Equals(Path.GetExtension(path),".calbak",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("備份檔副檔名必須為 .calbak。");
        if(administrator is not null) await VerifyAsync(administrator, cancellationToken);
        await using var c = await db.OpenAsync(cancellationToken);
        using var tx = c.BeginTransaction();
        async Task<List<T>> Read<T>(string sql) => (await c.QueryAsync<T>(new CommandDefinition(sql, transaction: tx, cancellationToken: cancellationToken))).AsList();
        var identity = (await Read<Device>("SELECT * FROM Devices;")).SingleOrDefault() ?? throw new InvalidOperationException("請先設定裝置身分。");
        var settings = (await Read<Setting>("SELECT * FROM Settings WHERE Locked=0;")).Where(s => AllowedSettings.Contains(s.Key)).ToList();
        var tasks = await Read<AlarmTask>("SELECT * FROM Tasks WHERE Source='本機';");
        var logs = await Read<AcknowledgementLog>("SELECT * FROM AcknowledgementLogs;");
        var users = administrator is null ? null : await Read<User>("SELECT * FROM Users;");
        var occurrences = await Read<SavedOccurrence>("SELECT * FROM Occurrences WHERE TaskId IN (SELECT Id FROM Tasks WHERE Source='本機');");
        var pomodoroSettings = await Read<PomodoroSetting>("SELECT * FROM PomodoroSettings;");
        var pomodoroLogs = await Read<PomodoroLogEntry>("SELECT * FROM PomodoroLog;");
        tx.Commit();
        var target = Path.GetFullPath(path);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using(var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
            using(var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                await Write(zip,"manifest.json",JsonSerializer.Serialize(new { Format="CloudAlarmOverlay", Version=1 }),cancellationToken);
                await Write(zip,"identity.json",JsonSerializer.Serialize(identity),cancellationToken);
                await Write(zip,"settings.json",JsonSerializer.Serialize(settings),cancellationToken);
                await Write(zip,"tasks.csv",Csv(tasks),cancellationToken);
                await Write(zip,"acklog.csv",Csv(logs),cancellationToken);
                await Write(zip,"occurrences.json",JsonSerializer.Serialize(occurrences),cancellationToken);
                await Write(zip,"pomodoro-settings.json",JsonSerializer.Serialize(pomodoroSettings),cancellationToken);
                await Write(zip,"pomodoro.csv",Csv(pomodoroLogs.Select(l=>l.IsActive?l with{IsActive=false,Result="Interrupted",EndedAt=null,CompletedAt=null}:l)),cancellationToken);
                if(users is not null) await Write(zip,"admin.key",JsonSerializer.Serialize(users),cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary,target,true);
        }
        finally { if(File.Exists(temporary))File.Delete(temporary); }
    }
    private static async Task Write(ZipArchive zip,string name,string value,CancellationToken ct)
    {
        await using var writer = new StreamWriter(zip.CreateEntry(name,CompressionLevel.Optimal).Open(),new UTF8Encoding(false));
        await writer.WriteAsync(value.AsMemory(),ct);
    }
    private static string Csv<T>(IEnumerable<T> values)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        using var csv = new CsvWriter(writer,CultureInfo.InvariantCulture);
        csv.Context.TypeConverterOptionsCache.GetOptions<DateTime>().Formats = ["O"];
        csv.Context.TypeConverterOptionsCache.GetOptions<DateTime?>().Formats = ["O"];
        csv.WriteRecords(values); return writer.ToString();
    }
    private static List<T> ReadCsv<T>(string text)
    {
        using var reader = new StringReader(text);
        using var csv = new CsvReader(reader,CultureInfo.InvariantCulture);
        return csv.GetRecords<T>().ToList();
    }
    private sealed record Package(Device Identity,List<Setting> Settings,List<AlarmTask> Tasks,List<AcknowledgementLog> Logs,List<User>? Users,List<SavedOccurrence> Occurrences,List<PomodoroSetting> PomodoroSettings,List<PomodoroLogEntry> PomodoroLogs);
    public sealed record SavedOccurrence(string Id,string TaskId,DateTime ScheduledAt,string State,DateTime? TriggeredAt) { public SavedOccurrence():this("","",default,"",null) {} }
    private static async Task<Package> ReadPackage(string path,CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        if(stream.Length>MaxArchiveBytes)throw new InvalidDataException("備份檔過大（上限 64 MB）。");
        using var zip = new ZipArchive(stream,ZipArchiveMode.Read);
        if(zip.Entries.Count>9 || zip.Entries.Sum(e=>e.Length)>MaxArchiveBytes || zip.Entries.Select(e=>e.FullName).Distinct().Count()!=zip.Entries.Count)
            throw new InvalidDataException("備份檔項目重複或解壓縮後超過 64 MB。");
        string[] allowed = ["manifest.json","identity.json","settings.json","tasks.csv","acklog.csv","admin.key","occurrences.json","pomodoro-settings.json","pomodoro.csv"];
        if(zip.Entries.Any(e=>!allowed.Contains(e.FullName)))throw new InvalidDataException("備份檔含有不支援的項目。");
        async Task<string> Read(string name)
        {
            var entry=zip.GetEntry(name)??throw new InvalidDataException("備份缺少 "+name);
            using var reader=new StreamReader(entry.Open()); return await reader.ReadToEndAsync(ct);
        }
        T Parse<T>(string json)=>JsonSerializer.Deserialize<T>(json)??throw new InvalidDataException("備份資料不可為空。");
        using var manifest=JsonDocument.Parse(await Read("manifest.json"));
        if(manifest.RootElement.GetProperty("Format").GetString()!="CloudAlarmOverlay" || manifest.RootElement.GetProperty("Version").GetInt32()!=1)
            throw new InvalidDataException("不支援的備份格式版本。");
        var package=new Package(Parse<Device>(await Read("identity.json")),Parse<List<Setting>>(await Read("settings.json")),ReadCsv<AlarmTask>(await Read("tasks.csv")),ReadCsv<AcknowledgementLog>(await Read("acklog.csv")),zip.GetEntry("admin.key") is null?null:Parse<List<User>>(await Read("admin.key")),zip.GetEntry("occurrences.json") is null?[]:Parse<List<SavedOccurrence>>(await Read("occurrences.json")),zip.GetEntry("pomodoro-settings.json") is null?[]:Parse<List<PomodoroSetting>>(await Read("pomodoro-settings.json")),zip.GetEntry("pomodoro.csv") is null?[]:ReadCsv<PomodoroLogEntry>(await Read("pomodoro.csv")));
        if(string.IsNullOrWhiteSpace(package.Identity.DeviceId)||string.IsNullOrWhiteSpace(package.Identity.DisplayName))throw new InvalidDataException("備份裝置身分不完整。");
        foreach(var setting in package.Settings)
        {
            if(!AllowedSettings.Contains(setting.Key)||setting.Locked)throw new InvalidDataException("備份不能包含政策或同步來源。");
            NotificationPreferences.Validate(setting);
            if(setting.Key=="KeepWindowAspectRatio" && !bool.TryParse(setting.Value,out _))throw new InvalidDataException("視窗設定無效。");
            if(setting.Key=="ThemeMode" && setting.Value is not ("淺色" or "深色" or "跟隨系統"))throw new InvalidDataException("主題設定無效。");
            if(setting.Key.StartsWith("Sound:",StringComparison.Ordinal)) _=JsonSerializer.Deserialize<SoundPreference>(setting.Value??"{}");
            if(setting.Key=="EmojiLibrary")
            {
                var items=(setting.Value??"").Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
                if(items.Length>60||items.Any(i=>i.Length>24))throw new InvalidDataException("常用 emoji 設定超出範圍。");
            }
        }
        foreach(var task in package.Tasks)
        {
            if(task.Source!=TaskSources.Local || string.IsNullOrWhiteSpace(task.Id)||string.IsNullOrWhiteSpace(task.Title)||task.Title.Length>50 || !new[] { AlarmLevels.Low, AlarmLevels.Mid, AlarmLevels.High, AlarmLevels.Max }.Contains(AlarmLevels.Normalize(task.Level)))throw new InvalidDataException("備份含無效本機任務。");
            RecurrenceRule.Validate(task.Recurrence);
        }
        foreach(var log in package.Logs)
            if(string.IsNullOrWhiteSpace(log.Id)||string.IsNullOrWhiteSpace(log.TaskId)||string.IsNullOrWhiteSpace(log.DeviceId)||log.Result is not ("Pending" or "Acknowledged" or "Overdue_Acknowledged" or "Overdue_Unacked" or "NotLaunched"))throw new InvalidDataException("歷史紀錄格式無效。");
        foreach(var setting in package.PomodoroSettings)
        {
            if(setting.Key!="Options")throw new InvalidDataException("不支援的番茄鐘設定。");
            (JsonSerializer.Deserialize<PomodoroOptions>(setting.Value??"{}")??new()).Validate();
        }
        foreach(var log in package.PomodoroLogs)
            if(string.IsNullOrWhiteSpace(log.Id)||log.IsActive||log.Result is not ("Completed" or "Interrupted")||log.Type is not ("Focus" or "Break" or "LongBreak"))throw new InvalidDataException("番茄鐘紀錄格式無效。");
        if(package.Tasks.Select(t=>t.Id).Distinct().Count()!=package.Tasks.Count || package.Logs.Select(l=>l.Id).Distinct().Count()!=package.Logs.Count)throw new InvalidDataException("備份資料 Id 重複。");
        foreach(var user in package.Users??[])
        {
            var parts=user.PasswordHash.Split(':');
            if(string.IsNullOrWhiteSpace(user.Username)||parts.Length!=3||parts[0]!="PBKDF2-SHA256"||!int.TryParse(parts[1],out var rounds)||rounds is <100000 or >2000000||Convert.FromBase64String(parts[2]).Length!=32||Convert.FromBase64String(user.Salt).Length!=32)throw new InvalidDataException("管理者備份格式無效。");
        }
        if(package.Users is {} users && !users.Any(u=>u.Enabled))throw new InvalidDataException("備份沒有啟用的管理者。");
        return package;
    }
    public async Task<BackupPreview> InspectAsync(string path,CancellationToken cancellationToken=default)
    {
        var p=await ReadPackage(path,cancellationToken);
        return new(p.Identity.DeviceId,p.Identity.DisplayName,p.Tasks.Count,p.Logs.Count,p.Users is not null);
    }
    public async Task<BackupResult> RestoreAsync(string path,bool replaceAdministrator,BackupCredentials? administrator,CancellationToken cancellationToken=default)
    {
        var p=await ReadPackage(path,cancellationToken);
        if(p.Users is not null && !replaceAdministrator)throw new InvalidOperationException("此備份包含管理者帳密，請明確確認是否還原。");
        if(p.Users is not null && await authentication.HasAdministratorAsync(cancellationToken))await VerifyAsync(administrator,cancellationToken);
        await using var c=await db.OpenAsync(cancellationToken);
        using var tx=c.BeginTransaction(deferred:false);
        async Task<int> Run(string sql,object? args=null)=>await c.ExecuteAsync(new CommandDefinition(sql,Database.Parameters(args),tx,cancellationToken:cancellationToken));
        if(await c.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM Occurrences WHERE State IN ('Claimed','Displayed');",transaction:tx)>0)throw new InvalidOperationException("請先完成目前通知，再還原備份。");
        if(await c.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM PomodoroLog WHERE IsActive=1;",transaction:tx)>0)throw new InvalidOperationException("請先結束番茄鐘，再還原備份。");
        var oldIdentity=await c.QuerySingleOrDefaultAsync<Device>("SELECT * FROM Devices LIMIT 1;",transaction:tx);
        if(oldIdentity?.DeviceId!=p.Identity.DeviceId || oldIdentity?.DisplayName!=p.Identity.DisplayName)await Run("DELETE FROM Tasks WHERE Source<>'本機';");
        await Run("DELETE FROM Devices;");
        await Insert(c,tx,"Devices",p.Identity with{Id=1},cancellationToken);
        foreach(var setting in p.Settings)
        {
            var key=setting.Key.StartsWith("Sound:",StringComparison.Ordinal)?"Sound:"+AlarmLevels.Normalize(setting.Key[6..]):setting.Key;
            await Run("INSERT INTO Settings(Key,Value,Locked) VALUES(@Key,@Value,0) ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value WHERE Settings.Locked=0;",setting with {Key=key});
        }
        int tasks=0,logs=0;
        var imported=new HashSet<string>();
        foreach(var task in p.Tasks)if(await Insert(c,tx,"Tasks",task with {Level=AlarmLevels.Normalize(task.Level)},cancellationToken)>0){tasks++;imported.Add(task.Id);}
        foreach(var occurrence in p.Occurrences.Where(o=>imported.Contains(o.TaskId)))
            await Insert(c,tx,"Occurrences",occurrence with { State=occurrence.State is "Claimed" or "Displayed"?"Completed":occurrence.State },cancellationToken);
        foreach(var log in p.Logs)
        {
            logs+=await Insert(c,tx,"AcknowledgementLogs",log with{Result=log.Result=="Pending"?"Overdue_Unacked":log.Result},cancellationToken);
            // Preserve deduplication when importing older archives without occurrences.json.
            if(imported.Contains(log.TaskId)&&log.ScheduledAt is {} at)
                await Insert(c,tx,"Occurrences",new SavedOccurrence(log.TaskId+":"+at.ToString("O"),log.TaskId,at,"Completed",log.TriggeredAt),cancellationToken);
        }
        foreach(var setting in p.PomodoroSettings)await Run("INSERT INTO PomodoroSettings(Key,Value) VALUES(@Key,@Value) ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value;",setting);
        foreach(var log in p.PomodoroLogs)await Insert(c,tx,"PomodoroLog",log with{IsActive=false},cancellationToken);
        if(p.Users is not null)
        {
            await Run("DELETE FROM Users;");
            foreach(var user in p.Users)await Insert(c,tx,"Users",user,cancellationToken);
        }
        await Run("INSERT INTO SystemEvents(Time,EventType,Message) VALUES(@Time,'BackupRestored',@Message);",new{Time=DateTime.Now,Message=$"還原本機任務 {tasks} 筆，歷史 {logs} 筆"});
        cancellationToken.ThrowIfCancellationRequested(); tx.Commit();
        if(p.Users is not null)session.SignOut();
        changes.Notify(); return new(tasks,p.Tasks.Count-tasks,logs);
    }
    private static Task<int> Insert<T>(SqliteConnection c,SqliteTransaction tx,string table,T value,CancellationToken ct)
    {
        // Table and property names originate solely from compiled model types, never archive column names.
        var columns=typeof(T).GetProperties().Where(p=>p.SetMethod is not null).Select(p=>p.Name).ToArray();
        var sql=$"INSERT OR IGNORE INTO {table} ({string.Join(',',columns)}) VALUES ({string.Join(',',columns.Select(n=>'@'+n))});";
        return c.ExecuteAsync(new CommandDefinition(sql,Database.Parameters(value),tx,cancellationToken:ct));
    }
}

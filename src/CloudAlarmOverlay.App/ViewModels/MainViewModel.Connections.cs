using CommunityToolkit.Mvvm.ComponentModel;
using CloudAlarmOverlay.Core.Services;

namespace CloudAlarmOverlay.App.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty,NotifyPropertyChangedFor(nameof(OverallHealth)),NotifyPropertyChangedFor(nameof(HomeAttention)),NotifyPropertyChangedFor(nameof(HasHomeAttention)),NotifyPropertyChangedFor(nameof(HomeAttentionLabel))] private HealthStatus localScheduleHealth=new("等待啟動","尚未檢查","Pending");
    [ObservableProperty,NotifyPropertyChangedFor(nameof(OverallHealth)),NotifyPropertyChangedFor(nameof(CloudHealth))] private HealthStatus sheetAHealth=new("未設定","尚未檢查","Pending");
    [ObservableProperty,NotifyPropertyChangedFor(nameof(OverallHealth)),NotifyPropertyChangedFor(nameof(CloudHealth))] private HealthStatus sheetBHealth=new("未設定","尚未檢查","Pending");
    [ObservableProperty,NotifyPropertyChangedFor(nameof(OverallHealth)),NotifyPropertyChangedFor(nameof(CloudHealth))] private bool hasSheetASource;
    [ObservableProperty,NotifyPropertyChangedFor(nameof(OverallHealth)),NotifyPropertyChangedFor(nameof(CloudHealth))] private bool hasSheetBSource;
    [ObservableProperty,NotifyPropertyChangedFor(nameof(OverallHealth)),NotifyPropertyChangedFor(nameof(CloudHealth))] private HealthStatus googleHealth=new("未新增","尚未新增 Google 帳號。","Inactive");
    [ObservableProperty,NotifyPropertyChangedFor(nameof(HomeAttention)),NotifyPropertyChangedFor(nameof(HasHomeAttention)),NotifyPropertyChangedFor(nameof(HomeAttentionLabel))] private IReadOnlyList<ConnectionGroup> connectionGroups=[];
    [ObservableProperty] private string connectionSummary="尚未新增同步來源";
    private IEnumerable<ConnectionRow> AttentionRows => ConnectionGroups.SelectMany(g=>g.Sources.Select(s=>s with{Name=g.Name+" · "+s.Name}))
        .Prepend(new ConnectionRow("本機排程","本機",LocalScheduleHealth))
        .Where(s=>s.Health.Severity is "Error" or "Stopped");
    public IReadOnlyList<ConnectionRow> HomeAttention=>AttentionRows.Take(3).ToArray();
    public bool HasHomeAttention=>AttentionRows.Any();
    public string HomeAttentionLabel=>$"需要處理 · {AttentionRows.Count()} 項";
    private IEnumerable<HealthStatus> CloudStates()
    {
        if(HasSheetASource)yield return SheetAHealth with{Detail="Sheet A："+SheetAHealth.Label+"\n"+SheetAHealth.Detail};
        if(HasSheetBSource)yield return SheetBHealth with{Detail="Sheet B："+SheetBHealth.Label+"\n"+SheetBHealth.Detail};
        yield return GoogleHealth;
    }
    public HealthStatus CloudHealth=>ConnectionPresentation.Aggregate(CloudStates());
    public HealthStatus OverallHealth=>ConnectionPresentation.Aggregate(CloudStates().Prepend(LocalScheduleHealth with{Detail="本機排程："+LocalScheduleHealth.Label+"\n"+LocalScheduleHealth.Detail}));
    public async Task RefreshHealthAsync()
    {
        var now=DateTime.Now;LocalScheduleHealth=heartbeat.GetStatus(now);
        var groups=new List<ConnectionGroup>();
        try
        {
            var options=await configuration.LoadAsync();var device=await identity.GetLocalAsync();var states=await syncLogs.GetStatesAsync();
            HasSheetASource=!string.IsNullOrWhiteSpace(options.SheetAId);HasSheetBSource=!string.IsNullOrWhiteSpace(options.SheetBId);
            SheetAHealth=SyncHealth.ForSheet("SheetA",options,device,states,now);SheetBHealth=SyncHealth.ForSheet("SheetB",options,device,states,now);
        }
        catch(Exception){HasSheetASource=HasSheetBSource=true;SheetAHealth=SheetBHealth=new("異常","無法讀取公開來源同步狀態。","Error");}
        var shared=new List<ConnectionRow>();
        if(HasSheetASource)shared.Add(new("Sheet A","公開試算表",SheetAHealth));
        if(HasSheetBSource)shared.Add(new("Sheet B","公開試算表",SheetBHealth));
        if(shared.Count>0)groups.Add(new("共用來源","由管理者設定的公開試算表",shared));
        try
        {
            var snapshot=await googleWorkspace.GetAsync();var googleGroups=ConnectionPresentation.GoogleGroups(snapshot,DateTimeOffset.Now);
            GoogleHealth=ConnectionPresentation.Aggregate(googleGroups.SelectMany(g=>g.Sources).Select(s=>s.Health));
            groups.AddRange(googleGroups);
            var count=shared.Count+snapshot.Sources.Count;
            ConnectionSummary=snapshot.Accounts.Count>0?$"{snapshot.Accounts.Count} 個 Google 帳號 · {count} 個來源":count>0?$"{count} 個共用來源":"尚未新增同步來源";
        }
        catch(Exception)
        {
            GoogleHealth=new("無法讀取","Google 連線資料無法讀取，請到設定查看。","Error");
            groups.Add(new("Google 帳號","連線資料暫時無法讀取",[new("Google 連線","Google",GoogleHealth)]));ConnectionSummary="Google 連線需要處理";
        }
        ConnectionGroups=groups;
        SheetAStatus=SheetAHealth.Label+" · "+SheetAHealth.Detail;SheetBStatus=SheetBHealth.Label+" · "+SheetBHealth.Detail;
    }
}

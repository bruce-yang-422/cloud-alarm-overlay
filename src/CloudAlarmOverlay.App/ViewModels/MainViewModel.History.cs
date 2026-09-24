using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CloudAlarmOverlay.Core.Models;
namespace CloudAlarmOverlay.App.ViewModels;
public partial class MainViewModel
{
    [ObservableProperty] private DateTime historyFrom=DateTime.Today.AddDays(-29);
    [ObservableProperty] private DateTime historyTo=DateTime.Today;
    [ObservableProperty] private string historySource="全部";
    [ObservableProperty] private string historyResult="全部";
    [ObservableProperty] private string historyHint="";
    [ObservableProperty] private int historyPage=1;
    private bool resettingHistory;
    private IReadOnlyList<HistoryRow> filteredHistory=[];
    public string[] HistorySources {get;}=["全部",TaskSources.Local,TaskSources.SheetA,TaskSources.SheetB];
    public string[] HistoryResults {get;}=["全部","準時簽收","逾期簽收","逾期未簽收","未開機","稍後提醒"];
    public int OnTimeHistoryCount=>filteredHistory.Count(x=>x.Entry.Result=="Acknowledged");
    public int LateHistoryCount=>filteredHistory.Count(x=>x.Entry.Result=="Overdue_Acknowledged");
    public int UnackedHistoryCount=>filteredHistory.Count(x=>x.Entry.Result=="Overdue_Unacked");
    public int NotLaunchedHistoryCount=>filteredHistory.Count(x=>x.Entry.Result=="NotLaunched");
    public bool CanPreviousHistory=>HistoryPage>1;
    public bool CanNextHistory=>HistoryPage*15<filteredHistory.Count;
    public string HistoryPageLabel=>$"顯示 {(filteredHistory.Count==0?0:(HistoryPage-1)*15+1)}–{Math.Min(HistoryPage*15,filteredHistory.Count)} 筆，共 {filteredHistory.Count} 筆 · 第 {HistoryPage}/{Math.Max(1,(filteredHistory.Count+14)/15)} 頁";
    partial void OnHistoryFromChanged(DateTime value)=>FilterHistory();
    partial void OnHistoryToChanged(DateTime value)=>FilterHistory();
    partial void OnHistorySourceChanged(string value)=>FilterHistory();
    partial void OnHistoryResultChanged(string value)=>FilterHistory();
    private void FilterHistory(bool resetPage=true)
    {
        if(resettingHistory)return;
        if(resetPage)HistoryPage=1;
        HistoryHint=HistoryFrom.Date>HistoryTo.Date?"結束日期不可早於開始日期。":
            (HistoryTo.Date-HistoryFrom.Date).TotalDays>90?"查詢區間較大，載入可能需要數秒。":"";
        var terms=HistorySearch.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
        var patterns=terms.Select(term=>new Regex(term.Contains('%')?"\\A"+Regex.Escape(term).Replace("%",".*")+"\\z":Regex.Escape(term),
            RegexOptions.IgnoreCase|RegexOptions.CultureInvariant|RegexOptions.Singleline|RegexOptions.NonBacktracking)).ToArray();
        filteredHistory=HistoryFrom.Date>HistoryTo.Date?[]:allHistory
            .Where(x=>(x.ScheduledAt??x.TriggeredAt).Date>=HistoryFrom.Date&&(x.ScheduledAt??x.TriggeredAt).Date<=HistoryTo.Date)
            .Select(x=>new HistoryRow(x))
            .Where(x=>(HistorySource=="全部"||x.Source==HistorySource)&&(HistoryResult=="全部"||x.Result==HistoryResult)&&patterns.All(p=>p.IsMatch(x.Title)))
            .OrderByDescending(x=>x.Entry.TriggeredAt).ThenBy(x=>x.Entry.Id).ToArray();
        ShowHistoryPage();
    }
    private void ShowHistoryPage()
    {
        HistoryPage=Math.Clamp(HistoryPage,1,Math.Max(1,(filteredHistory.Count+14)/15));
        History.Clear();foreach(var row in filteredHistory.Skip((HistoryPage-1)*15).Take(15))History.Add(row);
        foreach(var name in new[]{nameof(IsHistoryEmpty),nameof(HistoryPageLabel),nameof(CanPreviousHistory),nameof(CanNextHistory),nameof(OnTimeHistoryCount),nameof(LateHistoryCount),nameof(UnackedHistoryCount),nameof(NotLaunchedHistoryCount)})OnPropertyChanged(name);
    }
    [RelayCommand] private void PreviousHistory(){if(CanPreviousHistory){HistoryPage--;ShowHistoryPage();}}
    [RelayCommand] private void NextHistory(){if(CanNextHistory){HistoryPage++;ShowHistoryPage();}}
    [RelayCommand] private void ClearHistoryFilters()
    {
        resettingHistory=true;
        try{HistorySearch="";HistoryFrom=DateTime.Today.AddDays(-29);HistoryTo=DateTime.Today;HistorySource="全部";HistoryResult="全部";}
        finally{resettingHistory=false;}
        FilterHistory();
    }
    [RelayCommand] private async Task ExportHistoryAsync()
    {
        try
        {
            FilterHistory(false);if(HistoryFrom.Date>HistoryTo.Date)return;
            Status="正在匯出歷史紀錄…";
            var snapshot=filteredHistory.ToArray();
            var filename=$"AckLog_{HistoryFrom:yyyyMMdd}_{HistoryTo:yyyyMMdd}.csv";
            var csv=await Task.Run(()=>CsvExport.Build(["TaskName","ScheduledAt","TriggeredAt","AcknowledgedAt","DurationSeconds","SnoozeCount","Result","Source"],
                snapshot.Select(h=>new string?[]{h.Title,h.Entry.ScheduledAt is {} at?CsvExport.Date(at):"",h.Entry.Result=="NotLaunched"?"":CsvExport.Date(h.Entry.TriggeredAt),h.Entry.AcknowledgedAt is {} ack?CsvExport.Date(ack):"",h.Entry.DurationSeconds?.ToString(),h.Entry.SnoozeCount.ToString(),h.Entry.Result,h.Entry.Source}).ToArray()));
            dialogs.ExportNamed(csv,filename);Status="歷史紀錄匯出作業完成。";
        }
        catch(Exception ex){Status=ex.Message;}
    }
    [RelayCommand] private async Task ExportTaskTriggerLogAsync()
    {
        if(SelectedTask is not {} row){Status="請先選取任務。";return;}
        try
        {
            Status="正在匯出任務完整觸發日誌…";
            var entries=await history.GetTriggerLogAsync(row.Task.Id,row.Task.CreatedAt,DateTime.Now.AddDays(1));
            var filename=$"TriggerLog_{row.Task.Id}_{DateTime.Now:yyyyMMdd}.csv";
            var csv=await Task.Run(()=>CsvExport.Build(
                ["ScheduledAt","OccurrenceState","TriggeredAt","AcknowledgedAt","DurationSeconds","SnoozeCount","Result"],
                entries.Select(e=>new string?[]{CsvExport.Date(e.ScheduledAt),e.OccurrenceState,
                    e.TriggeredAt is {} t?CsvExport.Date(t):"",e.AcknowledgedAt is {} a?CsvExport.Date(a):"",
                    e.DurationSeconds?.ToString(),e.SnoozeCount.ToString(),e.Result??"（未產生簽收紀錄，可能為觸發失敗）"}).ToArray()));
            dialogs.ExportNamed(csv,filename);Status="任務完整觸發日誌匯出完成。";
        }
        catch(Exception ex){Status=ex.Message;}
    }
}
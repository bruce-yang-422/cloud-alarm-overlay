using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CloudAlarmOverlay.App.Services;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Services;

namespace CloudAlarmOverlay.App.ViewModels;

public partial class GoogleWorkspaceViewModel(IGoogleWorkspace workspace,IBrowserLauncher browser):ObservableObject
{
    public ObservableCollection<GoogleAccount> Accounts {get;}=[];
    public ObservableCollection<GoogleSourceDraft> Tabs {get;}=[new(new(){Id="",Name="帳號管理"})];
    public IEnumerable<GoogleSourceDraft> SourceCards=>Tabs.Where(t=>t.Id!="");
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsOverview))] [NotifyPropertyChangedFor(nameof(IsSource))] private GoogleSourceDraft? selected;
    [ObservableProperty] private GoogleAccount? selectedAccount;
    [ObservableProperty] private string accountLabel="個人";
    [ObservableProperty] private bool useSheets=true;
    [ObservableProperty] private bool useCalendar=true;
    [ObservableProperty] private bool writeSheets;
    [ObservableProperty] private string editTaskId="";
    [ObservableProperty] private string editSummary="";
    [ObservableProperty] private bool hasTaskDraft;
    private GoogleTaskDraft? taskDraft;
    public ObservableCollection<GoogleTaskField> TaskFields {get;}=[];
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanAct))] private bool busy;
    [ObservableProperty] private string message="";
    [ObservableProperty] private string configurationStatus="正在載入 Google 登入設定…";
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(SignInCommand))] private bool canSignIn;
    private CancellationTokenSource? operation;
    public bool CanAct=>!Busy;
    public bool IsOverview=>Selected is null||Selected.Id=="";
    public bool IsSource=>!IsOverview;
    public string[] Kinds {get;}=["Google Sheets","Google 日曆"];
    public async Task LoadAsync()
    {
        try{await RefreshAsync();}catch(Exception){Message="無法載入 Google 連線設定，請確認 Windows 帳號與資料檔。";}
    }
    private async Task RefreshAsync()
    {
        var snapshot=await workspace.GetAsync();
        CanSignIn=snapshot.Configured;
        ConfigurationStatus=snapshot.Configured?(snapshot.UsesBuiltInClient?"使用 Cloud Alarm Overlay 的 Google 登入":"使用組織自訂 Google 登入設定"):"此版本尚未設定 Google 登入，請聯絡軟體提供者。";
        var id=SelectedAccount?.Id;
        foreach(var removed in Accounts.Where(a=>snapshot.Accounts.All(x=>x.Id!=a.Id)).ToArray())Accounts.Remove(removed);
        foreach(var account in snapshot.Accounts)
        {
            var current=Accounts.FirstOrDefault(a=>a.Id==account.Id);
            if(current is null)Accounts.Add(account);else if(current!=account)Accounts[Accounts.IndexOf(current)]=account;
        }
        SelectedAccount=Accounts.FirstOrDefault(a=>a.Id==id)??Accounts.FirstOrDefault();
        foreach(var source in snapshot.Sources)
        {
            var draft=Tabs.FirstOrDefault(t=>t.Id==source.Id);
            if(draft is null)Tabs.Add(new(source));else{draft.Status=source.Status;draft.LastSync=source.LastSuccess?.ToLocalTime().ToString("yyyy-MM-dd HH:mm")??"尚未成功同步";}
        }
        OnPropertyChanged(nameof(SourceCards));
        Selected??=Tabs[0];
    }
    private async Task RunAsync(Func<CancellationToken,Task> action,string success)
    {
        if(Busy)return;Busy=true;operation=new();Message="處理中…";
        try{await action(operation.Token);await RefreshAsync();Message=success;}
        catch(OperationCanceledException){Message="操作已取消或逾時，可再次嘗試。";}
        catch(Exception ex){Message=ex is ArgumentException or InvalidOperationException or UnauthorizedAccessException or GoogleAuthorizationException?ex.Message:"Google 操作失敗，請確認帳號權限、API 設定與網路。";}
        finally{operation.Dispose();operation=null;Busy=false;}
    }
    public Task ImportAsync(string json)=>RunAsync(ct=>workspace.ImportClientAsync(json,ct),"OAuth 設定已匯入，可新增 Google 帳號。");
    [RelayCommand] private Task UseBuiltInClientAsync()=>RunAsync(ct=>workspace.UseBuiltInClientAsync(ct),"已恢復程式內建的 Google 登入設定。");
    [RelayCommand(CanExecute=nameof(CanSignIn))] private Task SignInAsync()=>RunAsync(ct=>workspace.SignInAsync(AccountLabel,UseSheets,UseCalendar,url=>browser.Open(new Uri(url)),ct,WriteSheets),"帳號已連線。憑證有效時會在背景自動續用。");
    partial void OnSelectedChanged(GoogleSourceDraft? value){taskDraft=null;HasTaskDraft=false;TaskFields.Clear();EditSummary="";}
    [RelayCommand] private Task ReadTaskAsync()=>ReadTaskCoreAsync(EditTaskId);
    [RelayCommand] private Task NewTaskAsync()=>ReadTaskCoreAsync("");
    private Task ReadTaskCoreAsync(string id)=>RunAsync(async ct=>
    {
        taskDraft=await workspace.ReadTaskAsync(Selected!.Id,id,ct);TaskFields.Clear();
        foreach(var field in taskDraft.Fields)TaskFields.Add(new(field.Key,field.Value));
        EditTaskId=taskDraft.ExternalId;HasTaskDraft=true;EditSummary=taskDraft.Exists?"已載入雲端任務，可修改欄位。":"新增任務草稿，尚未寫入。";
    },"任務已讀取；修改後可預覽差異並寫入。");
    public string PreviewTask(bool delete)
    {
        if(taskDraft is null)throw new InvalidOperationException("請先讀取或新增任務。");
        if(delete&&!taskDraft.Exists)throw new InvalidOperationException("此任務尚未建立。");
        var differences=TaskFields.Where(f=>!taskDraft.Exists||f.Value!=taskDraft.Fields[f.Name]).Select(f=>$"{f.Name}：{taskDraft.Fields[f.Name]} → {f.Value}");
        return $"來源：{taskDraft.SourceName}\n任務 ID：{taskDraft.ExternalId}\n"+(delete?"將刪除此任務整列。":string.Join("\n",differences))+"\n\n請確認此工作表目前沒有其他人或程式同時編輯。";
    }
    public Task WriteTaskAsync(bool delete)=>RunAsync(async ct=>
    {
        var draft=taskDraft??throw new InvalidOperationException("請先讀取或新增任務。");
        var fields=TaskFields.ToDictionary(f=>f.Name,f=>f.Value);
        try{await workspace.WriteTaskAsync(draft.Token,fields,delete,ct);await workspace.SyncAsync(Selected!.Id,ct:ct);}
        finally{taskDraft=null;HasTaskDraft=false;TaskFields.Clear();}
    },"寫入已回讀確認，並完成來源同步。");
    [RelayCommand] private void Cancel()=>operation?.Cancel();
    public Task SignOutAsync(bool revoke)=>RunAsync(async ct=>
    {
        var account=SelectedAccount??throw new ArgumentException("請選擇帳號。");
        await workspace.SignOutAsync(account.Id,revoke,ct);
        foreach(var tab in Tabs.Where(t=>t.Id!=""&&t.AccountId==account.Id).ToArray())Tabs.Remove(tab);
        Selected=Tabs[0];
    },revoke?"已撤銷授權並清除本機來源。":"已登出此帳號並清除本機來源。");
    [RelayCommand] private void AddSource()
    {
        if(Busy)return;
        if(SelectedAccount is null){Message="請先登入 Google 帳號。";return;}
        var tab=new GoogleSourceDraft(new(){AccountId=SelectedAccount.Id});tab.Locked=false;Tabs.Add(tab);Selected=tab;OnPropertyChanged(nameof(SourceCards));
    }
    [RelayCommand] private Task LoadResourcesAsync()=>RunAsync(async ct=>
    {
        var draft=Selected??throw new ArgumentException("請選擇來源。");
        var choices=draft.IsSheet?await workspace.ListTabsAsync(draft.AccountId,draft.ResourceId,ct):await workspace.ListCalendarsAsync(draft.AccountId,ct);
        draft.Resources.Clear();foreach(var resource in choices)draft.Resources.Add(resource);
        draft.SelectedResource=choices.FirstOrDefault(r=>r.Id==(draft.IsSheet?draft.TabId:draft.ResourceId));
    },"已讀取可用工作表／自有日曆，請選擇後儲存。");
    [RelayCommand] private Task SaveSourceAsync()=>RunAsync(async ct=>
    {
        var draft=Selected??throw new ArgumentException("請選擇來源。");
        await workspace.SaveSourceAsync(draft.Value(),ct);draft.Locked=true;
    },"來源設定已儲存並鎖定，背景同步將自動執行。");
    [RelayCommand] private Task SyncSelectedAsync()=>RunAsync(ct=>workspace.SyncAsync(IsOverview?null:Selected!.Id,ct:ct),"同步已完成，請查看每個來源的狀態。");
    public Task RemoveSourceAsync()=>RunAsync(async ct=>
    {
        var selected=Selected??throw new ArgumentException("請選擇來源。");
        if((await workspace.GetAsync(ct)).Sources.Any(s=>s.Id==selected.Id))await workspace.RemoveSourceAsync(selected.Id,ct);
        Tabs.Remove(selected);Selected=Tabs[0];
    },"來源已移除，該來源的本機排程已清除。");
    [RelayCommand] private async Task RefreshStatusAsync(){if(!Busy)await LoadAsync();}
}

public partial class GoogleSourceDraft:ObservableObject
{
    public GoogleSourceDraft(GoogleSource value)
    {
        Id=value.Id;Name=value.Name;AccountId=value.AccountId;Kind=value.Kind=="Calendar"?"Google 日曆":"Google Sheets";
        ResourceId=value.ResourceId;TabId=value.TabId;Enabled=value.Enabled;AllowWrite=value.AllowWrite;IntervalMinutes=value.IntervalMinutes;
        ReminderMinutes=value.ReminderMinutes;IncludeAllDay=value.IncludeAllDay;AllDayHour=value.AllDayHour;Status=value.Status;LastSync=value.LastSuccess?.ToLocalTime().ToString("yyyy-MM-dd HH:mm")??"尚未成功同步";
    }
    public string Id {get;}
    [ObservableProperty] private string name="";
    [ObservableProperty] private string accountId="";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsSheet))] [NotifyPropertyChangedFor(nameof(IsCalendar))] private string kind="Google Sheets";
    [ObservableProperty] private string resourceId="";
    [ObservableProperty] private string tabId="";
    [ObservableProperty] private bool enabled=true;
    [ObservableProperty] private bool allowWrite;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Editable))] private bool locked=true;
    [ObservableProperty] private int intervalMinutes=5;
    [ObservableProperty] private int reminderMinutes=10;
    [ObservableProperty] private bool includeAllDay;
    [ObservableProperty] private int allDayHour=9;
    [ObservableProperty] private string status="";
    [ObservableProperty] private string lastSync="";
    [ObservableProperty] private GoogleResource? selectedResource;
    public ObservableCollection<GoogleResource> Resources {get;}=[];
    public bool Editable=>!Locked;
    public bool IsSheet=>Kind=="Google Sheets";
    public bool IsCalendar=>!IsSheet;
    partial void OnSelectedResourceChanged(GoogleResource? value){if(value is not null){if(IsSheet)TabId=value.Id;else ResourceId=value.Id;}}
    partial void OnKindChanged(string value){Resources.Clear();SelectedResource=null;ResourceId="";TabId="";}
    partial void OnAccountIdChanged(string value){Resources.Clear();SelectedResource=null;}
    public GoogleSource Value()=>new(){Id=Id,Name=Name,AccountId=AccountId,Kind=IsSheet?"Sheet":"Calendar",ResourceId=ResourceId,TabId=TabId,Enabled=Enabled,AllowWrite=AllowWrite,IntervalMinutes=IntervalMinutes,ReminderMinutes=ReminderMinutes,IncludeAllDay=IncludeAllDay,AllDayHour=AllDayHour};
    public override string ToString()=>Name;
}
public partial class GoogleTaskField(string name,string value):ObservableObject
{
    public string Name {get;}=name;
    public bool ReadOnly=>Name=="Id";
    [ObservableProperty] private string value=value;
}

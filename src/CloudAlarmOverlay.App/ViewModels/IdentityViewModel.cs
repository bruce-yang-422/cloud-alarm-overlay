using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CloudAlarmOverlay.Core.Services;
using CloudAlarmOverlay.Core.Models;
namespace CloudAlarmOverlay.App.ViewModels;

public partial class IdentityViewModel(IDeviceIdentityService identity) : ObservableObject
{
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(SaveCommand))] private string deviceId = "";
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(SaveCommand))] private string displayName = "";
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyPropertyChangedFor(nameof(ModeDescription)), NotifyPropertyChangedFor(nameof(IdentityHelp)), NotifyPropertyChangedFor(nameof(ShowDeviceCode))]
    private string usageSelection = "";
    public string[] UsageChoices { get; } = ["個人使用", "公司使用"];
    private readonly string personalDeviceId = "PERSONAL-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
    public bool ShowDeviceCode => UsageSelection != "個人使用";
    public string ModeDescription => UsageSelection switch
    {
        "個人使用" => "管理員功能直接開放，不需密碼，也不會逾時登出。結束程式預設不要求密碼，可另外設定專用結束密碼。",
        "公司使用" => "管理員功能需驗證帳號與密碼；登入固定有效 10 分鐘。結束程式沿用公司密碼保護。",
        _ => "請先選擇使用方式，設定這台電腦的管理權限。"
    };
    public string IdentityHelp => UsageSelection == "個人使用" ? "填入你的顯示名稱，裝置代碼會自動建立。" : "請輸入 IT 提供的裝置代碼與顯示名稱。通知將依此識別對象。";
    partial void OnUsageSelectionChanged(string value)
    {
        if(value=="個人使用")
        {
            DeviceId=personalDeviceId;
            if(string.IsNullOrWhiteSpace(DisplayName))DisplayName=Environment.UserName;
        }
        else if(DeviceId==personalDeviceId)DeviceId="";
    }
    [ObservableProperty] private string error = "";
    public event Action? Saved;

    private bool CanSave()=>UsageChoices.Contains(UsageSelection)&&!string.IsNullOrWhiteSpace(DeviceId)&&!string.IsNullOrWhiteSpace(DisplayName);
    [RelayCommand(CanExecute=nameof(CanSave))]
    private async Task SaveAsync()
    {
        try
        {
            var mode=UsageSelection switch { "個人使用"=>UsageModes.Personal,"公司使用"=>UsageModes.Company,_=>throw new ArgumentException("請選擇使用方式。") };
            await identity.SetInitialIdentityAsync(DeviceId, DisplayName, mode);
            Saved?.Invoke();
        }
        catch (Exception exception)
        {
            Error = exception.Message;
        }
    }
}

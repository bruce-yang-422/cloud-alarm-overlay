using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CloudAlarmOverlay.Core.Services;
namespace CloudAlarmOverlay.App.ViewModels;

public partial class IdentityViewModel(IDeviceIdentityService identity) : ObservableObject
{
    [ObservableProperty] private string deviceId = "";
    [ObservableProperty] private string displayName = "";
    [ObservableProperty] private string error = "";
    public event Action? Saved;

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            await identity.SetInitialIdentityAsync(DeviceId, DisplayName);
            Saved?.Invoke();
        }
        catch (Exception exception)
        {
            Error = exception.Message;
        }
    }
}

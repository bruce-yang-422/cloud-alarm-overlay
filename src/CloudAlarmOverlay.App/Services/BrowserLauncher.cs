using System.Diagnostics;

namespace CloudAlarmOverlay.App.Services;

public interface IBrowserLauncher
{
    void Open(Uri address);
}

public sealed class BrowserLauncher : IBrowserLauncher
{
    public void Open(Uri address) => Process.Start(new ProcessStartInfo(address.AbsoluteUri) { UseShellExecute = true });
}

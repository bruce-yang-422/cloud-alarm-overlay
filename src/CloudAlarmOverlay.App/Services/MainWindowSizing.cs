using System.Runtime.InteropServices;
using System.Windows;

namespace CloudAlarmOverlay.App.Services;

/// <summary>Converts the requested physical-pixel presets to WPF layout units.</summary>
public static class MainWindowSizing
{
    public const double AspectRatio = 7d / 5;

    public static Size Calculate(double monitorHeightPixels, Size workAreaPixels, double dpiScale)
    {
        var preferredHeight = monitorHeightPixels switch
        {
            // Cap only the startup preset at QHD size; manual resizing is unrestricted.
            >= 1440 => 1280d,
            >= 1200 => 1100d,
            _ => 960d
        };
        var height = Math.Min(preferredHeight, Math.Min(workAreaPixels.Height, workAreaPixels.Width / AspectRatio));
        return new Size(height * AspectRatio / dpiScale, height / dpiScale);
    }

    public static Size ForWindow(IntPtr handle)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (GetMonitorInfo(MonitorFromWindow(handle, 2), ref info))
        {
            var dpi = GetDpiForWindow(handle);
            return Calculate(info.Monitor.Bottom - info.Monitor.Top,
                new Size(info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top),
                dpi > 0 ? dpi / 96d : 1);
        }
        // A native monitor query can fail while the display configuration changes.
        // SystemParameters uses WPF units; convert before applying the same policy.
        var fallbackScale = GetDpiForSystem() / 96d;
        if (fallbackScale <= 0) fallbackScale = 1;
        var work = SystemParameters.WorkArea;
        return Calculate(SystemParameters.PrimaryScreenHeight * fallbackScale,
            new Size(work.Width * fallbackScale, work.Height * fallbackScale), fallbackScale);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();
}

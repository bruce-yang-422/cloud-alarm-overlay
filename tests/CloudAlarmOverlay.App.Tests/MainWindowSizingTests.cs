using System.Windows;
using CloudAlarmOverlay.App.Services;

namespace CloudAlarmOverlay.App.Tests;

public sealed class MainWindowSizingTests
{
    [Theory]
    [InlineData(1920, 1080, 1, 1344, 960)]
    [InlineData(1920, 1080, 1.5, 1344, 960)]
    [InlineData(1920, 1200, 1, 1540, 1100)]
    [InlineData(1920, 1200, 1.5, 1540, 1100)]
    [InlineData(2560, 1440, 1, 1792, 1280)]
    [InlineData(2560, 1440, 1.5, 1792, 1280)]
    [InlineData(2560, 1600, 1, 1792, 1280)]
    [InlineData(2560, 1600, 2, 1792, 1280)]
    [InlineData(3840, 2160, 1, 1792, 1280)]
    [InlineData(3840, 2160, 2, 1792, 1280)]
    [InlineData(5120, 2880, 2, 1792, 1280)]
    [InlineData(7680, 4320, 3, 1792, 1280)]
    public void Presets_keep_the_requested_physical_pixels_under_display_scaling(
        double width, double height, double scale, double expectedWidth, double expectedHeight)
    {
        var size = MainWindowSizing.Calculate(height, new Size(width, height - 48 * scale), scale);
        Assert.Equal(expectedWidth, size.Width * scale, 6);
        Assert.Equal(expectedHeight, size.Height * scale, 6);
        Assert.Equal(1.4, size.Width / size.Height, 6);
    }

    [Theory]
    [InlineData(1366, 768, 1, 1366, 720)]
    [InlineData(1080, 1920, 1.5, 1000, 1850)]
    [InlineData(1920, 1080, 2, 1920, 984)]
    [InlineData(2560, 1600, 2.5, 2560, 1480)]
    public void Smaller_or_portrait_work_areas_fit_without_changing_the_ratio(
        double width, double height, double scale, double workWidth, double workHeight)
    {
        var size = MainWindowSizing.Calculate(height, new Size(workWidth, workHeight), scale);
        Assert.True(size.Width * scale <= Math.Min(width, workWidth));
        Assert.True(size.Height * scale <= workHeight);
        Assert.Equal(1.4, size.Width / size.Height, 6);
    }
}

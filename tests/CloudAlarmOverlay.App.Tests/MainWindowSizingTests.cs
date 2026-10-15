using System.Windows;
using CloudAlarmOverlay.App.Services;

namespace CloudAlarmOverlay.App.Tests;

public sealed class MainWindowSizingTests
{
    [Theory]
    [InlineData(1920, 1080, 1, 1344, 960)]
    [InlineData(1920, 1080, 1.5, 1411.2, 1008)]
    [InlineData(1920, 1200, 1, 1344, 960)]
    [InlineData(1920, 1200, 1.5, 1579.2, 1128)]
    [InlineData(2560, 1440, 1, 1400, 1000)]
    [InlineData(2560, 1440, 1.5, 1915.2, 1368)]
    [InlineData(2560, 1600, 1, 1400, 1000)]
    [InlineData(2560, 1600, 2, 2105.6, 1504)]
    [InlineData(2560, 1080, 1, 1344, 960)]
    [InlineData(3440, 1440, 1, 1400, 1000)]
    [InlineData(3440, 1440, 1.25, 1680, 1200)]
    [InlineData(3440, 1440, 1.5, 1915.2, 1368)]
    [InlineData(3840, 2160, 1, 1400, 1000)]
    [InlineData(3840, 2160, 1.5, 2016, 1440)]
    [InlineData(3840, 2160, 1.75, 2352, 1680)]
    [InlineData(3840, 2160, 2, 2688, 1920)]
    [InlineData(5120, 2880, 2, 2688, 1920)]
    [InlineData(7680, 4320, 3, 4032, 2880)]
    public void Presets_preserve_layout_space_under_display_scaling(
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

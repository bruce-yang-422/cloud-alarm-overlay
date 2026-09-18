using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CloudAlarmOverlay.App.Styles;
using CloudAlarmOverlay.App.ViewModels;
using CloudAlarmOverlay.App.Views;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace CloudAlarmOverlay.App.Tests;

// Uses the existing isolated UI fixture, never the installed application's data.
internal static class SiteScreenshotCapture
{
    internal static async Task CaptureAsync(IServiceProvider services, MainWindow window,
        MainViewModel main, string directory)
    {
        Directory.CreateDirectory(directory);
        AdaptiveBrushExtension.Apply(false, ThemeColorStyle.Default);
        var originalSize = new Size(window.Width, window.Height);
        // Capture a manually enlarged 7:5 window to show the complete dashboard.
        window.Width = 1540;
        window.Height = 1100;
        var today = DateTime.Today;
        var repository = services.GetRequiredService<ICountdownRepository>();
        var examples = new[]
        {
            new CountdownItem { Id="site-project", Title="專案上線", Category="工作", TargetAt=today.AddDays(3).AddHours(15), Mode="Time", IsPinned=true, IsTop=true, ReminderDays=1, ReminderMinutes=870, Notes="最後檢查，準備迎接新的里程碑。" },
            new CountdownItem { Id="site-reading", Title="持續閱讀", Category="生活", TargetAt=today.AddDays(-128), Direction="Up", IsPinned=true, IsTop=true, Notes="每天留一點時間給自己。" },
            new CountdownItem { Id="site-travel", Title="日本旅行", Category="旅行", TargetAt=today.AddDays(45), ReminderDays=7, Notes="探索世界，收藏美好回憶。" },
            new CountdownItem { Id="site-holiday", Title="節日聚餐", Category="節日", TargetAt=today.AddDays(7), ReminderDays=1 },
            new CountdownItem { Id="site-course", Title="完成線上課程", Category="其他", TargetAt=today.AddDays(-2), CompletedAt=today.AddDays(-2) },
            new CountdownItem { Id="site-review", Title="每月工作回顧", Category="工作", TargetAt=today.AddDays(-1) }
        };
        foreach (var example in examples)
            await repository.SaveAsync(example with { CreatedAt=today.AddDays(-30) });
        await main.Countdowns.LoadAsync();

        foreach (var (page, name) in new[] { (0,"home"), (1,"tasks"), (3,"pomodoro"), (6,"countdown") })
        {
            main.PageIndex = page;
            main.UpdateCountdown();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            Capture((FrameworkElement)window.Content, Path.Combine(directory, $"site-{name}-light.png"));
        }

        main.Countdowns.EditCommand.Execute(main.Countdowns.Items.Single(row => row.Item.Id == "site-project"));
        var editor = new CountdownEditorWindow(main.Countdowns) { Owner=window, ShowActivated=false,
            Height=Math.Min(1100, SystemParameters.WorkArea.Height-40) };
        try
        {
            editor.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            editor.UpdateLayout();
            Capture((FrameworkElement)editor.Content, Path.Combine(directory,"site-countdown-editor-light.png"));
        }
        finally { editor.Close(); main.Countdowns.NewCommand.Execute(null); }

        var notice = new AlarmTask { Id="site-notice", Title="確認本週專案進度", Level=AlarmLevels.Mid,
            Description="請整理本週已完成的工作與待處理事項。\n會議前確認負責人及預定完成日期。",
            Note="會議室 B，請攜帶筆記。", ScheduledAt=DateTime.Now, CreatedAt=today, UpdatedAt=today };
        var notification = new AlarmWindow(new AlarmViewModel(notice,"",true),AlarmLevels.Mid,colorMode:"亮色");
        try
        {
            notification.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            notification.UpdateLayout();
            Capture((FrameworkElement)notification.Content,Path.Combine(directory,"site-notification-light.png"));
        }
        finally { notification.Finish(false); }
        main.PageIndex = 0;
        window.Width = originalSize.Width;
        window.Height = originalSize.Height;
    }

    private static void Capture(FrameworkElement content, string path)
    {
        var dpi = VisualTreeHelper.GetDpi(content);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth*dpi.DpiScaleX),
            (int)Math.Ceiling(content.ActualHeight*dpi.DpiScaleY),96*dpi.DpiScaleX,96*dpi.DpiScaleY,PixelFormats.Pbgra32);
        // Draw the window background too: editor content has a transparent root.
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            var bounds = new Rect(0,0,content.ActualWidth,content.ActualHeight);
            context.DrawRectangle(Window.GetWindow(content)?.Background ?? Brushes.White,null,bounds);
            context.DrawRectangle(new VisualBrush(content),null,bounds);
        }
        bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }
}

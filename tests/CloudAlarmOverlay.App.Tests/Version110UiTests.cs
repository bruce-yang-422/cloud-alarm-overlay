using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using CloudAlarmOverlay.App.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CloudAlarmOverlay.App.Services;
using CloudAlarmOverlay.App.Styles;
using CloudAlarmOverlay.App.ViewModels;
using CloudAlarmOverlay.App.Views;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Repositories;
using CloudAlarmOverlay.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ThemeMode = CloudAlarmOverlay.Core.Models.ThemeMode;

namespace CloudAlarmOverlay.App.Tests;

public sealed class Version110UiTests
{
    [Fact]
    public Task Brightness_and_color_are_independent_persist_and_update_open_windows() => MilestoneOneTests.RunSta(async () =>
    {
        using var fixture = new Fixture(); await fixture.Initialize();
        var vm=fixture.Get<MaintenanceViewModel>(); await vm.InitializeAsync();
        var theme=fixture.Get<IThemeService>();
        var settings=fixture.Get<ISettingsRepository>();
        var preview=new TaskPreviewWindow(new(TaskSample()));
        var settingsWindow=new Window{Content=new MaintenanceView{DataContext=vm},Width=900,Height=850};
        settingsWindow.SetBinding(Control.BackgroundProperty,new System.Windows.Data.Binding("Background"){Source=preview});
        settingsWindow.SetBinding(Control.ForegroundProperty,new System.Windows.Data.Binding("Foreground"){Source=preview});
        try
        {
            preview.Show(); settingsWindow.Show();
            Assert.Equal(new[]{"淺色","暗色","跟隨系統"},vm.Themes);
            Assert.Equal(new[]{"預設","櫻花粉","若竹綠","薰衣草紫","夕陽橘","極簡銀白"},vm.ThemeColors);
            foreach(var color in vm.ThemeColors)
            foreach(var brightness in vm.Themes)
            {
                vm.ThemeColorChoice=color; vm.ThemeChoice=brightness;
                for(var retry=0;retry<100;retry++)
                {
                    if((await settings.GetAsync("ThemeMode"))?.Value==brightness && (await settings.GetAsync("ThemeColorStyle"))?.Value==color)break;
                    await Task.Delay(10);
                }
                Assert.Equal(brightness,(await settings.GetAsync("ThemeMode"))!.Value);
                Assert.Equal(color,(await settings.GetAsync("ThemeColorStyle"))!.Value);
                await vm.InitializeAsync();
                Assert.Equal(brightness,vm.ThemeChoice); Assert.Equal(color,vm.ThemeColorChoice);
                Assert.Equal(color switch{"櫻花粉"=>ThemeColorStyle.Pink,"若竹綠"=>ThemeColorStyle.Bamboo,"薰衣草紫"=>ThemeColorStyle.Lavender,"夕陽橘"=>ThemeColorStyle.Sunset,"極簡銀白"=>ThemeColorStyle.Silver,_=>ThemeColorStyle.Default},theme.ColorStyle);
                Assert.Equal(brightness switch{"淺色"=>ThemeMode.Light,"暗色"=>ThemeMode.Dark,_=>ThemeMode.System},theme.Mode);
                if(brightness!="跟隨系統")Assert.Equal(brightness=="暗色",theme.IsDark);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                preview.UpdateLayout(); settingsWindow.UpdateLayout();
                var foreground=((SolidColorBrush)preview.Foreground).Color;
                var background=((SolidColorBrush)preview.Background).Color;
                Assert.True(Contrast(foreground,background)>=4.5,$"{brightness}/{color} contrast");
                if(color is "薰衣草紫" or "夕陽橘" or "極簡銀白")
                {
                    var primary=new Button { Style=(Style)preview.FindResource("Primary") };
                    var fill=((SolidColorBrush)primary.Background).Color;
                    var text=((SolidColorBrush)primary.Foreground).Color;
                    var expected=color switch {"薰衣草紫"=>theme.IsDark?"#7C3AED":"#C4B5FD","夕陽橘"=>theme.IsDark?"#EA580C":"#FDBA74",_=>theme.IsDark?"#64748B":"#E2E8F0"};
                    Assert.Equal((Color)ColorConverter.ConvertFromString(expected),fill);
                    Assert.True(Contrast(text,fill)>=4.5,$"{brightness}/{color} primary button contrast");
                }
                if(brightness!="跟隨系統")
                {
                    Capture(preview,$"preview-{color}-{brightness}");
                    Capture(settingsWindow,$"settings-{color}-{brightness}");
                }
            }
        }
        finally {preview.Close();settingsWindow.Close();AdaptiveBrushExtension.Apply(false);}
    });

    [Theory]
    [InlineData("粉紅色","櫻花粉",ThemeColorStyle.Pink)]
    [InlineData("若竹色","若竹綠",ThemeColorStyle.Bamboo)]
    public Task Legacy_color_names_keep_brightness_and_load_with_new_labels(string oldName,string newName,ThemeColorStyle expected) => MilestoneOneTests.RunSta(async () =>
    {
        using var fixture=new Fixture();await fixture.Initialize();
        var settings=fixture.Get<ISettingsRepository>();
        await settings.SaveAsync(new(){Key="ThemeMode",Value="暗色"});
        await settings.SaveAsync(new(){Key="ThemeColorStyle",Value=oldName});
        var vm=fixture.Get<MaintenanceViewModel>();await vm.InitializeAsync();
        Assert.Equal("暗色",vm.ThemeChoice);Assert.Equal(newName,vm.ThemeColorChoice);
        Assert.Equal(expected,fixture.Get<IThemeService>().ColorStyle);
        Assert.True(fixture.Get<IThemeService>().IsDark);
        AdaptiveBrushExtension.Apply(false);
    });

    private static double Contrast(Color a,Color b)
    {
        static double Channel(byte value){var v=value/255d;return v<=.04045?v/12.92:Math.Pow((v+.055)/1.055,2.4);}
        static double Luminance(Color c)=>.2126*Channel(c.R)+.7152*Channel(c.G)+.0722*Channel(c.B);
        var x=Luminance(a);var y=Luminance(b);return (Math.Max(x,y)+.05)/(Math.Min(x,y)+.05);
    }

    [Fact]
    public Task Task_and_history_previews_render_markdown_in_both_fields_as_readonly() => MilestoneOneTests.RunSta(async () =>
    {
        const string markdown = "## 工作內容\n\n**重要** 與 *說明*\n\n- [x] 已處理\n- [ ] 待處理\n\n[參考](https://example.test/)\n\n```\n原始程式碼\n```";
        var task = TaskSample() with { Description = markdown, Note = markdown };
        var entry = new AcknowledgementLog { Id = "preview", DeviceId = "TEST", Result = "Acknowledged", TaskId = task.Id, TriggeredAt = DateTime.Now, TaskName = task.Title, TaskSnapshotJson = JsonSerializer.Serialize(task) };
        foreach(var vm in new[] { new TaskPreviewViewModel(task), TaskPreviewViewModel.FromHistory(entry) })
        {
            var window = new TaskPreviewWindow(vm);
            try
            {
                window.Show(); window.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                foreach(var name in new[]{"DescriptionPreview","NotePreview"})
                {
                    var view = (MarkdownView)window.FindName(name);
                    Assert.True(view.IsReadOnly); Assert.False(view.EditableTasks);
                    var heading = (Paragraph)view.Document.Blocks.FirstBlock;
                    Assert.Equal(FontWeights.Bold,heading.FontWeight);
                    var paragraphs = view.Document.Blocks.OfType<Paragraph>().ToArray();
                    Assert.Contains(paragraphs,p=>p.Inlines.OfType<Bold>().Any());
                    Assert.Contains(paragraphs,p=>p.Inlines.OfType<Italic>().Any());
                    Assert.Contains(paragraphs,p=>p.Inlines.OfType<Hyperlink>().Any());
                    var list = view.Document.Blocks.OfType<System.Windows.Documents.List>().Single();
                    var boxes = list.ListItems.SelectMany(i=>i.Blocks.OfType<Paragraph>()).SelectMany(p=>p.Inlines).OfType<InlineUIContainer>().Select(i=>(CheckBox)i.Child).ToArray();
                    Assert.Equal(2,boxes.Length); Assert.True(boxes[0].IsChecked); Assert.False(boxes[1].IsChecked);
                    Assert.All(boxes,box=>Assert.False(box.IsEnabled));
                    Assert.Contains("原始程式碼",new TextRange(view.Document.ContentStart,view.Document.ContentEnd).Text);
                    Assert.Equal(markdown,view.Text);
                }
                Assert.Equal(markdown,vm.Description); Assert.Equal(markdown,vm.Note);
            }
            finally {window.Close();}
        }
    });

    [Fact]
    public Task Editor_roundtrips_selected_months_and_saves_minutes_only_in_new_themes() => MilestoneOneTests.RunSta(async () =>
    {
        using var fixture = new Fixture();
        await fixture.Initialize();
        var service = fixture.Get<ITaskService>();
        var scheduler = fixture.Get<ITaskSchedulingService>();
        var original = TaskSample() with { Recurrence = "Monthly:10:1,3,5,7,9,11", ScheduledAt = new(2024, 1, 10, 15, 30, 47) };
        await service.SaveLocalAsync(original);
        var editor = new TaskEditorViewModel(service, original, false, scheduler);
        Assert.Equal(new[] { 1, 3, 5, 7, 9, 11 }, editor.Months.Where(m => m.IsSelected).Select(m => m.Value));
        await editor.RefreshNextReminderAsync();
        Assert.StartsWith("下一次提醒：", editor.NextReminderPreview);
        var window = new TaskEditorWindow { DataContext = editor };
        try
        {
            window.Show();
            foreach (var mode in new[] { ThemeMode.Pink, ThemeMode.Bamboo, ThemeMode.Dark })
            {
                AdaptiveBrushExtension.Apply(mode == ThemeMode.Dark, mode);
                window.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.Null(window.FindName("SecondWheel"));
                Assert.NotNull(window.FindName("MinuteWheel"));
                Capture(window, "v110-editor-" + mode);
            }
            await editor.SaveCommand.ExecuteAsync(null);
            Assert.Equal("", editor.Error);
            var saved = (await fixture.Get<ITaskRepository>().GetByIdAsync(original.Id))!;
            Assert.Equal(0, saved.ScheduledAt.Second);
            Assert.Equal(original.Recurrence, saved.Recurrence);
            foreach (var month in editor.Months) month.IsSelected = false;
            await editor.SaveCommand.ExecuteAsync(null);
            Assert.Contains("月份", editor.Error);
            Assert.Equal(original.Recurrence, (await fixture.Get<ITaskRepository>().GetByIdAsync(original.Id))!.Recurrence);
        }
        finally { window.Close(); AdaptiveBrushExtension.Apply(false); }
    });

    [Fact]
    public Task Themes_persist_and_readonly_preview_uses_historical_content_without_acknowledging() => MilestoneOneTests.RunSta(async () =>
    {
        using var fixture = new Fixture(); await fixture.Initialize();
        var maintenance = fixture.Get<MaintenanceViewModel>();
        foreach (var choice in new[] { "粉紅色", "若竹色" })
        {
            await fixture.Get<ISettingsRepository>().SaveAsync(new() { Key = "ThemeMode", Value = choice });
            await maintenance.InitializeAsync();
            Assert.Equal("淺色", maintenance.ThemeChoice);
            Assert.Equal(choice=="粉紅色"?"櫻花粉":"若竹綠", maintenance.ThemeColorChoice);
            Assert.Equal(ThemeMode.Light, fixture.Get<IThemeService>().Mode);
            Assert.Equal(choice == "粉紅色" ? ThemeColorStyle.Pink : ThemeColorStyle.Bamboo, fixture.Get<IThemeService>().ColorStyle);
            var entry = new AcknowledgementLog { Id = "old", TaskId = "task", DeviceId = "TEST", Result = "Acknowledged", TriggeredAt = DateTime.Now, TaskName = "歷史任務", TaskSnapshotJson = JsonSerializer.Serialize(TaskSample()) };
            var vm = TaskPreviewViewModel.FromHistory(entry);
            Assert.Equal("## 原始內容\n- [ ] 尚未勾選", vm.Description);
            var preview = new TaskPreviewWindow(vm);
            try
            {
                preview.Show(); preview.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Capture(preview, choice == "粉紅色" ? "v110-preview-pink" : "v110-preview-bamboo");
                Assert.Empty(await fixture.Get<IAckLogRepository>().GetRangeAsync(DateTime.MinValue, DateTime.MaxValue));
            }
            finally { preview.Close(); }
            Assert.Contains("不可取得", TaskPreviewViewModel.FromHistory(entry with { TaskSnapshotJson = null }).Description);
            Assert.Contains("不可取得", TaskPreviewViewModel.FromHistory(entry with { TaskSnapshotJson = "invalid" }).Description);
        }
        AdaptiveBrushExtension.Apply(false);
    });

    [Fact]
    public async Task Download_command_fetches_current_manifest_without_prior_check_and_reports_errors()
    {
        var updates = new FakeUpdates(); var browser = new FakeBrowser();
        using var fixture = new Fixture(s => { s.AddSingleton<IUpdateCheckService>(updates); s.AddSingleton<IBrowserLauncher>(browser); });
        await fixture.Initialize();
        var vm = fixture.Get<MaintenanceViewModel>();
        await vm.OpenDownloadCommand.ExecuteAsync(null);
        Assert.Equal(new Uri("https://example.test/current.exe"), browser.Opened);
        Assert.Contains("已交由瀏覽器下載安裝檔", vm.Message); Assert.False(vm.Busy);
        updates.Failure = true;
        await vm.OpenDownloadCommand.ExecuteAsync(null);
        Assert.Contains("無法啟動安裝檔下載", vm.Message); Assert.False(vm.Busy);
    }

    [Fact]
    public async Task Builtin_builder_serves_page_current_settings_and_local_ids_without_writing_sheets()
    {
        var csv = new FakeCsv();
        using var fixture = new Fixture(s => s.AddSingleton<ISheetCsvClient>(csv));
        await fixture.Initialize();
        var builder = fixture.Get<TaskBuilderHostService>();
        var url = builder.Start();
        using var client = new HttpClient { BaseAddress = url };
        var html = await client.GetStringAsync(url);
        Assert.Contains("panel-monthly", html);
        Assert.DoesNotContain("docs.google.com/spreadsheets", html);
        Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync("/api/employees-csv")).StatusCode);
        await fixture.Get<IAuthenticationService>().EnsureDefaultAdministratorAsync();
        Assert.True(await fixture.Get<IAuthenticationService>().AuthenticateAsync("admin", "12345"));
        await fixture.Get<SyncConfiguration>().SaveAsync(new() { SheetAId = "test-A", TasksAGid = "1", EmployeesGid = "2", HolidaysGid = "3", LunarGid = "4", SheetBId = "test-B", TasksBGid = "5" });
        await fixture.Get<ITaskService>().SaveLocalAsync(TaskSample() with { Id = "TB-001" });
        Assert.Contains("TB-001", await client.GetStringAsync("/api/task-ids"));
        Assert.Equal("Id,Name,Department,Title\nTEST,測試,測試部,專員", await client.GetStringAsync("/api/employees-csv"));
        Assert.Equal(("test-A", "2"), csv.LastRequest);
        await client.GetStringAsync("/api/tasks-csv"); Assert.Equal(("test-B", "5"), csv.LastRequest);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/missing")).StatusCode);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/employees-csv");
        request.Headers.Add("Origin", "https://example.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(request)).StatusCode);
    }

    private static AlarmTask TaskSample() => new() { Id = "task", Title = "詳細任務訊息", Description = "## 原始內容\n- [ ] 尚未勾選", Note = "補充備註 ✅", Recurrence = "Daily", ScheduledAt = DateTime.Now.AddDays(-1), CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now };
    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("CLOUD_ALARM_SCREENSHOT_DIR");
        if (directory is null) return;
        Directory.CreateDirectory(directory);
        var content = window;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(file);
    }
    [Fact]
    public async Task Builder_opens_local_form_and_current_sheet_B_tasks_tab_when_configured()
    {
        var browser = new FakeBrowser();
        using var fixture = new Fixture(s => s.AddSingleton<IBrowserLauncher>(browser));
        await fixture.Initialize();
        var builder = fixture.Get<TaskBuilderHostService>();
        await builder.OpenInBrowserAsync();
        Assert.Single(browser.Addresses);
        Assert.Equal("localhost", browser.Addresses[0].Host);
        Assert.Equal("/task_builder_tailwind.html", browser.Addresses[0].AbsolutePath);

        await fixture.Get<IAuthenticationService>().EnsureDefaultAdministratorAsync();
        Assert.True(await fixture.Get<IAuthenticationService>().AuthenticateAsync("admin", "12345"));
        foreach (var (id, gid) in new[] { ("test-B", "123"), ("updated-B", "456") })
        {
            await fixture.Get<SyncConfiguration>().SaveAsync(new() { SheetBId = id, TasksBGid = gid });
            browser.Addresses.Clear();
            await builder.OpenInBrowserAsync();
            Assert.Equal(2, browser.Addresses.Count);
            Assert.Equal("localhost", browser.Addresses[0].Host);
            Assert.Equal($"https://docs.google.com/spreadsheets/d/{id}/edit?gid={gid}#gid={gid}", browser.Addresses[1].AbsoluteUri);
        }
    }

    private sealed class FakeBrowser : IBrowserLauncher
    {
        public Uri? Opened;
        public List<Uri> Addresses { get; } = [];
        public void Open(Uri address) { Opened = address; Addresses.Add(address); }
    }
    private sealed class FakeUpdates : IUpdateCheckService
    {
        public bool Failure;
        public Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken = default) => Task.FromResult<UpdateInfo?>(null);
        public Task<UpdateInfo> GetManifestAsync(CancellationToken cancellationToken = default) => Failure ? Task.FromException<UpdateInfo>(new HttpRequestException("測試離線")) : Task.FromResult(new UpdateInfo(new Version(1, 1, 0), new Uri("https://example.test/current.exe"), ""));
    }
    private sealed class FakeCsv : ISheetCsvClient
    {
        public (string, string) LastRequest;
        public Task<string> DownloadAsync(string spreadsheetId, string gid, CancellationToken cancellationToken = default)
        { LastRequest = (spreadsheetId, gid); return Task.FromResult("Id,Name,Department,Title\nTEST,測試,測試部,專員"); }
    }
    private sealed class Fixture : IDisposable, IAppPaths
    {
        private readonly IHost host;
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "CloudAlarmV110Ui", Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "test.db");
        public Fixture(Action<IServiceCollection>? configure = null) => host = new HostBuilder().ConfigureServices(s => { CompositionRoot.ConfigureServices(s); s.AddSingleton<IAppPaths>(this); configure?.Invoke(s); }).Build();
        public T Get<T>() where T : notnull => host.Services.GetRequiredService<T>();
        public Task Initialize() => Get<IDatabaseInitializer>().InitializeAsync();
        public void Dispose() { host.Dispose(); if (Directory.Exists(DataDirectory)) Directory.Delete(DataDirectory, true); }
    }
}

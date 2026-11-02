using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CloudAlarmOverlay.App.Services;
using CloudAlarmOverlay.App.Styles;
using CloudAlarmOverlay.App.ViewModels;
using CloudAlarmOverlay.App.Views;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
namespace CloudAlarmOverlay.App.Tests;

public sealed class SoftwareUpdateUiTests
{
    [Fact] public void Legacy_plain_notes_become_spaced_items_and_a_named_link_without_rewriting_markdown()
    {
        const string plain="新增提醒功能。\r\n改善操作介面。\r\n完整改版說明：https://example.test/release";
        var formatted=ReleaseNotesFormatter.Format(plain);
        Assert.Contains("- 新增提醒功能。\n\n- 改善操作介面。",formatted);
        Assert.Contains("[完整改版說明](<https://example.test/release>)",formatted);
        const string markdown="## 功能\n- **提醒**：保留格式。\n  - 子項目\n\n```text\n原始\n換行\n```";
        Assert.Equal(markdown,ReleaseNotesFormatter.Format(markdown));
        Assert.Equal("此版本未提供更新說明。",ReleaseNotesFormatter.Format(" "));
    }
    [Fact] public Task Published_manifest_renders_sections_lists_and_link_at_readable_width()=>MilestoneOneTests.RunSta(async()=>
    {
        using var manifest=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../version.json"))));
        var updates=new Updates{Version=new(1,3,0),Note=manifest.RootElement.GetProperty("releaseNote").GetString()!};
        using var host=CreateHost(updates);var vm=host.Services.GetRequiredService<MaintenanceViewModel>();
        var view=new SoftwareUpdateView{DataContext=vm};
        var window=new Window{Content=new ScrollViewer{Content=view,Padding=new Thickness(24),HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled},Width=800,Height=950,FontFamily=new FontFamily("Segoe UI, Microsoft JhengHei UI"),ShowInTaskbar=false,ShowActivated=false};
        try
        {
            window.Show();await vm.CheckUpdateAsync();window.UpdateLayout();
            var markdown=(CloudAlarmOverlay.App.Controls.MarkdownView)view.FindName("ReleaseNotes");
            Assert.Equal(23,markdown.Document.LineHeight);
            var headings=updates.Note.Split('\n').Count(line=>line.StartsWith("## "));Assert.True(headings>0);
            Assert.True(markdown.Document.Blocks.OfType<Paragraph>().Count(p=>p.FontWeight==FontWeights.SemiBold)>=headings);
            Assert.NotEmpty(markdown.Document.Blocks.OfType<System.Windows.Documents.List>());
            var link=markdown.Document.Blocks.OfType<Paragraph>().SelectMany(p=>p.Inlines.OfType<Hyperlink>()).Single();
            Assert.Equal("查看完整改版說明",new TextRange(link.ContentStart,link.ContentEnd).Text);
            Assert.EndsWith("/releases/tag/v"+manifest.RootElement.GetProperty("latestVersion").GetString(),link.NavigateUri.AbsoluteUri);
            Assert.InRange(markdown.ActualWidth,400,640);
            foreach(var dark in new[]{false,true})
            {
                AdaptiveBrushExtension.Apply(dark);window.Background=new SolidColorBrush(dark?Color.FromRgb(32,34,38):Colors.White);window.UpdateLayout();
                Capture(window,dark?"release-notes-formatted-dark":"release-notes-formatted-light");
            }
            window.Width=540;window.UpdateLayout();Assert.True(markdown.ActualWidth<=((FrameworkElement)window.Content).ActualWidth-48);
            updates.Note="新增功能。\n改善介面。\n完整改版說明：https://example.test/release";await vm.CheckUpdateAsync();window.UpdateLayout();
            Assert.Single(markdown.Document.Blocks.OfType<System.Windows.Documents.List>());
            Assert.Contains(markdown.Document.Blocks.OfType<Paragraph>(),p=>p.Inlines.OfType<Hyperlink>().Any());
        }
        finally{window.Close();AdaptiveBrushExtension.Apply(false);}
    });
    [Fact] public async Task Release_notes_are_available_for_new_equal_and_older_public_versions()
    {
        var updates=new Updates();using var host=CreateHost(updates);
        var vm=host.Services.GetRequiredService<MaintenanceViewModel>();
        Assert.False(vm.HasRelease);
        await vm.CheckUpdateAsync();Assert.NotNull(vm.AvailableUpdate);Assert.Contains("新版本",vm.UpdateMessage);
        updates.Version=UpdateCheckService.CurrentVersion;
        await vm.CheckUpdateAsync();Assert.True(vm.HasRelease);Assert.Null(vm.AvailableUpdate);Assert.Contains("最新版本",vm.UpdateMessage);
        updates.Version=new(1,0,0);updates.Note="";
        await vm.CheckUpdateAsync();Assert.Contains("比公開版新",vm.UpdateMessage);Assert.Contains("未提供",vm.ReleaseNotes);
        updates.Fail=true;await vm.CheckUpdateAsync();Assert.False(vm.HasRelease);Assert.Null(vm.AvailableUpdate);Assert.Contains("無法檢查",vm.UpdateMessage);Assert.False(vm.Busy);
    }
    [Fact] public Task Settings_render_release_notes_and_download_without_administrator_login()=>MilestoneOneTests.RunSta(async()=>
    {
        var updates=new Updates();var installer=new Installer();var dialogs=new Dialogs();using var host=CreateHost(updates,installer,dialogs);
        var vm=host.Services.GetRequiredService<MaintenanceViewModel>();Assert.False(host.Services.GetRequiredService<AdminSession>().IsAuthenticated);
        var view=new SoftwareUpdateView{DataContext=vm};
        var window=new Window{Content=new ScrollViewer{Content=view,Padding=new Thickness(24),HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled},Width=900,Height=730,ShowInTaskbar=false,ShowActivated=false};
        try
        {
            window.Show();await vm.CheckUpdateAsync();window.UpdateLayout();
            var markdown=(CloudAlarmOverlay.App.Controls.MarkdownView)view.FindName("ReleaseNotes");
            var rendered=new TextRange(markdown.Document.ContentStart,markdown.Document.ContentEnd).Text;
            Assert.Contains("新增功能",rendered);Assert.Contains("提醒設定",rendered);Assert.DoesNotContain("##",rendered);
            Assert.Contains(markdown.Document.Blocks.Cast<Block>(),b=>b is System.Windows.Documents.List);
            Assert.True(((StackPanel)view.FindName("ReleaseDetails")).IsVisible);
            Assert.DoesNotContain(updates.Note,vm.UpdateMessage);
            foreach(var dark in new[]{false,true})
            {
                AdaptiveBrushExtension.Apply(dark);window.UpdateLayout();
                Capture(window,dark?"software-update-dark":"software-update-light");
            }
            await vm.OpenDownloadCommand.ExecuteAsync(null);
            Assert.Equal(1,installer.Downloads);Assert.Equal(0,installer.Installs);Assert.Equal(1,dialogs.Confirmations);
            Assert.True(vm.HasDownloadedUpdate);Assert.Contains("SHA-256",vm.UpdateMessage);
        }
        finally{window.Close();AdaptiveBrushExtension.Apply(false);}
    });
    [Fact] public async Task Declining_install_keeps_verified_download_and_later_install_requires_confirmation()
    {
        var updates=new Updates();var installer=new Installer();var dialogs=new Dialogs();using var host=CreateHost(updates,installer,dialogs);
        var vm=host.Services.GetRequiredService<MaintenanceViewModel>();await vm.CheckUpdateAsync();
        updates.Version=new(100,0,0);await vm.OpenDownloadCommand.ExecuteAsync(null);
        Assert.Equal(new Version(100,0,0),vm.DownloadedUpdate!.Info.LatestVersion);
        Assert.True(vm.InstallUpdateCommand.CanExecute(null));Assert.Equal(0,installer.Installs);
        dialogs.Accept=true;await vm.InstallUpdateCommand.ExecuteAsync(null);
        Assert.Equal(1,installer.Installs);Assert.Equal(2,dialogs.Confirmations);Assert.False(vm.HasDownloadedUpdate);
        Assert.Contains("已啟動安裝",vm.UpdateMessage);Assert.False(vm.Busy);
    }
    [Fact] public async Task Confirming_after_download_installs_only_after_successful_verification()
    {
        var installer=new Installer();var dialogs=new Dialogs{Accept=true};using var host=CreateHost(new Updates(),installer,dialogs);
        var vm=host.Services.GetRequiredService<MaintenanceViewModel>();await vm.CheckUpdateAsync();await vm.OpenDownloadCommand.ExecuteAsync(null);
        Assert.Equal(1,installer.Downloads);Assert.Equal(1,installer.Installs);Assert.Equal(1,dialogs.Confirmations);
        Assert.False(vm.HasDownloadedUpdate);Assert.False(vm.Busy);
    }
    [Fact] public async Task Failed_verification_never_offers_install_and_tampering_invalidates_ready_state()
    {
        var installer=new Installer{FailDownload=true};var dialogs=new Dialogs();using var host=CreateHost(new Updates(),installer,dialogs);
        var vm=host.Services.GetRequiredService<MaintenanceViewModel>();await vm.CheckUpdateAsync();await vm.OpenDownloadCommand.ExecuteAsync(null);
        Assert.Equal(0,dialogs.Confirmations);Assert.Equal(0,installer.Installs);Assert.False(vm.HasDownloadedUpdate);Assert.Contains("SHA-256",vm.UpdateMessage);
        installer.FailDownload=false;await vm.OpenDownloadCommand.ExecuteAsync(null);Assert.True(vm.HasDownloadedUpdate);
        installer.FailInstall=true;dialogs.Accept=true;await vm.InstallUpdateCommand.ExecuteAsync(null);
        Assert.Equal(0,installer.Installs);Assert.False(vm.HasDownloadedUpdate);Assert.Contains("SHA-256",vm.UpdateMessage);Assert.False(vm.Busy);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Cancelling_download_or_changing_source_restores_commands_and_never_offers_install(bool changeSource)
    {
        var installer=new Installer{BlockDownload=true};var dialogs=new Dialogs();using var host=CreateHost(new Updates(),installer,dialogs);
        var vm=host.Services.GetRequiredService<MaintenanceViewModel>();await vm.CheckUpdateAsync();
        var operation=vm.OpenDownloadCommand.ExecuteAsync(null);await installer.Started.Task;
        Assert.True(vm.IsDownloading);Assert.False(vm.CheckUpdateCommand.CanExecute(null));Assert.False(vm.OpenDownloadCommand.CanExecute(null));
        if(changeSource)vm.UpdateUrl="https://example.test/replacement.json";
        else vm.CancelDownloadCommand.Execute(null);
        await operation;
        Assert.False(vm.Busy);Assert.False(vm.IsDownloading);Assert.False(vm.HasDownloadedUpdate);
        Assert.True(vm.CheckUpdateCommand.CanExecute(null));Assert.Equal(0,dialogs.Confirmations);Assert.Contains("已取消下載",vm.UpdateMessage);
    }
    private static IHost CreateHost(Updates updates,Installer? installer=null,Dialogs? dialogs=null)=>new HostBuilder().ConfigureServices(s=>
    {CompositionRoot.ConfigureServices(s);s.AddSingleton<IUpdateCheckService>(updates);s.AddSingleton<IUpdateInstallerService>(installer??new());s.AddSingleton<IUserDialogs>(dialogs??new());}).Build();
    private sealed class Updates:IUpdateCheckService
    {
        public Version Version=new(99,0,0);public bool Fail;
        public string Note="## 新增功能\n- **提醒設定**：可調整通知時間。\n- 顯示版本更新內容。\n\n## 改善\n修正重複提示。";
        public Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public Task<UpdateInfo> GetManifestAsync(CancellationToken cancellationToken=default)=>Fail?Task.FromException<UpdateInfo>(new IOException("測試連線中斷")):Task.FromResult(new UpdateInfo(Version,new("https://github.com/owner/repo/releases/download/v99/setup.exe"),Note){Sha256=new string('a',64)});
    }
    private sealed class Installer:IUpdateInstallerService
    {
        public int Downloads,Installs;public bool FailDownload,FailInstall,BlockDownload;
        public TaskCompletionSource Started {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<DownloadedUpdate> DownloadAsync(UpdateInfo update,IProgress<UpdateDownloadProgress>? progress=null,CancellationToken ct=default)
        {
            Downloads++;Started.TrySetResult();if(BlockDownload)await Task.Delay(Timeout.Infinite,ct);
            if(FailDownload)throw new InvalidDataException("SHA-256 驗證失敗。");
            return new(update,Path.Combine(Path.GetTempPath(),"test-update.exe"));
        }
        public Task InstallAsync(DownloadedUpdate update,CancellationToken ct=default)
        {if(FailInstall)throw new InvalidDataException("SHA-256 驗證失敗。");Installs++;return Task.CompletedTask;}
    }
    private sealed class Dialogs:IUserDialogs
    {
        public bool Accept;public int Confirmations;
        public bool Confirm(string message){Confirmations++;Assert.Contains("SHA-256",message);return Accept;}
        public void Edit(AlarmTask? task,bool copy)=>throw new NotSupportedException();
        public void Export(string contents)=>throw new NotSupportedException();
    }
    private static void Capture(Window window,string name)
    {
        var content=(FrameworkElement)window.Content;var dpi=VisualTreeHelper.GetDpi(content);
        var bmp=new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth*dpi.DpiScaleX),(int)Math.Ceiling(content.ActualHeight*dpi.DpiScaleY),96*dpi.DpiScaleX,96*dpi.DpiScaleY,PixelFormats.Pbgra32);
        var drawing=new DrawingVisual();using(var dc=drawing.RenderOpen()){dc.DrawRectangle(window.Background,null,new Rect(0,0,content.ActualWidth,content.ActualHeight));dc.DrawRectangle(new VisualBrush(content),null,new Rect(0,0,content.ActualWidth,content.ActualHeight));}bmp.Render(drawing);
        var folder=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../artifacts/screenshots"));Directory.CreateDirectory(folder);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bmp));using var file=File.Create(Path.Combine(folder,name+".png"));png.Save(file);
    }
}

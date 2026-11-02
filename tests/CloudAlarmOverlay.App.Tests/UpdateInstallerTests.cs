using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Services;
using CloudAlarmOverlay.Infrastructure.Services;

namespace CloudAlarmOverlay.App.Tests;

public sealed class UpdateInstallerTests:IDisposable,IAppPaths
{
    public string DataDirectory {get;}=Path.Combine(Path.GetTempPath(),"UpdateInstallerTests",Guid.NewGuid().ToString("N"));
    public string DatabasePath=>Path.Combine(DataDirectory,"test.db");
    private static readonly byte[] Payload="verified installer test bytes"u8.ToArray();
    private static UpdateInfo Release()=>new(new(99,0,0),new("https://github.com/owner/repo/releases/download/v99.0.0/setup.exe"),"Notes")
        {Sha256=Convert.ToHexString(SHA256.HashData(Payload))};
    [Theory]
    [InlineData("http://github.com/owner/repo/releases/download/v99/setup.exe")]
    [InlineData("https://github.com.evil.test/owner/repo/releases/download/v99/setup.exe")]
    [InlineData("https://evil.test/setup.exe")]
    [InlineData("https://github.com/owner/repo/blob/main/setup.exe")]
    [InlineData("https://user:pass@github.com/owner/repo/releases/download/v99/setup.exe")]
    [InlineData("https://github.com:8443/owner/repo/releases/download/v99/setup.exe")]
    [InlineData("https://github.com/owner/repo/releases/download/v99/setup.zip")]
    [InlineData("https://release-assets.githubusercontent.com/github-production-release-asset/setup.exe")]
    public async Task Rejects_untrusted_initial_urls_before_any_request(string url)
    {
        var handler=new Handler(_=>new(HttpStatusCode.OK){Content=new ByteArrayContent(Payload)});
        using var service=new UpdateInstallerService(this,new Launcher(),handler);
        await Assert.ThrowsAsync<InvalidDataException>(()=>service.DownloadAsync(Release() with{DownloadUrl=new(url)}));
        Assert.Empty(handler.Requests);Assert.False(Directory.Exists(DataDirectory));
    }
    [Theory] [InlineData("")] [InlineData("1234")] [InlineData("z234567890123456789012345678901234567890123456789012345678901234")]
    public async Task Requires_valid_hash_before_transport(string hash)
    {
        var handler=new Handler(_=>new(HttpStatusCode.OK){Content=new ByteArrayContent(Payload)});
        using var service=new UpdateInstallerService(this,new Launcher(),handler);
        await Assert.ThrowsAsync<InvalidDataException>(()=>service.DownloadAsync(Release() with{Sha256=hash}));
        Assert.Empty(handler.Requests);
    }
    [Fact] public async Task Follows_only_github_asset_redirects_reports_progress_and_waits_for_install()
    {
        var handler=new Handler(uri=>uri.Host=="github.com"?Redirect("https://release-assets.githubusercontent.com/github-production-release-asset/123/setup.exe?signature=example")
            :new(HttpStatusCode.OK){Content=new ByteArrayContent(Payload)});
        var launcher=new Launcher();using var service=new UpdateInstallerService(this,launcher,handler);
        var progress=new ProgressCollector();var downloaded=await service.DownloadAsync(Release(),progress);
        Assert.Equal(2,handler.Requests.Count);Assert.Empty(launcher.Launched);
        Assert.Equal(Payload,await File.ReadAllBytesAsync(downloaded.FilePath));Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(downloaded.FilePath)!,"*.part"));
        Assert.Contains(progress.Values,p=>p.IsVerifying&&p.BytesReceived==Payload.Length&&p.TotalBytes==Payload.Length);
        await service.InstallAsync(downloaded);Assert.Equal(downloaded.FilePath,Assert.Single(launcher.Launched));
    }
    [Theory]
    [InlineData("https://evil.test/setup.exe")]
    [InlineData("http://release-assets.githubusercontent.com/github-production-release-asset/setup.exe")]
    [InlineData("https://release-assets.githubusercontent.com.evil.test/github-production-release-asset/setup.exe")]
    [InlineData("https://raw.githubusercontent.com/owner/repo/main/setup.exe")]
    public async Task Blocks_redirect_target_before_contacting_it(string url)
    {
        var handler=new Handler(_=>Redirect(url));using var service=new UpdateInstallerService(this,new Launcher(),handler);
        await Assert.ThrowsAsync<InvalidDataException>(()=>service.DownloadAsync(Release()));
        Assert.Single(handler.Requests);Assert.Empty(Directory.GetFiles(Path.Combine(DataDirectory,"Updates")));
    }
    [Fact] public async Task Redirect_loops_are_bounded()
    {
        var handler=new Handler(_=>Redirect(Release().DownloadUrl.AbsoluteUri));using var service=new UpdateInstallerService(this,new Launcher(),handler);
        await Assert.ThrowsAsync<InvalidDataException>(()=>service.DownloadAsync(Release()));Assert.Equal(6,handler.Requests.Count);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Hash_mismatch_or_truncated_body_deletes_partial_file(bool truncated)
    {
        var handler=new Handler(_=>
        {
            var content=new ByteArrayContent(Payload);if(truncated)content.Headers.ContentLength=Payload.Length+1;
            return new(HttpStatusCode.OK){Content=content};
        });
        var launcher=new Launcher();using var service=new UpdateInstallerService(this,launcher,handler);
        await Assert.ThrowsAsync<InvalidDataException>(()=>service.DownloadAsync(truncated?Release():Release() with{Sha256=new string('0',64)}));
        Assert.Empty(launcher.Launched);Assert.Empty(Directory.GetFiles(Path.Combine(DataDirectory,"Updates")));
    }
    [Fact] public async Task Cancellation_cleans_partial_download()
    {
        using var cancellation=new CancellationTokenSource();
        var handler=new Handler(_=>new(HttpStatusCode.OK){Content=new ByteArrayContent(Payload)});
        using var service=new UpdateInstallerService(this,new Launcher(),handler);
        var progress=new ProgressCollector{Reported=_=>cancellation.Cancel()};
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>service.DownloadAsync(Release(),progress,cancellation.Token));
        Assert.Empty(Directory.GetFiles(Path.Combine(DataDirectory,"Updates")));
    }
    [Fact] public async Task Unknown_content_length_downloads_with_hash_verification()
    {
        var handler=new Handler(_=>new(HttpStatusCode.OK){Content=new UnknownLengthContent()});
        using var service=new UpdateInstallerService(this,new Launcher(),handler);var progress=new ProgressCollector();
        var downloaded=await service.DownloadAsync(Release(),progress);Assert.Equal(Payload,await File.ReadAllBytesAsync(downloaded.FilePath));
        Assert.All(progress.Values,p=>Assert.Null(p.TotalBytes));
    }
    [Fact] public async Task Oversized_download_is_rejected_before_creating_a_file()
    {
        var handler=new Handler(_=>
        {
            var content=new ByteArrayContent(Payload);content.Headers.ContentLength=513L*1024*1024;
            return new(HttpStatusCode.OK){Content=content};
        });
        using var service=new UpdateInstallerService(this,new Launcher(),handler);
        await Assert.ThrowsAsync<InvalidDataException>(()=>service.DownloadAsync(Release()));Assert.Empty(Directory.GetFiles(Path.Combine(DataDirectory,"Updates")));
    }
    [Fact] public async Task Tampering_after_download_is_detected_before_launch()
    {
        var launcher=new Launcher();using var service=new UpdateInstallerService(this,launcher,new Handler(_=>new(HttpStatusCode.OK){Content=new ByteArrayContent(Payload)}));
        var downloaded=await service.DownloadAsync(Release());await File.WriteAllTextAsync(downloaded.FilePath,"changed");
        await Assert.ThrowsAsync<InvalidDataException>(()=>service.InstallAsync(downloaded));Assert.Empty(launcher.Launched);
    }
    [Fact] public async Task External_paths_are_never_launched()
    {
        var launcher=new Launcher();using var service=new UpdateInstallerService(this,launcher,new Handler(_=>throw new NotSupportedException()));
        await Assert.ThrowsAsync<InvalidDataException>(()=>service.InstallAsync(new(Release(),Path.Combine(DataDirectory,"outside.exe"))));Assert.Empty(launcher.Launched);
    }
    private static HttpResponseMessage Redirect(string url)
    {var response=new HttpResponseMessage(HttpStatusCode.Found);response.Headers.Location=new(url);return response;}
    private sealed class Handler(Func<Uri,HttpResponseMessage> respond):HttpMessageHandler
    {
        public List<Uri> Requests {get;}=[];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {ct.ThrowIfCancellationRequested();Requests.Add(request.RequestUri!);return Task.FromResult(respond(request.RequestUri!));}
    }
    private sealed class Launcher:IInstallerLauncher
    {public List<string> Launched {get;}=[];public void Launch(string filePath)=>Launched.Add(filePath);}
    private sealed class ProgressCollector:IProgress<UpdateDownloadProgress>
    {public List<UpdateDownloadProgress> Values {get;}=[];public Action<UpdateDownloadProgress>? Reported;public void Report(UpdateDownloadProgress value){Values.Add(value);Reported?.Invoke(value);}}
    private sealed class UnknownLengthContent:HttpContent
    {
        protected override bool TryComputeLength(out long length){length=0;return false;}
        protected override Task SerializeToStreamAsync(Stream stream,TransportContext? context)=>stream.WriteAsync(Payload).AsTask();
    }
    public void Dispose(){if(Directory.Exists(DataDirectory))Directory.Delete(DataDirectory,true);}
}

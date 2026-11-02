using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Services;

namespace CloudAlarmOverlay.Infrastructure.Services;

public sealed class InstallerLauncher:IInstallerLauncher
{
    public void Launch(string filePath)
    {
        using var process=Process.Start(new ProcessStartInfo(filePath){UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(filePath)});
        if(process is null)throw new InvalidOperationException("無法啟動安裝程式。");
    }
}

public sealed class UpdateInstallerService:IUpdateInstallerService,IDisposable
{
    private const long MaximumBytes=512L*1024*1024;
    private readonly string directory;
    private readonly IInstallerLauncher launcher;
    private readonly HttpClient client;
    public UpdateInstallerService(IAppPaths paths,IInstallerLauncher launcher)
        :this(paths,launcher,new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false}) { }
    public UpdateInstallerService(IAppPaths paths,IInstallerLauncher launcher,HttpMessageHandler handler)
    {
        directory=Path.GetFullPath(Path.Combine(paths.DataDirectory,"Updates"));
        this.launcher=launcher;
        client=new HttpClient(handler){Timeout=Timeout.InfiniteTimeSpan};
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CloudAlarmOverlay/"+UpdateCheckService.CurrentVersion.ToString(3));
    }
    public async Task<DownloadedUpdate> DownloadAsync(UpdateInfo update,IProgress<UpdateDownloadProgress>? progress=null,CancellationToken ct=default)
    {
        UpdateDownloadPolicy.Validate(update);
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromMinutes(30));
        var token=deadline.Token;
        Directory.CreateDirectory(directory);
        var part=Path.Combine(directory,Guid.NewGuid().ToString("N")+".part");
        var completed=Path.ChangeExtension(part,".exe");
        try
        {
            using var response=await GetResponseAsync(update.DownloadUrl,token).ConfigureAwait(false);
            var length=response.Content.Headers.ContentLength;
            if(length is <=0 or >MaximumBytes)throw new InvalidDataException("安裝檔大小無效或超過 512 MB。");
            await using(var input=await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
            await using(var output=new FileStream(part,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true))
            {
                var buffer=new byte[65536];long received=0;var lastReport=Stopwatch.GetTimestamp();
                progress?.Report(new(0,length));
                int count;
                while((count=await input.ReadAsync(buffer,token).ConfigureAwait(false))>0)
                {
                    received+=count;
                    if(received>MaximumBytes||length is {} total&&received>total)throw new InvalidDataException("安裝檔大小與伺服器回應不符。");
                    await output.WriteAsync(buffer.AsMemory(0,count),token).ConfigureAwait(false);
                    if(Stopwatch.GetElapsedTime(lastReport)>TimeSpan.FromMilliseconds(150))
                    {progress?.Report(new(received,length));lastReport=Stopwatch.GetTimestamp();}
                }
                if(received==0||length is {} expected&&received!=expected)throw new InvalidDataException("安裝檔下載不完整，請重新下載。");
                progress?.Report(new(received,length,true));
            }
            await using(var downloaded=File.OpenRead(part))await VerifyAsync(downloaded,update.Sha256,token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            File.Move(part,completed);
            return new(update,completed);
        }
        catch
        {
            if(File.Exists(part))File.Delete(part);
            if(File.Exists(completed))File.Delete(completed);
            throw;
        }
    }
    private async Task<HttpResponseMessage> GetResponseAsync(Uri initial,CancellationToken ct)
    {
        var uri=initial;
        for(var hop=0;hop<=5;hop++)
        {
            UpdateDownloadPolicy.ValidateUrl(uri,hop>0);
            using var request=new HttpRequestMessage(HttpMethod.Get,uri);
            var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct).ConfigureAwait(false);
            if(response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                var location=response.Headers.Location;response.Dispose();
                if(location is null)throw new InvalidDataException("GitHub 下載重新導向缺少目的地。");
                uri=location.IsAbsoluteUri?location:new Uri(uri,location);
                continue;
            }
            try{response.EnsureSuccessStatusCode();return response;}
            catch{response.Dispose();throw;}
        }
        throw new InvalidDataException("GitHub 下載重新導向次數過多。");
    }
    public async Task InstallAsync(DownloadedUpdate update,CancellationToken ct=default)
    {
        UpdateDownloadPolicy.Validate(update.Info);
        var path=Path.GetFullPath(update.FilePath);
        if(!string.Equals(Path.GetDirectoryName(path),directory,StringComparison.OrdinalIgnoreCase)||Path.GetExtension(path)!=".exe")
            throw new InvalidDataException("安裝檔不在程式下載目錄內，拒絕執行。");
        // Recheck immediately before launch; keep a read-only shared handle open to
        // prevent replacement or writes between verification and process creation.
        await using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,65536,true);
        await VerifyAsync(stream,update.Info.Sha256,ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        launcher.Launch(path);
    }
    private static async Task VerifyAsync(Stream stream,string expected,CancellationToken ct)
    {
        var actual=await SHA256.HashDataAsync(stream,ct).ConfigureAwait(false);
        if(!CryptographicOperations.FixedTimeEquals(actual,Convert.FromHexString(expected)))
            throw new InvalidDataException("SHA-256 驗證失敗：安裝檔內容不符，已拒絕執行，請重新下載。");
    }
    public void Dispose()=>client.Dispose();
}

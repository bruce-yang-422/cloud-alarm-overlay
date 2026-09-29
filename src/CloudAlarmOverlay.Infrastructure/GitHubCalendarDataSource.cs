using CloudAlarmOverlay.Core.Models;
using CloudAlarmOverlay.Core.Services;

namespace CloudAlarmOverlay.Infrastructure;

public sealed class GitHubCalendarDataSource:ICalendarDataSource,IDisposable
{
    private readonly HttpClient http;
    public GitHubCalendarDataSource():this(new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(20),MaxResponseContentBufferSize=4*1024*1024}){}
    public GitHubCalendarDataSource(HttpClient http)=>this.http=http;
    public async Task<string> DownloadAsync(string url,CancellationToken ct=default)
    {
        url=CalendarUpdateOptions.NormalizeUrl(url);
        using var response=await http.GetAsync(url,ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if(response.Content.Headers.ContentType?.MediaType=="text/html")throw new FormatException("來源是網頁，請使用 GitHub 原始 JSON 網址。");
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }
    public void Dispose()=>http.Dispose();
}

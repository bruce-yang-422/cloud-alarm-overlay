using System.Net;
using System.Net.Http;
using CloudAlarmOverlay.Infrastructure;

namespace CloudAlarmOverlay.App.Tests;

public sealed class SheetCsvClientTests
{
    private const string Csv = "Id,Title\r\n編號,標題\r\n1,提醒";

    [Fact]
    public async Task Successful_export_keeps_content_and_does_not_retry()
    {
        using var handler = new Handler((_, _) => Task.FromResult(Response()));
        using var client = new SheetCsvClient(new HttpClient(handler));
        Assert.Equal(Csv, await client.DownloadAsync("sheet id", "123"));
        Assert.Equal("https://docs.google.com/spreadsheets/d/sheet%20id/export?format=csv&gid=123", Assert.Single(handler.Urls));
    }

    [Theory]
    [InlineData(408)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    [InlineData(0)]
    public async Task Transient_failure_retries_same_tab_with_fresh_export_url(int status)
    {
        using var handler = new Handler((call, _) => call == 1
            ? status == 0 ? Task.FromException<HttpResponseMessage>(new HttpRequestException("offline"))
                : Task.FromResult(new HttpResponseMessage((HttpStatusCode)status))
            : Task.FromResult(Response()));
        using var client = new SheetCsvClient(new HttpClient(handler));
        Assert.Equal(Csv, await client.DownloadAsync("sheet", "123"));
        Assert.Equal(2, handler.Urls.Count);
        Assert.StartsWith(handler.Urls[0] + "&cachebust=", handler.Urls[1]);
    }

    [Fact]
    public async Task Http_timeout_retries_with_a_new_request_timeout()
    {
        using var handler = new Handler(async (call, ct) =>
        {
            if (call == 1) await Task.Delay(Timeout.Infinite, ct);
            return Response();
        });
        using var client = new SheetCsvClient(new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(100) });
        Assert.Equal(Csv, await client.DownloadAsync("sheet", "123"));
        Assert.Equal(2, handler.Urls.Count);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(429)]
    public async Task Permanent_failure_or_rate_limit_does_not_retry(int status)
    {
        using var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)));
        using var client = new SheetCsvClient(new HttpClient(handler));
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.DownloadAsync("sheet", "123"));
        Assert.Equal((HttpStatusCode)status, error.StatusCode);
        Assert.Single(handler.Urls);
    }

    [Fact]
    public async Task Repeated_failure_stops_after_one_retry()
    {
        using var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)));
        using var client = new SheetCsvClient(new HttpClient(handler));
        await Assert.ThrowsAsync<HttpRequestException>(() => client.DownloadAsync("sheet", "123"));
        Assert.Equal(2, handler.Urls.Count);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Caller_cancellation_stops_initial_request_or_retry(int cancelOnCall)
    {
        using var cancellation = new CancellationTokenSource();
        using var handler = new Handler(async (call, ct) =>
        {
            if (call == cancelOnCall)
            {
                cancellation.Cancel();
                await Task.Delay(Timeout.Infinite, ct);
            }
            return new HttpResponseMessage(HttpStatusCode.BadGateway);
        });
        using var client = new SheetCsvClient(new HttpClient(handler));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.DownloadAsync("sheet", "123", cancellation.Token));
        Assert.Equal(cancelOnCall, handler.Urls.Count);
    }

    [Theory]
    [InlineData("  <html>Sign in</html>", 1)]
    [InlineData("\uFEFF <html>Sign in</html>", 2)]
    public async Task Login_page_is_rejected_on_either_attempt(string html, int htmlOnCall)
    {
        using var handler = new Handler((call, _) => Task.FromResult(call == htmlOnCall
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html) }
            : new HttpResponseMessage(HttpStatusCode.BadGateway)));
        using var client = new SheetCsvClient(new HttpClient(handler));
        await Assert.ThrowsAsync<FormatException>(() => client.DownloadAsync("sheet", "123"));
        Assert.Equal(htmlOnCall, handler.Urls.Count);
    }

    private static HttpResponseMessage Response() => new(HttpStatusCode.OK) { Content = new StringContent(Csv) };

    private sealed class Handler(Func<int, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Urls.Add(request.RequestUri!.AbsoluteUri);
            return respond(Urls.Count, ct);
        }
    }
}

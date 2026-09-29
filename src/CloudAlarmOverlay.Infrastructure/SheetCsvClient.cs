using CloudAlarmOverlay.Core.Services;
namespace CloudAlarmOverlay.Infrastructure;

internal sealed class SheetCsvClient : ISheetCsvClient, IDisposable
{
    private readonly HttpClient http;
    public SheetCsvClient() : this(new HttpClient { Timeout = TimeSpan.FromSeconds(20), MaxResponseContentBufferSize = 5 * 1024 * 1024 }) { }
    internal SheetCsvClient(HttpClient http) => this.http = http;

    public async Task<string> DownloadAsync(string spreadsheetId, string gid, CancellationToken ct = default)
    {
        var url = $"https://docs.google.com/spreadsheets/d/{Uri.EscapeDataString(spreadsheetId)}/export?format=csv&gid={Uri.EscapeDataString(gid)}";
        try
        {
            return await ReadAsync(url, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && IsTransient(ex))
        {
            // A cached export/redirect can stall even while the spreadsheet opens normally.
            // Retry once with a fresh URL, keeping the original CSV export semantics.
            ct.ThrowIfCancellationRequested();
            return await ReadAsync(url + "&cachebust=" + Guid.NewGuid().ToString("N"), ct).ConfigureAwait(false);
        }
    }

    private static bool IsTransient(Exception error) => error switch
    {
        OperationCanceledException => true, // HttpClient timeout; caller cancellation is excluded above.
        HttpRequestException request => request.StatusCode is null
            or System.Net.HttpStatusCode.RequestTimeout
            or System.Net.HttpStatusCode.InternalServerError
            or System.Net.HttpStatusCode.BadGateway
            or System.Net.HttpStatusCode.ServiceUnavailable
            or System.Net.HttpStatusCode.GatewayTimeout,
        _ => false
    };

    private async Task<string> ReadAsync(string url, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (text.TrimStart('\uFEFF').TrimStart().StartsWith("<", StringComparison.Ordinal)) throw new FormatException("來源傳回網頁，請確認試算表可公開檢視及 GID 正確。");
        return text;
    }
    public void Dispose() => http.Dispose();
}

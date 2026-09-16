using CloudAlarmOverlay.Core.Services;
namespace CloudAlarmOverlay.Infrastructure;

internal sealed class SheetCsvClient : ISheetCsvClient, IDisposable
{
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(20), MaxResponseContentBufferSize = 5 * 1024 * 1024 };
    public async Task<string> DownloadAsync(string spreadsheetId, string gid, CancellationToken ct = default)
    {
        var url = $"https://docs.google.com/spreadsheets/d/{Uri.EscapeDataString(spreadsheetId)}/export?format=csv&gid={Uri.EscapeDataString(gid)}";
        using var response = await http.GetAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (text.TrimStart().StartsWith("<", StringComparison.Ordinal)) throw new FormatException("來源傳回網頁，請確認試算表可公開檢視及 GID 正確。");
        return text;
    }
    public void Dispose() => http.Dispose();
}

using System.Diagnostics;
using System.IO;
using System.Net;
using CloudAlarmOverlay.Core.Services;
using Microsoft.Extensions.Logging;
namespace CloudAlarmOverlay.App.Services;

// 提供「任務產生器」(task_builder_tailwind.html) 的本機服務：
// 1. 靜態頁面本身：瀏覽器以 file:// 開啟時，fetch() 會被跨源限制擋下，因此改由本程式起
//    一個 HttpListener，讓使用者用系統瀏覽器透過 http://localhost 開啟。
// 2. Sheet 資料代理：Sheet 的 SpreadsheetId／GID 屬於公司內部設定（管理者頁 → 同步來源），
//    不寫死在 HTML 裡、不外流；頁面改為呼叫本機 /api/... 端點，由本程式讀取管理者已設定好的
//    SyncOptions 並代為向 Google 要 CSV，回傳純資料給頁面。
public sealed class TaskBuilderHostService(ILogger<TaskBuilderHostService> logger,SyncConfiguration syncConfiguration,ISheetCsvClient csvClient)
{
    private const string FileName = "task_builder_tailwind.html";
    private HttpListener? listener;
    private int port;

    public void OpenInBrowser()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, FileName);
            if (!File.Exists(path))
            {
                logger.LogError("找不到任務產生器頁面：{Path}", path);
                System.Windows.MessageBox.Show($"找不到任務產生器頁面檔案：\n{path}", "Cloud Alarm Overlay",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                return;
            }

            EnsureListenerStarted(path);
            Process.Start(new ProcessStartInfo($"http://localhost:{port}/{FileName}") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "開啟任務產生器失敗");
            System.Windows.MessageBox.Show($"無法開啟任務產生器：\n{ex.Message}", "Cloud Alarm Overlay",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private void EnsureListenerStarted(string filePath)
    {
        if (listener is { IsListening: true }) return;

        Exception? lastError = null;
        foreach (var candidate in new[] { 8843, 8844, 8845, 0 })
        {
            try
            {
                var trial = new HttpListener();
                var usedPort = candidate == 0 ? GetFreeTcpPort() : candidate;
                trial.Prefixes.Add($"http://localhost:{usedPort}/");
                trial.Start();
                listener = trial;
                port = usedPort;
                lastError = null;
                break;
            }
            catch (Exception ex) { lastError = ex; }
        }
        if (listener is null) throw lastError ?? new InvalidOperationException("無法啟動本機伺服器");

        _ = Task.Run(() => AcceptLoopAsync(listener, filePath));
    }

    private static int GetFreeTcpPort()
    {
        var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var freePort = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return freePort;
    }

    private async Task AcceptLoopAsync(HttpListener server, string filePath)
    {
        while (server.IsListening)
        {
            HttpListenerContext context;
            try { context = await server.GetContextAsync(); }
            catch (Exception) { break; } // listener disposed/stopped

            _ = Task.Run(async () =>
            {
                try { await RouteAsync(context, filePath); }
                catch (Exception ex) { logger.LogError(ex, "任務產生器伺服器處理請求失敗"); }
            });
        }
    }

    private async Task RouteAsync(HttpListenerContext context, string filePath)
    {
        var path = context.Request.Url?.AbsolutePath ?? "/";
        if (path == "/api/tasks-csv") { await ServeCsvAsync(context, sheetB: true); return; }
        if (path == "/api/employees-csv") { await ServeCsvAsync(context, sheetB: false); return; }
        await ServeFileAsync(context, filePath);
    }

    private async Task ServeCsvAsync(HttpListenerContext context, bool sheetB)
    {
        var response = context.Response;
        try
        {
            var options = await syncConfiguration.LoadAsync();
            var (spreadsheetId, gid) = sheetB ? (options.SheetBId, options.TasksBGid) : (options.SheetAId, options.EmployeesGid);
            if (string.IsNullOrWhiteSpace(spreadsheetId) || string.IsNullOrWhiteSpace(gid))
            {
                response.StatusCode = 409;
                await WriteTextAsync(response, "尚未在管理者頁「同步來源」設定 " + (sheetB ? "Sheet B" : "Sheet A") + " 的連線資訊。");
                return;
            }

            var csv = await csvClient.DownloadAsync(spreadsheetId, gid);
            response.ContentType = "text/csv; charset=utf-8";
            await WriteTextAsync(response, csv);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "任務產生器讀取 Sheet 資料失敗");
            response.StatusCode = 502;
            await WriteTextAsync(response, "讀取雲端資料失敗：" + ex.Message);
        }
        finally { response.Close(); }
    }

    private static async Task WriteTextAsync(HttpListenerResponse response, string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
    }

    private static async Task ServeFileAsync(HttpListenerContext context, string filePath)
    {
        var response = context.Response;
        try
        {
            var bytes = await File.ReadAllBytesAsync(filePath);
            response.ContentType = "text/html; charset=utf-8";
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes);
        }
        finally { response.Close(); }
    }
}

using CloudAlarmOverlay.Core.Models;
namespace CloudAlarmOverlay.Core.Services;

public interface IGoogleWorkspace
{
    event Action? Changed;
    Task<GoogleWorkspaceSnapshot> GetAsync(CancellationToken ct=default);
    Task ImportClientAsync(string json,CancellationToken ct=default);
    Task UseBuiltInClientAsync(CancellationToken ct=default);
    Task SignInAsync(string label,bool sheets,bool calendar,Action<string> openBrowser,CancellationToken ct=default,bool writeSheets=false);
    Task SignOutAsync(string accountId,bool revoke,CancellationToken ct=default);
    Task RenameAccountAsync(string accountId,string name,CancellationToken ct=default);
    Task<IReadOnlyList<GoogleResource>> ListCalendarsAsync(string accountId,CancellationToken ct=default);
    Task<IReadOnlyList<GoogleResource>> ListTabsAsync(string accountId,string spreadsheet,CancellationToken ct=default);
    Task SaveSourceAsync(GoogleSource source,CancellationToken ct=default);
    Task RemoveSourceAsync(string id,CancellationToken ct=default);
    Task SyncAsync(string? sourceId=null,bool automatic=false,CancellationToken ct=default);
    Task<GoogleTaskDraft> ReadTaskAsync(string sourceId,string externalId,CancellationToken ct=default);
    Task WriteTaskAsync(string draftToken,IReadOnlyDictionary<string,string> fields,bool delete,CancellationToken ct=default);
}

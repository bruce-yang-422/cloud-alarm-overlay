using System.Text.Json;
using CloudAlarmOverlay.Core.Models;

namespace CloudAlarmOverlay.Infrastructure.Google;

internal sealed partial class GoogleWorkspace
{
    private sealed record EditSnapshot(GoogleSource Source,string ExternalId,string[][] Rows,DateTimeOffset At);
    private readonly Dictionary<string,EditSnapshot> drafts=[];
    private static readonly HashSet<string> EditableColumns=["Id","Time","Title","Description","Level","Enabled","RequireAck","Recurrence","SkipOnHoliday","TargetDeviceOrName","ExcludeDeviceOrName"];
    private async Task<string[][]> ReadRowsAsync(GoogleVaultState state,GoogleSource source,CancellationToken ct)
    {
        var tab=(await TabsAsync(state,source.AccountId,source.ResourceId,ct)).SingleOrDefault(t=>t.Id==source.TabId)??throw new InvalidOperationException("工作表已移除。");
        var range="'"+tab.Name.Replace("'","''")+"'";
        var endpoint=$"https://sheets.googleapis.com/v4/spreadsheets/{GoogleApi.Encode(source.ResourceId)}/values/{GoogleApi.Encode(range)}";
        var formulas=await GetAsync(state,source.AccountId,endpoint+"?valueRenderOption=FORMULA",ct);
        var json=await GetAsync(state,source.AccountId,endpoint+"?valueRenderOption=FORMATTED_VALUE",ct);
        if(!json.TryGetProperty("values",out var values)||values.GetArrayLength()<2)throw new InvalidOperationException("工作表缺少標題與說明列。");
        // Reject formulas in managed columns for this editor; never convert or overwrite them silently.
        var rows=values.EnumerateArray().Select(r=>r.EnumerateArray().Select(v=>v.ToString()).ToArray()).ToArray();
        var headers=rows[0];
        if(headers.Length>100||rows.Length>10000)throw new InvalidOperationException("線上編輯上限為 100 欄、10000 列。");
        if(!formulas.TryGetProperty("values",out var formulaRows)||formulaRows.EnumerateArray().Skip(2).Any(r=>r.EnumerateArray().Where((v,i)=>i<headers.Length&&EditableColumns.Contains(headers[i])).Any(v=>v.ToString().StartsWith('='))))throw new InvalidOperationException("Tasks 資料含公式，請在 Google Sheets 編輯；程式不會覆蓋公式。");
        // Existing template parser validates the original grid before allowing a mutation.
        parser.ParseTasks(ToCsv(JsonSerializer.SerializeToElement(new{values=rows})),source.CacheSource);
        return rows;
    }
    public async Task<GoogleTaskDraft> ReadTaskAsync(string sourceId,string externalId,CancellationToken ct=default)
    {
        await gate.WaitAsync(ct);
        try
        {
            var state=await ReadStateAsync(ct);var source=state.Sources.Single(s=>s.Id==sourceId);
            if(source.Kind!="Sheet")throw new ArgumentException("日曆來源不支援寫回。");
            var rows=await ReadRowsAsync(state,source,ct);var idColumn=Array.IndexOf(rows[0],"Id");
            var isNew=string.IsNullOrWhiteSpace(externalId);var id=isNew?Guid.NewGuid().ToString("N"):externalId.Trim();
            var row=rows.Skip(2).SingleOrDefault(r=>r.Length>idColumn&&r[idColumn]==id);
            if(!isNew&&row is null)throw new InvalidOperationException("找不到任務 ID，請重新載入或選擇新增。");
            var fields=rows[0].Select((name,index)=>(name,index)).Where(x=>EditableColumns.Contains(x.name))
                .ToDictionary(x=>x.name,x=>row is not null&&row.Length>x.index?row[x.index]:"");
            fields["Id"]=id;
            if(isNew){fields["Time"]=clock.GetLocalNow().AddHours(1).ToString("yyyy-MM-dd HH:mm");fields["Enabled"]="TRUE";}
            foreach(var expired in drafts.Where(x=>clock.GetUtcNow()-x.Value.At>TimeSpan.FromMinutes(15)).Select(x=>x.Key).ToArray())drafts.Remove(expired);
            if(drafts.Count>=20)drafts.Remove(drafts.Keys.First());
            var token=Guid.NewGuid().ToString("N");drafts[token]=new(source,id,rows,clock.GetUtcNow());
            return new(token,source.Name,id,row is not null,fields);
        }
        finally{gate.Release();}
    }
    public async Task WriteTaskAsync(string draftToken,IReadOnlyDictionary<string,string> fields,bool delete,CancellationToken ct=default)
    {
        await gate.WaitAsync(ct);
        try
        {
            if(!drafts.TryGetValue(draftToken,out var draft)||clock.GetUtcNow()-draft.At>TimeSpan.FromMinutes(15))throw new InvalidOperationException("編輯預覽已過期，請重新讀取任務。");
            var state=await ReadStateAsync(ct);var source=state.Sources.SingleOrDefault(s=>s.Id==draft.Source.Id);
            if(source is null||source.Kind!="Sheet"||!source.AllowWrite||!source.Enabled||source.AccountId!=draft.Source.AccountId||source.ResourceId!=draft.Source.ResourceId||source.TabId!=draft.Source.TabId)
                throw new InvalidOperationException("來源設定已變更或未開啟寫回，請儲存設定後重新讀取。");
            var account=state.Accounts.Single(a=>a.Id==source.AccountId);
            if(!account.Scope.Split(' ').Contains(GoogleApi.SheetsWrite))throw new InvalidOperationException("此帳號只有讀取授權，請勾選「同時申請 Sheets 編輯權限」後重新登入。");
            var fresh=await ReadRowsAsync(state,source,ct);
            if(JsonSerializer.Serialize(fresh)!=JsonSerializer.Serialize(draft.Rows))throw new InvalidOperationException("雲端資料已變更（可能有他人編輯或排序），請重新讀取再合併修改。");
            if(!fields.TryGetValue("Id",out var externalId)||externalId!=draft.ExternalId)throw new ArgumentException("任務 ID 不可變更。");
            var headers=fresh[0];var idColumn=Array.IndexOf(headers,"Id");
            var index=Array.FindIndex(fresh,2,r=>r.Length>idColumn&&r[idColumn]==externalId);
            if(delete&&index<2)throw new InvalidOperationException("新增中的任務不能刪除。");
            var requests=new List<object>();var tabId=int.Parse(source.TabId,System.Globalization.CultureInfo.InvariantCulture);
            var target=new string[headers.Length];
            if(index>=2)Array.Copy(fresh[index],target,Math.Min(target.Length,fresh[index].Length));
            for(var i=0;i<target.Length;i++)target[i]??="";
            foreach(var (name,value) in fields)
            {
                if(!EditableColumns.Contains(name)||Array.IndexOf(headers,name)<0)throw new ArgumentException("不可修改未知欄位。");
                target[Array.IndexOf(headers,name)]=value;
            }
            if(!delete)
            {
                var proposed=new List<string[]>(fresh);if(index>=2)proposed[index]=target;else proposed.Add(target);
                parser.ParseTasks(ToCsv(JsonSerializer.SerializeToElement(new{values=proposed})),source.CacheSource);
            }
            if(delete)requests.Add(new{deleteDimension=new{range=new{sheetId=tabId,dimension="ROWS",startIndex=index,endIndex=index+1}}});
            else if(index<2)requests.Add(new{appendCells=new{sheetId=tabId,rows=new[]{new{values=target.Select(value=>new{userEnteredValue=new{stringValue=value}}).ToArray()}},fields="userEnteredValue"}});
            else
            {
                for(var col=0;col<target.Length;col++)
                {
                    var original=col<fresh[index].Length?fresh[index][col]:"";if(target[col]==original)continue;
                    requests.Add(new{updateCells=new{start=new{sheetId=tabId,rowIndex=index,columnIndex=col},rows=new[]{new{values=new[]{new{userEnteredValue=new{stringValue=target[col]}}}}},fields="userEnteredValue"}});
                }
            }
            if(requests.Count==0)return;
            var token=await TokenAsync(state,source.AccountId,false,ct);
            // Consume before POST. A timeout is an unknown outcome; never blindly resend a mutation.
            drafts.Remove(draftToken);
            try{await api.PostAsync($"https://sheets.googleapis.com/v4/spreadsheets/{GoogleApi.Encode(source.ResourceId)}:batchUpdate",token,new{requests},ct);}
            catch(Exception ex) when(ex is HttpRequestException or OperationCanceledException)
            {throw new InvalidOperationException("寫入結果尚未確認；請重新讀取該任務 ID，確認雲端內容後再操作，勿直接重送。");}
            var after=await ReadRowsAsync(state,source,ct);
            var saved=after.Skip(2).SingleOrDefault(r=>r.Length>idColumn&&r[idColumn]==externalId);
            if((delete&&saved is not null)||(!delete&&(saved is null||fields.Any(f=>Array.IndexOf(headers,f.Key)>=saved.Length||saved[Array.IndexOf(headers,f.Key)]!=f.Value))))
                throw new InvalidOperationException("寫入後內容與預期不同，請至 Google Sheets 確認是否有同時編輯。");
            var updated=source with{LastAttempt=null,Status="寫入已回讀確認，等待同步"};
            state.Sources[state.Sources.IndexOf(source)]=updated;await vault.WriteAsync(state,ct);
            await audit.RecordAsync(new(){UserId=$"Google:{account.Id}",Action=delete?"Google Tasks 刪除":"Google Tasks 寫入",NewValue=$"來源 {source.Id}；已回讀確認",CreatedAt=clock.GetLocalNow().DateTime},ct);
        }
        finally{gate.Release();Notify();}
    }
}

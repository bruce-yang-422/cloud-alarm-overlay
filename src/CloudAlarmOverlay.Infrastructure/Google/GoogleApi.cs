using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CloudAlarmOverlay.Infrastructure.Google;

internal sealed class GoogleApi:IDisposable
{
    internal const string SheetsRead="https://www.googleapis.com/auth/spreadsheets.readonly";
    internal const string SheetsWrite="https://www.googleapis.com/auth/spreadsheets";
    internal const string CalendarList="https://www.googleapis.com/auth/calendar.calendarlist.readonly";
    internal const string CalendarRead="https://www.googleapis.com/auth/calendar.events.owned.readonly";
    private readonly HttpClient http;
    public GoogleApi():this(new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(30),MaxResponseContentBufferSize=16*1024*1024}){}
    internal GoogleApi(HttpClient client)=>http=client;
    internal static string Encode(string text)=>Uri.EscapeDataString(text);
    private static string Base64Url(byte[] data)=>Convert.ToBase64String(data).TrimEnd('=').Replace('+','-').Replace('/','_');
    internal static GoogleClient ParseClient(string json)
    {
        using var doc=JsonDocument.Parse(json);
        if(!doc.RootElement.TryGetProperty("installed",out var app))throw new ArgumentException("請匯入 OAuth「桌面應用程式」JSON（installed），服務帳戶或網頁用戶端不適用。");
        var id=Text(app,"client_id");var secret=Text(app,"client_secret");
        if(!id.EndsWith(".apps.googleusercontent.com",StringComparison.Ordinal)||id.Any(char.IsWhiteSpace)||secret.Length==0)
            throw new ArgumentException("桌面 OAuth 設定缺少有效的 client_id 或 client_secret。");
        return new(id,secret);
    }
    internal static string Text(JsonElement element,string name)=>element.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString()??"":"";
    internal async Task<JsonElement> SendAsync(HttpRequestMessage request,CancellationToken ct)
    {
        using(request)
        using(var response=await http.SendAsync(request,ct).ConfigureAwait(false))
        {
            var body=await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if(!response.IsSuccessStatusCode)
            {
                var error="";var limited=false;
                try
                {
                    using var errorDoc=JsonDocument.Parse(body);error=Text(errorDoc.RootElement,"error");
                    if(errorDoc.RootElement.TryGetProperty("error",out var detail)&&detail.ValueKind==JsonValueKind.Object&&detail.TryGetProperty("errors",out var errors))
                        limited=errors.EnumerateArray().Any(e=>Text(e,"reason") is "rateLimitExceeded" or "userRateLimitExceeded" or "quotaExceeded");
                }catch(JsonException){}
                if(limited)throw new HttpRequestException("Google 流量受限，稍後重試。",null,HttpStatusCode.TooManyRequests);
                if(error=="invalid_grant"||response.StatusCode==HttpStatusCode.Unauthorized)throw new Core.Models.GoogleAuthorizationException("需要重新登入");
                if(error=="admin_policy_enforced")throw new GooglePolicyException("公司政策限制，請聯絡管理員。");
                if(response.StatusCode==HttpStatusCode.Forbidden)throw new GoogleAccessException("Google 拒絕存取：請確認 API 已啟用、帳號授權與檔案／日曆權限，或公司政策限制。");
                if(response.StatusCode==HttpStatusCode.NotFound)throw new GoogleAccessException("來源不存在或此帳號無法存取。");
                // Never propagate server bodies: these may contain tokens, identifiers or private data.
                throw new HttpRequestException($"Google 請求失敗（HTTP {(int)response.StatusCode}），稍後可重試。",null,response.StatusCode);
            }
            if(string.IsNullOrWhiteSpace(body))return JsonSerializer.SerializeToElement(new{});
            using var doc=JsonDocument.Parse(body);return doc.RootElement.Clone();
        }
    }
    internal Task<JsonElement> GetAsync(string url,string token,CancellationToken ct)
    {
        var request=new HttpRequestMessage(HttpMethod.Get,url);
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
        return SendAsync(request,ct);
    }
    internal Task<JsonElement> PostAsync(string url,string token,object data,CancellationToken ct)
    {
        var request=new HttpRequestMessage(HttpMethod.Post,url){Content=new StringContent(JsonSerializer.Serialize(data),Encoding.UTF8,"application/json")};
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
        return SendAsync(request,ct);
    }
    internal Task<JsonElement> TokenAsync(GoogleClient client,Dictionary<string,string> fields,CancellationToken ct)
    {
        fields["client_id"]=client.Id;fields["client_secret"]=client.Secret;
        return SendAsync(new(HttpMethod.Post,"https://oauth2.googleapis.com/token"){Content=new FormUrlEncodedContent(fields)},ct);
    }
    internal Task<JsonElement> RevokeAsync(string token,CancellationToken ct)=>SendAsync(new(HttpMethod.Post,"https://oauth2.googleapis.com/revoke"){Content=new FormUrlEncodedContent(new Dictionary<string,string>{{"token",token}})},ct);
    internal async Task<JsonElement> AuthorizeAsync(GoogleClient client,string scope,Action<string> openBrowser,CancellationToken ct)
    {
        // Bind only loopback; the listener and state are unique to this authorization attempt.
        var reservation=new TcpListener(IPAddress.Loopback,0);reservation.Start();
        var port=((IPEndPoint)reservation.LocalEndpoint).Port;reservation.Stop();
        var redirect=$"http://127.0.0.1:{port}/";
        using var listener=new HttpListener();listener.Prefixes.Add(redirect);listener.Start();
        var state=Base64Url(RandomNumberGenerator.GetBytes(32));var verifier=Base64Url(RandomNumberGenerator.GetBytes(64));
        var challenge=Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var values=new Dictionary<string,string>{{"client_id",client.Id},{"redirect_uri",redirect},{"response_type","code"},{"scope",scope},{"state",state},{"code_challenge",challenge},{"code_challenge_method","S256"},{"access_type","offline"},{"prompt","consent select_account"}};
        openBrowser("https://accounts.google.com/o/oauth2/v2/auth?"+string.Join("&",values.Select(x=>Encode(x.Key)+"="+Encode(x.Value))));
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromMinutes(3));
        while(true)
        {
            var context=await listener.GetContextAsync().WaitAsync(deadline.Token).ConfigureAwait(false);
            var valid=context.Request.HttpMethod=="GET"&&context.Request.Url?.AbsolutePath=="/"&&context.Request.QueryString["state"]==state;
            var code=valid?context.Request.QueryString["code"]:null;
            context.Response.StatusCode=valid?200:400;context.Response.ContentType="text/plain; charset=utf-8";
            context.Response.Headers["Cache-Control"]="no-store";
            var message=Encoding.UTF8.GetBytes(valid?"Google 授權已回傳，請返回 Cloud Alarm Overlay。":"無效的登入回呼。");
            await context.Response.OutputStream.WriteAsync(message,deadline.Token).ConfigureAwait(false);context.Response.Close();
            if(!valid)continue;
            if(string.IsNullOrWhiteSpace(code))throw new OperationCanceledException("已取消 Google 授權。");
            return await TokenAsync(client,new(){{"code",code},{"code_verifier",verifier},{"redirect_uri",redirect},{"grant_type","authorization_code"}},deadline.Token).ConfigureAwait(false);
        }
    }
    public void Dispose()=>http.Dispose();
}
internal sealed class GoogleAccessException(string message):Exception(message);
internal sealed class GooglePolicyException(string message):Exception(message);

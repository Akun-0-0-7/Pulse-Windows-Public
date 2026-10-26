// Copyright (c) 2026 qunqin24. Licensed under the Apache License, Version 2.0.
// Device grants and saved-account renewal translated from Pulse Auth services.
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
namespace Pulse.Core;

public sealed record DeviceLoginPrompt(string ProviderId,string UserCode,string VerificationUrl,string DeviceCode,int IntervalSeconds,DateTimeOffset ExpiresAt);
public sealed record DeviceLoginPoll(string? Credential,bool Pending,int RetryAfterSeconds,string? ErrorMessage=null);
public sealed record BrowserLoginPrompt(string ProviderId,string AuthorizationUrl,string RedirectUri,string State,string CodeVerifier);
public static class HandwrittenLogin
{
    const string CodexClient="app_EMoamEEZ73f0CkXaXp7hrann",GithubClient="Iv1.b507a08c87ecfe98",GrokClient="b1a00492-073a-47ea-816f-4c329264a828",ClaudeClient="9d1c250a-e61b-44d9-88ed-5944d1962f5e";
    // The caller binds a loopback listener before invoking this method and keeps the verifier in memory.
    public static BrowserLoginPrompt BeginBrowser(string providerId,string redirectUri) {
        var id=providerId.ToLowerInvariant();if(id is not("claudecode" or "grok"))throw new NotSupportedException("此服务没有浏览器授权登录。");
        if(!Uri.TryCreate(redirectUri,UriKind.Absolute,out var redirect)||redirect.Scheme!="http"||redirect.Host is not("localhost" or "127.0.0.1")||redirect.IsDefaultPort||redirect.AbsolutePath!="/callback"||redirect.Query!=""||redirect.Fragment!="")throw new ArgumentException("回调地址必须是本机 http://127.0.0.1:<端口>/callback。",nameof(redirectUri));
        var verifier=RandomToken();var state=RandomToken();var challenge=Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+','-').Replace('/','_');
        var claude=id=="claudecode";var endpoint=claude?"https://claude.com/cai/oauth/authorize":"https://auth.x.ai/oauth2/authorize";
        var fields=new Dictionary<string,string>{{"client_id",claude?ClaudeClient:GrokClient},{"response_type","code"},{"redirect_uri",redirectUri},{"scope",claude?"user:profile":"openid email offline_access grok-cli:access"},{"code_challenge",challenge},{"code_challenge_method","S256"},{"state",state}};
        if(claude)fields["code"]="true";
        return new(id,endpoint+"?"+string.Join("&",fields.Select(p=>Uri.EscapeDataString(p.Key)+"="+Uri.EscapeDataString(p.Value))),redirectUri,state,verifier);
    }
    public static async Task<string> CompleteBrowserAsync(BrowserLoginPrompt prompt,string callbackUri,HttpClient http,CancellationToken ct=default) {
        if(!Uri.TryCreate(callbackUri,UriKind.Absolute,out var callback)||callback.GetLeftPart(UriPartial.Path)!=prompt.RedirectUri)throw new InvalidOperationException("授权回调地址不匹配。");
        var fields=callback.Query.TrimStart('?').Split('&',StringSplitOptions.RemoveEmptyEntries).Select(x=>x.Split('=',2)).ToDictionary(x=>Uri.UnescapeDataString(x[0]),x=>x.Length==2?Uri.UnescapeDataString(x[1]):"");
        if(!fields.TryGetValue("state",out var state)||!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state),Encoding.UTF8.GetBytes(prompt.State)))throw new InvalidOperationException("授权状态不匹配。");
        if(fields.TryGetValue("error",out var error))throw new InvalidOperationException("授权服务拒绝登录："+error);
        if(!fields.TryGetValue("code",out var code)||string.IsNullOrWhiteSpace(code))throw new InvalidOperationException("授权回调缺少代码。");
        var claude=prompt.ProviderId=="claudecode";if(!claude&&prompt.ProviderId!="grok")throw new NotSupportedException("此服务没有浏览器授权登录。");
        var body=new Dictionary<string,string>{{"grant_type","authorization_code"},{"code",code},{"redirect_uri",prompt.RedirectUri},{"client_id",claude?ClaudeClient:GrokClient},{"code_verifier",prompt.CodeVerifier}};
        if(claude)body["state"]=prompt.State;
        var (status,tokens)=await Post(http,claude?"https://platform.claude.com/v1/oauth/token":"https://auth.x.ai/oauth2/token",body,claude,ct);
        if(status!=200||JsonTools.String(tokens,"access_token")==null)throw new InvalidOperationException("授权代码交换没有完成。");
        return Saved(tokens);
    }
    static string RandomToken()=>Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+','-').Replace('/','_');
    public static async Task<DeviceLoginPrompt> BeginDeviceAsync(string providerId,HttpClient http,CancellationToken ct=default) {
        var codex=providerId.Equals("codex",StringComparison.OrdinalIgnoreCase);if(!codex&&!providerId.Equals("copilot",StringComparison.OrdinalIgnoreCase))throw new NotSupportedException("此服务没有已接入的设备授权登录。");
        var (status,n)=await Post(http,codex?"https://auth.openai.com/api/accounts/deviceauth/usercode":"https://github.com/login/device/code",new Dictionary<string,string>{{"client_id",codex?CodexClient:GithubClient},{"scope",codex?"":"read:user"}},true,ct);
        var code=JsonTools.String(n,"user_code")??JsonTools.String(n,"usercode");var handle=JsonTools.String(n,codex?"device_auth_id":"device_code");
        if(status!=200||string.IsNullOrWhiteSpace(code)||string.IsNullOrWhiteSpace(handle))throw new InvalidOperationException("服务没有签发设备登录代码。");
        var url=codex?"https://auth.openai.com/codex/device":JsonTools.String(n,"verification_uri_complete")??JsonTools.String(n,"verification_uri")??"https://github.com/login/device";
        if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.Host!=(codex?"auth.openai.com":"github.com"))throw new InvalidOperationException("服务返回了无法使用的登录地址。");
        return new(codex?"codex":"copilot",code,url,handle,(int)Math.Clamp(JsonTools.Number(n,"interval")??5,1,60),DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(JsonTools.Number(n,"expires_in")??900,1,900)));
    }
    // One poll only. The UI schedules the next call using RetryAfterSeconds and provides cancellation.
    public static async Task<DeviceLoginPoll> PollDeviceAsync(DeviceLoginPrompt prompt,HttpClient http,CancellationToken ct=default) {
        if(DateTimeOffset.UtcNow>=prompt.ExpiresAt)return new(null,false,0,"设备登录代码已过期，请重新开始。");
        var codex=prompt.ProviderId=="codex";var fields=codex?new Dictionary<string,string>{{"device_auth_id",prompt.DeviceCode},{"user_code",prompt.UserCode}}:new Dictionary<string,string>{{"client_id",GithubClient},{"device_code",prompt.DeviceCode},{"grant_type","urn:ietf:params:oauth:grant-type:device_code"}};
        var (status,n)=await Post(http,codex?"https://auth.openai.com/api/accounts/deviceauth/token":"https://github.com/login/oauth/access_token",fields,true,ct);
        if(codex&&status is 403 or 404)return new(null,true,prompt.IntervalSeconds);
        if(!codex) {
            if(JsonTools.String(n,"access_token") is {} token)return new(token,false,0);
            return JsonTools.String(n,"error") switch {"authorization_pending" or null=>new(null,true,prompt.IntervalSeconds),"slow_down"=>new(null,true,(int)Math.Clamp(JsonTools.Number(n,"interval")??prompt.IntervalSeconds+5,1,60)),"access_denied"=>new(null,false,0,"登录授权已取消。"),"expired_token"=>new(null,false,0,"设备登录代码已过期。"),_=>new(null,false,0,"GitHub 拒绝了设备登录请求。")};
        }
        var code=JsonTools.String(n,"authorization_code");var verifier=JsonTools.String(n,"code_verifier");if(status!=200||code==null||verifier==null)return new(null,false,0,"Codex 拒绝了设备登录请求。");
        var (exchange,tokens)=await Post(http,"https://auth.openai.com/oauth/token",new(){{"grant_type","authorization_code"},{"code",code},{"redirect_uri","https://auth.openai.com/deviceauth/callback"},{"client_id",CodexClient},{"code_verifier",verifier}},false,ct);
        if(exchange!=200||JsonTools.String(tokens,"access_token")==null)return new(null,false,0,"设备代码已确认，但登录令牌交换没有完成。");
        return new(Saved(tokens),false,0);
    }
    internal static async Task<FetchContext> RenewIfExpiredAsync(FetchContext c) {
        if(c.Credential==null||!c.Credential.TrimStart().StartsWith('{'))return c;
        JsonElement n;try {using var d=JsonDocument.Parse(c.Credential);n=d.RootElement.Clone();}catch(JsonException){return c;}
        var refresh=JsonTools.String(n,"refreshToken")??JsonTools.String(n,"refresh_token");var expiry=JsonTools.Date(n,"expiresAt")??JsonTools.Date(n,"expires_at");
        if(string.IsNullOrEmpty(refresh)||expiry==null||expiry>DateTimeOffset.UtcNow.AddSeconds(60))return c;
        var (url,client,json,scope)=c.Account.ProviderId.ToLowerInvariant() switch {
            "codex"=>("https://auth.openai.com/oauth/token",CodexClient,false,"openid profile email offline_access api.connectors.read api.connectors.invoke"),
            "claudecode"=>("https://platform.claude.com/v1/oauth/token",ClaudeClient,true,"user:profile"),
            "grok"=>("https://auth.x.ai/oauth2/token",GrokClient,false,"openid email offline_access grok-cli:access"),
            _=>("","",false,"")};if(url=="")return c;
        var (status,tokens)=await Post(c.Http,url,new(){{"grant_type","refresh_token"},{"refresh_token",refresh},{"client_id",client},{"scope",scope}},json,c.CancellationToken);
        if(status!=200||JsonTools.String(tokens,"access_token")==null)return c;
        var updated=Saved(tokens,n,refresh);if(c.Secrets?.Get(c.Account.Id)==c.Credential)c.Secrets.Set(c.Account.Id,updated);
        return c with{Credential=updated};
    }
    static string Saved(JsonElement tokens,JsonElement old=default,string? refresh=null) {
        var access=JsonTools.String(tokens,"access_token")!;var account=JsonTools.String(old,"accountId")??JsonTools.String(old,"account_id");
        var claims=Claims(access);var auth=claims.ValueKind==JsonValueKind.Object&&claims.TryGetProperty("https://api.openai.com/auth",out var claim)?claim:default;account=JsonTools.String(auth,"chatgpt_account_id")??account;
        return JsonSerializer.Serialize(new{accessToken=access,refreshToken=JsonTools.String(tokens,"refresh_token")??refresh??"",expiresAt=DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(JsonTools.Number(tokens,"expires_in")??3600,1,365*86400)),accountId=account});
    }
    internal static JsonElement Claims(string token) {try {var p=token.Split('.');if(p.Length<2)return default;var value=p[1].Replace('-','+').Replace('_','/');value=value.PadRight((value.Length+3)/4*4,'=');using var d=JsonDocument.Parse(Convert.FromBase64String(value));return d.RootElement.Clone();}catch(FormatException){return default;}catch(JsonException){return default;}}
    static async Task<(int Status,JsonElement Json)> Post(HttpClient http,string url,Dictionary<string,string> body,bool json,CancellationToken ct) {
        using var request=new HttpRequestMessage(HttpMethod.Post,url);request.Headers.Accept.ParseAdd("application/json");request.Content=json?new StringContent(JsonSerializer.Serialize(body),Encoding.UTF8,"application/json"):new FormUrlEncodedContent(body);
        using var response=await http.SendAsync(request,ct);var bytes=await response.Content.ReadAsByteArrayAsync(ct);if(bytes.Length>262144)throw new IOException("登录服务响应超过大小上限。");
        try {using var doc=JsonDocument.Parse(bytes);return((int)response.StatusCode,doc.RootElement.Clone());}catch(JsonException){return((int)response.StatusCode,default);}
    }
}

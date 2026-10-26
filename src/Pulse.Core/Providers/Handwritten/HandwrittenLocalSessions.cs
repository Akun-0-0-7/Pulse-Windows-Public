// Copyright (c) 2026 qunqin24. Licensed under the Apache License, Version 2.0.
// Windows transports for the upstream local Antigravity and Claude desktop routes.
using System.Text.Json;
namespace Pulse.Core;

public sealed record AntigravityServer(int ProcessId,string Origin,string CsrfToken,IReadOnlyList<int> Ports);
public static class HandwrittenLocalSessions
{
    const string QuotaMethod="exa.language_server_pb.LanguageServerService/RetrieveUserQuotaSummary";
    const string StatusMethod="exa.language_server_pb.LanguageServerService/GetUserStatus";
    public static IReadOnlyList<AntigravityServer> ParseAntigravityProcesses(string json) {
        using var doc=JsonDocument.Parse(json);var root=doc.RootElement;var entries=root.ValueKind==JsonValueKind.Array?root.EnumerateArray().ToArray():root.ValueKind==JsonValueKind.Object?[root]:[];
        var result=new List<AntigravityServer>();
        foreach(var entry in entries) {
            var path=JsonTools.String(entry,"ExecutablePath")??"";var command=JsonTools.String(entry,"CommandLine")??"";
            var normalized=path.Replace('/','\\');var origin=normalized.Contains("\\Antigravity IDE\\",StringComparison.OrdinalIgnoreCase)?"ide":normalized.Contains("\\Antigravity\\",StringComparison.OrdinalIgnoreCase)?"app":null;
            if(origin==null||!Path.GetFileName(path).StartsWith("language_server",StringComparison.OrdinalIgnoreCase))continue;
            var fields=command.Split(' ',StringSplitOptions.RemoveEmptyEntries);var index=Array.FindIndex(fields,x=>x=="--csrf_token");if(index<0||index+1>=fields.Length)continue;
            var token=fields[index+1].Trim('"');if(token.Length is <8 or >256||token.Any(ch=>!char.IsLetterOrDigit(ch)&&ch is not('-' or '_')))continue;
            var ports=JsonTools.Array(entry,"Ports").Select(x=>x.ValueKind==JsonValueKind.Number&&x.TryGetInt32(out var n)?n:0).Where(x=>x is >0 and <=65535).Distinct().ToArray();
            if(ports.Length>0)result.Add(new((int)(JsonTools.Number(entry,"ProcessId")??0),origin,token,ports));
        }
        return result.OrderBy(x=>x.Origin=="app"?0:1).ToArray();
    }
    public static async Task<IReadOnlyList<AntigravityServer>> DiscoverAntigravityAsync(CancellationToken ct=default) {
        if(!OperatingSystem.IsWindows())return [];
        const string script="""
        $rows = @(Get-CimInstance Win32_Process -Filter "Name LIKE 'language_server%'" -ErrorAction SilentlyContinue | ForEach-Object {
          $p = $_; $ports = @(Get-NetTCPConnection -State Listen -OwningProcess $p.ProcessId -ErrorAction SilentlyContinue | Select-Object -ExpandProperty LocalPort -Unique)
          [pscustomobject]@{ ProcessId=$p.ProcessId; ExecutablePath=$p.ExecutablePath; CommandLine=$p.CommandLine; Ports=$ports }
        }); ConvertTo-Json -InputObject $rows -Compress -Depth 4
        """;
        var shell=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe");
        try {var output=await ProcessRunner.RunAsync(shell,["-NoProfile","-NonInteractive","-EncodedCommand",Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script))],TimeSpan.FromSeconds(8),ct,maxOutputBytes:262144);return output.ExitCode==0?ParseAntigravityProcesses(output.Output):[];}catch(Exception ex)when(ex is IOException or System.ComponentModel.Win32Exception or JsonException){return [];}
    }
    public static async Task<UsageReading> QueryAntigravityAsync(FetchContext context,IReadOnlyList<AntigravityServer> servers) {
        var answered=false;var empty=false;
        foreach(var server in servers)foreach(var port in server.Ports) {
            var url=$"https://127.0.0.1:{port}/";var headers=new Dictionary<string,string>{{"x-codeium-csrf-token",server.CsrfToken},{"Accept","application/json"}};
            try {
                var reply=await ProviderRequest.SendAsync(context,url+QuotaMethod,"POST",new {},headers);
                if(reply.Status is 401 or 403){answered=true;continue;}if(reply.Status!=200)continue;answered=true;
                var result=HandwrittenProviders.Parse(context.Account,reply.Body);if(!result.Success){if(reply.Json.ValueKind==JsonValueKind.Object)empty=true;continue;}
                result.Source="language-server";
                try {var status=await ProviderRequest.SendAsync(context,url+StatusMethod,"POST",new {},headers);if(status.Status==200)result.Plan=JsonTools.String(status.Json,"userStatus.planStatus.planInfo.planName");}catch(HttpRequestException){}
                return result;
            }catch(HttpRequestException){}catch(TaskCanceledException)when(!context.CancellationToken.IsCancellationRequested){}
        }
        return UsageReading.Unavailable(context.Account,empty?"noLimits":servers.Count>0||answered?"antigravityNotAnswering":"antigravityNotRunning",empty?"语言服务器没有上报限额。":servers.Count>0||answered?"Antigravity 已打开，但语言服务器没有返回用量。":"没有找到运行中的 Antigravity 语言服务器。");
    }
    public static HttpClient CreateLoopbackClient() {
        var handler=new HttpClientHandler{UseProxy=false,UseCookies=false,ServerCertificateCustomValidationCallback=(request,_,_,_)=>request.RequestUri?.Host=="127.0.0.1"};
        return new HttpClient(handler){Timeout=TimeSpan.FromSeconds(6)};
    }
    public static (string? Session,string? ActiveOrganization) ClaudeCookie(string? input) {
        if(string.IsNullOrWhiteSpace(input)||input.Length>32768||input.Any(ch=>ch<' '||ch>126))return(null,null);
        var found=new Dictionary<string,string>(StringComparer.Ordinal);foreach(var field in input.Split(';')) {
            var parts=field.Trim().Split('=',2);if(parts.Length!=2||parts[1].Length==0)continue;
            if(parts[0] is "sessionKey" or "sessionKeyV3" or "lastActiveOrg" && parts[1].IndexOfAny(['\r','\n',';'])<0)found.TryAdd(parts[0],parts[1]);
        }
        var session=string.Join("; ",new[]{"sessionKey","sessionKeyV3"}.Where(found.ContainsKey).Select(name=>name+"="+found[name]));
        var active=found.GetValueOrDefault("lastActiveOrg");if(active!=null)active=Uri.UnescapeDataString(active.Trim('"'));
        return(session.Length==0?null:session,string.IsNullOrWhiteSpace(active)?null:active);
    }
    public static async Task<UsageReading> QueryClaudeDesktopAsync(FetchContext c,string cookieInput) {
        var (session,active)=ClaudeCookie(cookieInput);if(session==null)return UsageReading.Unavailable(c.Account,"signedOut","请连接 Claude 桌面会话。");
        var headers=new Dictionary<string,string>{{"Cookie",session},{"Accept","application/json"},{"anthropic-client-platform","web_claude_ai"}};
        var rejected=new HashSet<string>(StringComparer.Ordinal);
        for(var attempt=0;attempt<2;attempt++) {
            var bootstrap=await ProviderRequest.SendAsync(c,"https://claude.ai/api/bootstrap",headers:headers);
            if(bootstrap.Status!=200)return ClaudeFailure(c,bootstrap.Status);
            var memberships=JsonTools.Array(bootstrap.Json,"account.memberships").Select(x=>JsonTools.At(x,"organization")).Where(x=>x.ValueKind==JsonValueKind.Object).ToArray();
            var org=memberships.FirstOrDefault(x=>JsonTools.String(x,"uuid")==active&&!rejected.Contains(active!));if(org.ValueKind!=JsonValueKind.Object)org=memberships.FirstOrDefault(x=>JsonTools.String(x,"uuid") is {} id&&!rejected.Contains(id));
            var id=JsonTools.String(org,"uuid");if(id==null||!Guid.TryParse(id,out _))return UsageReading.Unavailable(c.Account,"unreadableReply","Claude 会话没有返回组织标识。");
            var usage=await ProviderRequest.SendAsync(c,"https://claude.ai/api/organizations/"+id+"/usage",headers:headers);
            if(usage.Status==404&&attempt==0){rejected.Add(id);active=null;continue;}
            if(usage.Status!=200)return ClaudeFailure(c,usage.Status);
            var result=HandwrittenProviders.Parse(c.Account,usage.Body);result.Source="desktop-session";
            var plan=JsonTools.String(org,"subscription_type")??JsonTools.String(org,"rate_limit_tier")??JsonTools.String(org,"organization_type");
            result.Plan=plan?.Replace('_',' ');return result;
        }
        return UsageReading.Unavailable(c.Account,"noLimits","Claude 桌面会话没有可用的组织额度。");
    }
    static UsageReading ClaudeFailure(FetchContext c,int status)=>status switch {
        401 or 403=>UsageReading.Unavailable(c.Account,"sessionExpired","Claude 桌面会话已过期，请重新登录。"),
        429=>UsageReading.Unavailable(c.Account,"rateLimited","Claude 暂时限制了额度查询。"),
        _=>UsageReading.Unavailable(c.Account,"serverError","Claude 桌面会话没有返回额度。")};
}

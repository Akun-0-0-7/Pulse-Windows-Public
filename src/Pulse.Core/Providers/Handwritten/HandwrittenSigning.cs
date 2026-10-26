// Copyright (c) 2026 qunqin24. Licensed under the Apache License, Version 2.0.
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
namespace Pulse.Core;
public static class HandwrittenSigning
{
    public static Dictionary<string,string> Headers(string url,string accessKey,string secretKey,DateTimeOffset at) {
        var uri=new Uri(url);const string algorithm="HMAC-SHA256",signed="content-type;host;x-content-sha256;x-date",content="application/x-www-form-urlencoded; charset=utf-8";
        var day=at.UtcDateTime.ToString("yyyyMMdd",CultureInfo.InvariantCulture);var stamp=at.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'",CultureInfo.InvariantCulture);var hash=Hash("");
        var query=string.Join('&',uri.Query.TrimStart('?').Split('&',StringSplitOptions.RemoveEmptyEntries).Select(x=>x.Split('=',2)).Select(x=>(Name:Uri.EscapeDataString(Uri.UnescapeDataString(x[0])),Value:Uri.EscapeDataString(Uri.UnescapeDataString(x.Length>1?x[1]:"")))).OrderBy(x=>x.Name,StringComparer.Ordinal).ThenBy(x=>x.Value,StringComparer.Ordinal).Select(x=>x.Name+"="+x.Value));
        var canonical=string.Join('\n',"GET",uri.AbsolutePath,query,"content-type:"+content,"host:"+uri.Host,"x-content-sha256:"+hash,"x-date:"+stamp,"",signed,hash);
        var scope=day+"/cn-beijing/ark/request";var toSign=string.Join('\n',algorithm,stamp,scope,Hash(canonical));var key=Encoding.UTF8.GetBytes(secretKey);foreach(var step in new[]{day,"cn-beijing","ark","request"})key=HMACSHA256.HashData(key,Encoding.UTF8.GetBytes(step));
        var signature=Convert.ToHexStringLower(HMACSHA256.HashData(key,Encoding.UTF8.GetBytes(toSign)));
        return new(){{"Content-Type",content},{"Host",uri.Host},{"X-Date",stamp},{"X-Content-Sha256",hash},{"Authorization",algorithm+" Credential="+accessKey+"/"+scope+", SignedHeaders="+signed+", Signature="+signature}};
    }
    static string Hash(string text)=>Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}

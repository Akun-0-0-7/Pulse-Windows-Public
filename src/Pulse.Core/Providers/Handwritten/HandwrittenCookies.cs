// Copyright (c) 2026 qunqin24. Licensed under the Apache License, Version 2.0.
using System.Text.RegularExpressions;
namespace Pulse.Core;
public static class HandwrittenCookies
{
    public static string? Normalize(string providerId,string input) {
        if(input.Length>32768||input.Any(c=>c<32||c>126))return null;
        var header=input.Trim();if(header.StartsWith("Cookie:",StringComparison.OrdinalIgnoreCase))header=header[7..].Trim();
        var id=providerId.ToLowerInvariant();var required=id switch {"xiaomimimo"=>new[]{"api-platform_serviceToken","userId"},"stepfun"=>new[]{"Oasis-Token"},"opencodego"=>new[]{"__Host-console_session"},"cursor" or "grokbot"=>new[]{"WorkosCursorSessionToken"},_=>Array.Empty<string>()};
        var wanted=id switch {"xiaomimimo"=>required.Concat(["api-platform_ph","api-platform_slh"]).ToArray(),"stepfun"=>required.Concat(["Oasis-Webid","INGRESSCOOKIE"]).ToArray(),"opencodego"=>required.Concat(["auth"]).ToArray(),_=>required};
        var found=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var part in header.Split(';')) {
            var pair=part.Split('=',2);if(pair.Length!=2)continue;var name=pair[0].Trim();var value=pair[1].Trim();
            var allowed=id switch {
                "ollamacloud"=>Regex.IsMatch(name,@"^(wos-session|__Secure-session|__Secure-next-auth\.session-token|next-auth\.session-token)(\.[0-9]+)?$"),
                "qoder"=>!new[]{"_ga","_gid","_gat","_gcl","_fbp","_fbc","_clck","_clsk","_hj","_uet","_tt_","_ttp","ajs_","amp_","mp_","hm_","hmaccount","intercom-","__stripe","_rdt","_pin"}.Any(p=>name.StartsWith(p,StringComparison.OrdinalIgnoreCase)),
                _=>wanted.Contains(name)};
            if(!allowed)continue;if(value==""||name==""||value.IndexOfAny([' ','"','\\'])>=0||name.Contains(' '))return null;found.TryAdd(name,value);
        }
        if(found.Count==0||required.Any(k=>!found.ContainsKey(k)))return null;
        return string.Join("; ",found.Select(p=>p.Key+"="+p.Value));
    }
    public static string? StepFunWebId(string cookie) {
        var pairs=cookie.Split(';').Select(p=>p.Trim().Split('=',2)).Where(p=>p.Length==2).ToDictionary(p=>p[0],p=>p[1]);if(pairs.TryGetValue("Oasis-Webid",out var id))return id;
        if(pairs.TryGetValue("Oasis-Token",out var token))foreach(var part in token.Split("...").Reverse())if(JsonTools.String(HandwrittenLogin.Claims(part),"device_id") is {} device)return device;return null;
    }
}

namespace Pulse.Core;
public static class ProxyEnvironment
{
    private const string LoopbackNoProxy = "localhost,127.0.0.1,::1";

    public static Dictionary<string,string>? For(AppConfig config)
    {
        if(config.ProxyMode=="system")return null;
        var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);foreach(System.Collections.DictionaryEntry pair in Environment.GetEnvironmentVariables())if(pair.Key is string key&&pair.Value is string value)result[key]=value;
        Apply(result,config);return result;
    }
    public static Dictionary<string,string>? ForTool(AppConfig config,Func<Uri,Uri?>? resolveSystemProxy=null)
    {
        if(config.ProxyMode!="system")return For(config);

        resolveSystemProxy??=ResolveSystemProxy;
        Uri? httpProxy,httpsProxy;
        try
        {
            httpProxy=resolveSystemProxy(new Uri("http://chatgpt.com"));
            httpsProxy=resolveSystemProxy(new Uri("https://chatgpt.com"));
        }
        catch(Exception exception)when(exception is not OutOfMemoryException)
        {
            throw new InvalidOperationException("系统代理解析失败，未启动本机工具。");
        }
        var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(System.Collections.DictionaryEntry pair in Environment.GetEnvironmentVariables())
            if(pair.Key is string key&&pair.Value is string value)result[key]=value;
        var proxyKeys=new HashSet<string>(["HTTP_PROXY","HTTPS_PROXY","ALL_PROXY","NO_PROXY"],StringComparer.OrdinalIgnoreCase);
        foreach(var key in result.Keys.Where(proxyKeys.Contains).ToArray())
            result.Remove(key);

        if(httpProxy is null&&httpsProxy is null)
        {
            result["NO_PROXY"]="*";
            return result;
        }
        if(httpProxy is not null)result["HTTP_PROXY"]=httpProxy.ToString();
        if(httpsProxy is not null)
        {
            result["HTTPS_PROXY"]=httpsProxy.ToString();
        }
        if(httpProxy is not null&&httpsProxy is not null&&httpProxy.Equals(httpsProxy))
            result["ALL_PROXY"]=httpsProxy.ToString();
        result["NO_PROXY"]=LoopbackNoProxy;
        return result;
    }

    private static Uri? ResolveSystemProxy(Uri target)
    {
        var proxy=HttpClient.DefaultProxy;
        if(proxy.IsBypassed(target))return null;
        var selected=proxy.GetProxy(target);
        if(selected is null||!selected.IsAbsoluteUri||selected==target)
            throw new InvalidOperationException("系统代理未返回可用于本机工具的代理地址。");
        return selected;
    }
    public static void Apply(IDictionary<string,string> target,AppConfig config)
    {
        if(config.ProxyMode=="system")return;
        foreach(var key in target.Keys.Where(k=>new[]{"HTTP_PROXY","HTTPS_PROXY","ALL_PROXY","NO_PROXY"}.Contains(k,StringComparer.OrdinalIgnoreCase)).ToList())target.Remove(key);
        if(config.ProxyMode=="none"){target["NO_PROXY"]="*";return;}
        if(Uri.TryCreate(config.ProxyAddress,UriKind.Absolute,out var uri)&&uri.Scheme is "http" or "https" or "socks5"){target["HTTP_PROXY"]=uri.ToString();target["HTTPS_PROXY"]=uri.ToString();target["ALL_PROXY"]=uri.ToString();}
    }
}

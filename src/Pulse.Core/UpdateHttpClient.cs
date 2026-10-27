using System.Net;
namespace Pulse.Core;

/// <summary>Public update transport, separate from authenticated account API clients.</summary>
public static class UpdateHttpClient
{
    public static HttpClient Create(AppConfig config) => new(CreateHandler(config)) { Timeout = Timeout.InfiniteTimeSpan };

    public static HttpClientHandler CreateHandler(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var handler = new HttpClientHandler
        {
            UseProxy = config.ProxyMode != "none",
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            UseDefaultCredentials = false,
            Credentials = null,
            DefaultProxyCredentials = null
        };
        if (config.ProxyMode == "custom" && Uri.TryCreate(config.ProxyAddress, UriKind.Absolute, out var proxy)) handler.Proxy = new WebProxy(proxy);
        return handler;
    }
}

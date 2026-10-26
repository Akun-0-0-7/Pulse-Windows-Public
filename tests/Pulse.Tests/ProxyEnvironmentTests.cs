using Pulse.Core;

namespace Pulse.Tests;

public static class ProxyEnvironmentTests
{
    private const string Loopback = "localhost,127.0.0.1,::1";

    [Check] public static void SystemModeMapsEachTargetAndClearsInheritedProxyVariables()
    {
        var prior = new[] { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "NO_PROXY", "CODEX_HOME" }
            .ToDictionary(key => key, Environment.GetEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable("HTTP_PROXY", "http://stale-http.invalid:1");
            Environment.SetEnvironmentVariable("HTTPS_PROXY", "http://stale-https.invalid:2");
            Environment.SetEnvironmentVariable("ALL_PROXY", "http://stale-all.invalid:3");
            Environment.SetEnvironmentVariable("NO_PROXY", "stale.invalid");
            Environment.SetEnvironmentVariable("CODEX_HOME", @"C:\fixture-codex-home");
            var requested = new List<string>();
            Uri? Resolve(Uri target)
            {
                requested.Add(target.ToString());
                return target.Scheme == "http" ? new Uri("http://127.0.0.1:7891") : new Uri("http://127.0.0.1:7897");
            }

            var result = ProxyEnvironment.ForTool(new() { ProxyMode = "system" }, Resolve)!;

            Assert.Equal("http://127.0.0.1:7891/", result["HTTP_PROXY"]);
            Assert.Equal("http://127.0.0.1:7897/", result["HTTPS_PROXY"]);
            Assert.True(!result.ContainsKey("ALL_PROXY"));
            Assert.Equal(Loopback, result["NO_PROXY"]);
            Assert.Equal(@"C:\fixture-codex-home", result["CODEX_HOME"]);
            Assert.Equal(Environment.GetEnvironmentVariable("PATH"), result.GetValueOrDefault("PATH"));
            Assert.Equal("http://chatgpt.com/,https://chatgpt.com/", string.Join(',', requested));
            Assert.True(!result.Values.Any(value => value.Contains("stale.invalid", StringComparison.Ordinal)));
        }
        finally
        {
            foreach (var (key, value) in prior) Environment.SetEnvironmentVariable(key, value);
        }
    }

    [Check] public static void SystemModeWithBothTargetsDirectForcesNoProxy()
    {
        var prior = new[] { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "NO_PROXY" }
            .ToDictionary(key => key, Environment.GetEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable("http_proxy", "http://stale-http.invalid:1");
            Environment.SetEnvironmentVariable("Https_Proxy", "http://stale-https.invalid:2");
            Environment.SetEnvironmentVariable("all_proxy", "http://stale-all.invalid:3");
            Environment.SetEnvironmentVariable("no_proxy", "stale.invalid");

            var result = ProxyEnvironment.ForTool(new() { ProxyMode = "system" }, _ => null)!;
            Assert.Equal("*", result["NO_PROXY"]);
            Assert.True(!result.ContainsKey("HTTP_PROXY"));
            Assert.True(!result.ContainsKey("HTTPS_PROXY"));
            Assert.True(!result.ContainsKey("ALL_PROXY"));
        }
        finally
        {
            foreach (var (key, value) in prior) Environment.SetEnvironmentVariable(key, value);
        }
    }

    [Check] public static void SystemModeWithOneDirectTargetUsesAvailableProxy()
    {
        var result = ProxyEnvironment.ForTool(new() { ProxyMode = "system" }, target =>
            target.Scheme == "https" ? new Uri("http://127.0.0.1:7897") : null)!;
        Assert.True(!result.ContainsKey("HTTP_PROXY"));
        Assert.Equal("http://127.0.0.1:7897/", result["HTTPS_PROXY"]);
        Assert.True(!result.ContainsKey("ALL_PROXY"));
        Assert.Equal(Loopback, result["NO_PROXY"]);
    }

    [Check] public static void SharedSystemProxyAlsoCoversOtherToolTraffic()
    {
        var result = ProxyEnvironment.ForTool(new() { ProxyMode = "system" }, _ => new Uri("http://127.0.0.1:7897"))!;
        Assert.Equal(result["HTTP_PROXY"], result["HTTPS_PROXY"]);
        Assert.Equal(result["HTTPS_PROXY"], result["ALL_PROXY"]);
        Assert.Equal(Loopback, result["NO_PROXY"]);
    }

    [Check] public static void HttpOnlySystemProxyDoesNotRouteHttpsThroughAllProxy()
    {
        var result = ProxyEnvironment.ForTool(new() { ProxyMode = "system" }, target =>
            target.Scheme == "http" ? new Uri("http://127.0.0.1:7891") : null)!;
        Assert.Equal("http://127.0.0.1:7891/", result["HTTP_PROXY"]);
        Assert.True(!result.ContainsKey("HTTPS_PROXY"));
        Assert.True(!result.ContainsKey("ALL_PROXY"));
        Assert.Equal(Loopback, result["NO_PROXY"]);
    }

    [Check] public static void ResolverFailurePropagatesInsteadOfFallingBackToDirect()
    {
        var error = Assert.Throws<InvalidOperationException>(() => ProxyEnvironment.ForTool(
            new() { ProxyMode = "system" }, _ => throw new InvalidOperationException("fixture resolver failed")));
        Assert.True(!error.Message.Contains("fixture resolver failed", StringComparison.Ordinal));
    }

    [Check] public static void ExplicitModesPreserveExistingForBehaviorAndSkipResolver()
    {
        Uri? Unused(Uri _) => throw new Exception("System resolver must not run for explicit modes");
        var custom = new AppConfig { ProxyMode = "custom", ProxyAddress = "http://127.0.0.1:7897" };
        var customResult = ProxyEnvironment.ForTool(custom, Unused)!;
        Assert.Equal(ProxyEnvironment.For(custom)!["HTTP_PROXY"], customResult["HTTP_PROXY"]);
        Assert.Equal(ProxyEnvironment.For(custom)!["HTTPS_PROXY"], customResult["HTTPS_PROXY"]);
        Assert.Equal(ProxyEnvironment.For(custom)!["ALL_PROXY"], customResult["ALL_PROXY"]);
        var none = new AppConfig { ProxyMode = "none" };
        var noneResult = ProxyEnvironment.ForTool(none, Unused)!;
        Assert.Equal(ProxyEnvironment.For(none)!["NO_PROXY"], noneResult["NO_PROXY"]);
        Assert.True(!noneResult.ContainsKey("HTTP_PROXY"));
        Assert.True(!noneResult.ContainsKey("HTTPS_PROXY"));
        Assert.True(!noneResult.ContainsKey("ALL_PROXY"));
    }
}

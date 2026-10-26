using System.Text.Json.Serialization;
namespace Pulse.Core;

public sealed record ProviderDefinition(string Id, string Name, string Billing, string AuthKind, string HelpUrl, string UsageUrl, string Color = "#8A8FFA", bool MultipleAccounts = false);
public sealed class AccountConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ProviderId { get; set; } = "codex";
    public string Label { get; set; } = "";
    public bool Enabled { get; set; }
    public string? ServerAddress { get; set; }
    public string Route { get; set; } = "automatic";
    public string? PrimaryWindowId { get; set; }
    public string? AccentColor { get; set; }
    public bool Detailed { get; set; }
    public string BalanceMode { get; set; } = "sinceTopUp";
    public decimal? Budget { get; set; }
    public List<string> SourcePaths { get; set; } = [];
    public Dictionary<string,string> Extra { get; set; } = [];
}
public sealed record PanelPlacement(string Edge,double X,double Y);
public sealed class AppConfig
{
    public int SchemaVersion { get; set; } = 1;
    public bool OnboardingCompleted { get; set; }
    public List<AccountConfig> Accounts { get; set; } = [];
    public string Language { get; set; } = "zh-CN";
    public string Theme { get; set; } = "dark";
    public string Currency { get; set; } = "CNY";
    public decimal? ManualExchangeRate { get; set; }
    public bool ShowRemaining { get; set; } = true;
    public bool ShowPanel { get; set; } = true;
    public string TrayUsageMode { get; set; } = "number";
    public string? TrayAccountId { get; set; }
    public bool TrayOpensDashboard { get; set; } = true;
    public bool AutoCollapse { get; set; } = true;
    public bool SnapToEdges { get; set; } = true;
    public bool Horizontal { get; set; }
    public string DockEdge { get; set; } = "right";
    public double? PanelX { get; set; }
    public double? PanelY { get; set; }
    public string? PanelDisplay { get; set; }
    public Dictionary<string,PanelPlacement> DisplayPlacements { get; set; } = [];
    public double RingSize { get; set; } = 48;
    public double RingGap { get; set; } = 12;
    public double PanelOpacity { get; set; } = .96;
    public bool ShowClock { get; set; } = true;
    public bool ShowSecondaryRing { get; set; } = true;
    public bool SegmentedBars { get; set; }
    public bool FollowDisplay { get; set; }
    public bool HideFullscreen { get; set; } = true;
    public bool HideCapture { get; set; }
    public bool StartWithWindows { get; set; }
    public bool SpendEnabled { get; set; }
    public List<string>? EnabledSpendSources { get; set; }
    public decimal? MonthlySubscriptionUsd { get; set; }
    public bool RecapAlerts { get; set; }
    public bool ForecastEnabled { get; set; }
    public bool AnimatedMarks { get; set; }
    public bool HideProjectNames { get; set; } = true;
    public bool AlertsEnabled { get; set; }
    public List<int> AlertThresholds { get; set; } = [75,80,90,95];
    public bool ResetAlerts { get; set; }
    public bool FailureAlerts { get; set; }
    public bool StatusAlerts { get; set; }
    public decimal? LowBalanceThreshold { get; set; }
    public string ProxyMode { get; set; } = "system";
    public string? ProxyAddress { get; set; }
    public double RefreshMinutes { get; set; } = 2;
    public string? SettingsShortcut { get; set; }
    public string? PanelShortcut { get; set; }
    public string? UpdateManifestUrl { get; set; }
    public List<string> SpendPaths { get; set; } = [];
    public List<string> EnabledExtensions { get; set; } = [];
    public bool WindowStarterEnabled { get; set; }
    public int WindowStarterStartHour { get; set; } = 9;
    public int WindowStarterEndHour { get; set; } = 18;
}
public sealed class UsageWindow
{
    public string Id { get; set; } = "main";
    public string Kind { get; set; } = "other";
    public string? Scope { get; set; }
    public string? Label { get; set; }
    public double UsedPercent { get; set; }
    public bool Exhausted { get; set; }
    public DateTimeOffset? ResetsAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public double? ExpiringAmount { get; set; }
    public double? WindowSeconds { get; set; }
    public bool ReportsLength { get; set; }
    public bool Estimated { get; set; }
    public string? EstimatedFrom { get; set; }
}
public sealed record MoneyBalance(decimal Amount, string Currency);
public sealed record MetricPoint(DateTimeOffset T, double V);
public sealed record UsageMetric(string Id, string Label, double Value, string? Unit = null, List<MetricPoint>? History = null);
public sealed record DiagnosticEntry(DateTimeOffset At, string Url, int Status, string Body);
public sealed class UsageReading
{
    public string AccountId { get; set; } = "";
    public string ProviderId { get; set; } = "";
    public string? Plan { get; set; }
    public List<UsageWindow> Windows { get; set; } = [];
    public MoneyBalance? Balance { get; set; }
    public string? CreditBalance { get; set; }
    public DateTimeOffset? ObservedAt { get; set; }
    public string? Source { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsStale { get; set; }
    public List<UsageMetric> Metrics { get; set; } = [];
    [JsonIgnore] public List<DiagnosticEntry> Diagnostics { get; set; } = [];
    [JsonIgnore] public bool Success => ErrorCode is null && (Windows.Count > 0 || Balance is not null || CreditBalance is not null || Metrics.Count > 0);
    public static UsageReading Unavailable(AccountConfig account, string code, string message) => new() { AccountId = account.Id, ProviderId = account.ProviderId, ErrorCode = code, ErrorMessage = message };
}
public interface ISecretStore { string? Get(string key); void Set(string key, string? value); }
public sealed record FetchContext(AccountConfig Account, string? Credential, HttpClient Http, string HomeDirectory, string DataDirectory, CancellationToken CancellationToken, ISecretStore? Secrets = null,TimeSpan? RequestTimeout=null,Dictionary<string,string>? ProcessEnvironment=null);
public interface IUsageProvider { Task<UsageReading> FetchAsync(FetchContext context); }

using System.Text.Json;

namespace Pulse.Desktop;

public sealed partial class PulseApp
{
    private readonly DispatcherTimer windowStarterTimer=new(){Interval=TimeSpan.FromSeconds(1)};
    private WindowResetCoordinator windowResets=null!;
    private string savedWindowResetState="";
    private bool windowResetStateUnavailable;
    private Dictionary<string,WindowStarterReport> windowStarterReports=[];
    private Func<string,string?> windowStarterAuthReader=LocalPaths.ReadText;

    private void InitializeWindowStarter()
    {
        LoadWindowResetState();
        windowStarterReports=Storage.Load<Dictionary<string,WindowStarterReport>>("window-starter-last.json")??[];
        foreach(var report in windowStarterReports.Values.Where(r=>r is not null))if(report.Status=="sending")report.Status="interrupted";
        windowStarterTimer.Tick+=async(_,_)=>await StartDueWindows();
    }

    private void LoadWindowResetState()
    {
        windowResetStateUnavailable=false;
        var path=Path.Combine(Storage.Root,"window-starter-state.json");
        WindowResetState? state=null;
        try{state=Storage.Load<WindowResetState>("window-starter-state.json");}
        catch(UnauthorizedAccessException){windowResetStateUnavailable=true;}
        windowResetStateUnavailable|=(File.Exists(path)||Directory.Exists(path))&&
            (state?.Accounts is null||state.Accounts.Values.Any(a=>a is null||a.PlannedResets is null));
        windowResets=new(windowResetStateUnavailable?null:state);
        savedWindowResetState=JsonSerializer.Serialize(windowResets.State,StorageJson.Options);
    }

    public bool UsesWindowStarterLogin(AccountConfig account)
    {
        if(!account.Enabled||account.Extra.GetValueOrDefault("isAdditionalAccount")=="true"||
            account.ProviderId is not("codex" or "claudeCode")||!string.IsNullOrWhiteSpace(Secrets.Get("desktop:"+account.Id)))return false;
        var saved=Secrets.Get(account.Id);
        if(account.ProviderId!="codex")return string.IsNullOrWhiteSpace(saved);
        if(!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY"))||
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CODEX_API_KEY")))return false;
        var defaultHome=Path.Combine(LocalPaths.Home,".codex");
        var configuredHome=Environment.GetEnvironmentVariable("CODEX_HOME");
        if(!string.IsNullOrWhiteSpace(configuredHome)&&!Path.IsPathFullyQualified(configuredHome))return false;
        var toolHome=string.IsNullOrWhiteSpace(configuredHome)?defaultHome:configuredHome;
        var monitoredAuth=string.IsNullOrWhiteSpace(saved)?windowStarterAuthReader(Path.Combine(defaultHome,"auth.json")):saved;
        return WindowStarterIdentity.SameCodexAccount(monitoredAuth,windowStarterAuthReader(Path.Combine(toolHome,"auth.json")));
    }

    private bool CanStartWindow(AccountConfig account)=>!exiting&&Engine.Config.WindowStarterEnabled&&
        account.Extra.GetValueOrDefault("windowStarter")=="true"&&UsesWindowStarterLogin(account)&&
        Engine.Config.Accounts.FirstOrDefault(a=>a.ProviderId==account.ProviderId&&UsesWindowStarterLogin(a))?.Id==account.Id;

    private void SaveWindowResetState()
    {
        var json=JsonSerializer.Serialize(windowResets.State,StorageJson.Options);
        if(json==savedWindowResetState)return;
        Storage.Save("window-starter-state.json",windowResets.State);
        savedWindowResetState=json;
    }

    private WindowResetJob? ClaimWindowReset(AccountConfig account,UsageReading? reading,DateTimeOffset now)
    {
        var before=JsonSerializer.Serialize(windowResets.State,StorageJson.Options);
        try
        {
            var job=windowResets.Claim(account,reading,now,Engine.Config.WindowStarterStartHour,Engine.Config.WindowStarterEndHour);
            SaveWindowResetState();
            return job;
        }
        catch(Exception exception)when(exception is IOException or UnauthorizedAccessException)
        {
            windowResets=new(JsonSerializer.Deserialize<WindowResetState>(before,StorageJson.Options));
            throw;
        }
    }

    private void SetWindowStarterReport(WindowResetJob job,string status,WindowStarterReceipt? receipt=null,ProcessResult? process=null)
    {
        windowStarterReports[job.AccountId]=new(){ProviderId=job.ProviderId,ResetAt=job.ResetAt,At=DateTimeOffset.Now,Status=status,
            Completed=receipt?.Completed,InputTokens=receipt?.InputTokens,OutputTokens=receipt?.OutputTokens,FailureKind=receipt?.FailureKind,
            ExitCode=process?.ExitCode,TimedOut=process?.TimedOut,QuotaObservedAt=Engine.Reading(job.AccountId)?.ObservedAt};
        try{Storage.Save("window-starter-last.json",windowStarterReports);}
        catch(Exception exception)when(exception is IOException or UnauthorizedAccessException){}
        if(!exiting)settings?.UpdateReadings();
    }

    public string WindowStarterStatus(string accountId)
    {
        var account=Engine.Config.Accounts.FirstOrDefault(a=>a.Id==accountId);
        if(account is null||!CanStartWindow(account))return Localization.Translate("自动发送已关闭。");
        if(windowResetStateUnavailable)return Localization.Translate("无法读取执行记录，自动发送已暂停。");
        if(!windowStarterReports.TryGetValue(accountId,out var report)||report is null)return Localization.Translate("已开启：等待 5 小时额度重置。");
        var text=report.Status switch
        {
            "sending"=>"5 小时重置处理中：正在发送 hi…",
            "success"=>"上次处理：已发送 hi 并请求刷新额度 · {0}",
            "toolMissing"=>"上次处理：未找到本机工具，请检查工具路径 · {0}",
            "timedOut"=>"上次处理：发送超时，已请求刷新额度 · {0}",
            "interrupted"=>"上次处理：发送已中断，本轮不重复发送 · {0}",
            "storageError"=>"上次处理：无法保存执行记录，未发送消息 · {0}",
            "cancelled"=>"上次处理：账号设置已更改，已取消发送 · {0}",
            "network"=>"上次处理：网络连接失败，请检查代理 · {0}",
            "auth"=>"上次处理：登录已失效，请重新登录 · {0}",
            "turnIncomplete"=>"上次处理：Codex 未确认请求完成，本轮不重复发送 · {0}",
            _=>"上次处理：发送失败，已请求刷新额度 · {0}"
        };
        return Localization.Format(text,report.At.ToString("MM-dd HH:mm:ss"));
    }

    private async Task StartDueWindows()
    {
        if(exiting||startingWindow||windowResetStateUnavailable||!Engine.Config.WindowStarterEnabled)return;
        startingWindow=true;
        try
        {
            var accounts=Engine.Config.Accounts.Where(CanStartWindow).ToArray();
            foreach(var account in accounts)
            {
                if(!CanStartWindow(account))continue;
                WindowResetJob? job;
                try{job=ClaimWindowReset(account,Engine.Reading(account.Id),DateTimeOffset.Now);}
                catch(Exception exception)when(exception is IOException or UnauthorizedAccessException)
                {
                    SetWindowStarterReport(new(account.Id,account.ProviderId,DateTimeOffset.Now),"storageError");
                    continue;
                }
                if(job is null)continue;
                var version=CredentialVersion(account.Id);
                SetWindowStarterReport(job,"sending");
                try
                {
                    var result=await WindowResetCoordinator.ExecuteAsync(ct=>
                    {
                        var current=Engine.Config.Accounts.FirstOrDefault(a=>a.Id==job.AccountId);
                        if(current is null||current.ProviderId!=job.ProviderId||!CanStartWindow(current)||CredentialVersion(current.Id)!=version)
                            throw new WindowStarterAccountChangedException();
                        return WindowStarter.StartAsync(current.ProviderId,Storage.Root,ct,current.Extra.GetValueOrDefault("executable"),ProxyEnvironment.ForTool(Engine.Config));
                    },ct=>Engine.RefreshAsync(ct,waitForCurrent:true),Engine.LifetimeToken);
                    var receipt=WindowStarter.Evaluate(job.ProviderId,result);
                    var status=receipt.Completed&&receipt.Status=="success"?"success":receipt.FailureKind switch
                    {"timeout"=>"timedOut","toolMissing"=>"toolMissing","auth"=>"auth","network"=>"network","turnIncomplete"=>"turnIncomplete",_=>"failed"};
                    SetWindowStarterReport(job,status,receipt,result);
                }
                catch(WindowStarterAccountChangedException){SetWindowStarterReport(job,"cancelled");}
                catch(OperationCanceledException)when(exiting||Engine.LifetimeToken.IsCancellationRequested){SetWindowStarterReport(job,"interrupted");}
                catch(Exception exception)when(exception is not OutOfMemoryException){SetWindowStarterReport(job,"failed");}
            }
        }
        finally{startingWindow=false;}
    }

    private sealed class WindowStarterAccountChangedException:Exception;
    public sealed class WindowStarterReport
    {
        public string ProviderId {get;set;}="";
        public DateTimeOffset ResetAt {get;set;}
        public DateTimeOffset At {get;set;}
        public string Status {get;set;}="";
        public bool? Completed {get;set;}
        public int? InputTokens {get;set;}
        public int? OutputTokens {get;set;}
        public string? FailureKind {get;set;}
        public int? ExitCode {get;set;}
        public bool? TimedOut {get;set;}
        public DateTimeOffset? QuotaObservedAt {get;set;}
    }
}

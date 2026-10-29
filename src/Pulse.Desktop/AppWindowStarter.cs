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
    private Func<DateTimeOffset> windowStarterNow=()=>DateTimeOffset.Now;
    private Func<AccountConfig,CancellationToken,Task<ProcessResult>>? windowStarterSend=null;
    private Func<CancellationToken,Task>? windowStarterRefresh=null;
    private DateTimeOffset nextWindowStarterRefreshAt=DateTimeOffset.MinValue;

    private void InitializeWindowStarter()
    {
        LoadWindowResetState();
        LoadWindowStarterLedger();
        windowStarterReports=Storage.Load<Dictionary<string,WindowStarterReport>>("window-starter-last.json")??[];
        foreach(var report in windowStarterReports.Values.Where(r=>r is not null))if(report.Status=="sending")report.Status="interrupted";
        foreach(var (id,report) in windowStarterReports)
            if(report?.Status=="success"&&report.At!=default&&windowResets.State.Accounts.GetValueOrDefault(id) is {Pending:null} state&&state.LastClaimedResetAt==report.ResetAt)
            {state.LastCompletedResetAt=report.ResetAt;state.LastCompletedAt=report.At;}
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
            !WindowResetCoordinator.IsValidState(state);
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
        windowStarterReports[job.AccountId]=new(){ProviderId=job.ProviderId,ResetAt=job.ResetAt,At=windowStarterNow(),Status=status,
            Completed=receipt?.Completed,InputTokens=receipt?.InputTokens,OutputTokens=receipt?.OutputTokens,FailureKind=receipt?.FailureKind,
            ExitCode=process?.ExitCode,TimedOut=process?.TimedOut,QuotaObservedAt=Engine.Reading(job.AccountId)?.ObservedAt,
            Model=process?.Model,ModelSource=process?.ModelSource};
        try{Storage.Save("window-starter-last.json",windowStarterReports);}
        catch(Exception exception)when(exception is IOException or UnauthorizedAccessException){}
        if(!exiting)settings?.UpdateReadings();
    }

    public string WindowStarterStatus(string accountId)
    {
        var account=Engine.Config.Accounts.FirstOrDefault(a=>a.Id==accountId);
        if(account is null||!CanStartWindow(account))return Localization.Translate("自动发送已关闭。");
        if(windowResetStateUnavailable)return Localization.Translate("无法读取执行记录，自动发送已暂停。");
        if(windowStarterLedgerUnavailable)return Localization.Translate("无法读取或保存发送计数，自动和手动发送均已暂停。");
        if(!WindowStarterBudgetAvailable(account))return Localization.Translate("今日 hi 尝试次数已达上限，次日恢复；也可调整每日上限。");
        var pending=windowResets.State.Accounts.GetValueOrDefault(accountId)?.Pending;
        if(pending?.Status is "retry" or "quota")
        {
            var reading=Engine.Reading(accountId);
            if(reading is null||reading.ErrorCode is not null||reading.IsStale)
                return Localization.Translate("等待网络恢复和最新额度，恢复后自动补发。");
            if(reading.Windows.Any(w=>w.Kind is "fiveHour" or "weekly"&&(w.Exhausted||w.UsedPercent>=100)))
                return Localization.Translate("额度不足，等待 5 小时及每周额度恢复后补发。");
            return Localization.Format("等待自动补发，最早 {0}；每分钟检查额度。",pending.NextAttemptAt?.ToLocalTime().ToString("HH:mm:ss")??"—");
        }
        if(pending?.Status=="sending"&&!startingWindow)return Localization.Translate("上次发送结果未确认，请先查看额度，可手动重试。");
        if(!windowStarterReports.TryGetValue(accountId,out var report)||report is null)return Localization.Translate("已开启：等待 5 小时额度重置。");
        var text=report.Status switch
        {
            "sending"=>"5 小时重置处理中：正在发送 hi…",
            "success"=>"上次处理：已发送 hi 并请求刷新额度 · {0}",
            "toolMissing"=>"上次处理：未找到本机工具，请检查工具路径 · {0}",
            "timedOut"=>"上次处理：发送超时，请先查看额度，可手动重试 · {0}",
            "interrupted"=>"上次处理：发送已中断，可手动重试 · {0}",
            "storageError"=>"上次处理：无法保存执行记录，未发送消息 · {0}",
            "cancelled"=>"上次处理：账号设置已更改，已取消发送 · {0}",
            "network"=>"上次处理：网络连接失败，请检查代理 · {0}",
            "auth"=>"上次处理：登录已失效，请重新登录 · {0}",
            "turnIncomplete"=>"上次处理：未确认请求完成，请先查看额度，可手动重试 · {0}",
            "notReady"=>"未发送：请等待最新可用额度，或确认本轮是否已成功。 · {0}",
            "model"=>"上次处理：hi 所选模型不可用，请检查自动 hi 模型设置后重试 · {0}",
            "modelSelection"=>"未发送：无法读取 Codex 模型设置，请先确认 Codex 能正常启动 · {0}",
            _=>"上次处理：发送失败，可手动重试 · {0}"
        };
        var status=Localization.Format(text,report.At.ToString("MM-dd HH:mm:ss"));
        return string.IsNullOrWhiteSpace(report.Model)?status:status+"\n"+Localization.Format("模型：{0}",report.Model);
    }

    private async Task StartDueWindows()
    {
        if(exiting||startingWindow||windowResetStateUnavailable||!Engine.Config.WindowStarterEnabled)return;
        startingWindow=true;
        var changed=false;
        try
        {
            var accounts=Engine.Config.Accounts.Where(CanStartWindow).ToArray();
            if(accounts.Length==0)return;
            var versions=accounts.ToDictionary(account=>account.Id,account=>CredentialVersion(account.Id));
            var now=windowStarterNow();
            if(now>=nextWindowStarterRefreshAt)
            {
                nextWindowStarterRefreshAt=now.AddMinutes(1);
                changed=true;
                await RefreshWindowStarterQuotaAsync(Engine.LifetimeToken);
            }
            foreach(var account in accounts)
            {
                if(!CanStartWindow(account)||!WindowStarterBudgetAvailable(account))continue;
                if(CredentialVersion(account.Id)!=versions[account.Id]){nextWindowStarterRefreshAt=DateTimeOffset.MinValue;continue;}
                WindowResetJob? job;
                try{job=ClaimWindowReset(account,Engine.Reading(account.Id),windowStarterNow());}
                catch(Exception exception)when(exception is IOException or UnauthorizedAccessException)
                {
                    SetWindowStarterReport(new(account.Id,account.ProviderId,windowStarterNow()),"storageError");
                    continue;
                }
                if(job is null)continue;
                changed=true;
                await ExecuteWindowStarterJobAsync(job,versions[account.Id]);
                if(windowResetStateUnavailable)break;
            }
        }
        catch(OperationCanceledException)when(exiting||Engine.LifetimeToken.IsCancellationRequested){}
        finally{startingWindow=false;if(changed&&!exiting)settings?.UpdateReadings();}
    }

    public bool CanRetryWindowStarter(string accountId)=>!startingWindow&&!windowResetStateUnavailable&&
        Engine.Config.Accounts.FirstOrDefault(a=>a.Id==accountId) is {} account&&CanStartWindow(account)&&WindowStarterBudgetAvailable(account)&&
        (windowResets.State.Accounts.GetValueOrDefault(accountId)?.Pending?.Status!="success"||
            windowResets.CanClaimManual(account,Engine.Reading(accountId),windowStarterNow()))&&
        !(windowResets.State.Accounts.GetValueOrDefault(accountId)?.Pending is null&&
            windowStarterReports.GetValueOrDefault(accountId) is {Status:"success"} report&&windowStarterNow()-report.At<TimeSpan.FromHours(5));

    public async Task RetryWindowStarterAsync(string accountId)
    {
        if(!CanRetryWindowStarter(accountId))return;
        startingWindow=true;
        var version=CredentialVersion(accountId);
        try
        {
            settings?.UpdateReadings();
            await RefreshWindowStarterQuotaAsync(Engine.LifetimeToken);
            var account=Engine.Config.Accounts.FirstOrDefault(a=>a.Id==accountId);
            if(account is null||!CanStartWindow(account)||windowResetStateUnavailable)return;
            if(CredentialVersion(accountId)!=version){nextWindowStarterRefreshAt=DateTimeOffset.MinValue;return;}
            var before=JsonSerializer.Serialize(windowResets.State,StorageJson.Options);
            WindowResetJob? job;
            try
            {
                job=windowResets.ClaimManual(account,Engine.Reading(accountId),windowStarterNow());
                SaveWindowResetState();
            }
            catch(Exception exception)when(exception is IOException or UnauthorizedAccessException)
            {
                windowResets=new(JsonSerializer.Deserialize<WindowResetState>(before,StorageJson.Options));
                SetWindowStarterReport(new(account.Id,account.ProviderId,windowStarterNow()),"storageError");return;
            }
            if(job is null)
            {
                SetWindowStarterReport(new(account.Id,account.ProviderId,windowStarterNow()),"notReady");return;
            }
            await ExecuteWindowStarterJobAsync(job,version);
        }
        catch(OperationCanceledException)when(exiting||Engine.LifetimeToken.IsCancellationRequested){}
        finally{startingWindow=false;if(!exiting)settings?.UpdateReadings();}
    }

    private async Task RefreshWindowStarterQuotaAsync(CancellationToken ct)
    {
        try
        {
            if(windowStarterRefresh is {} refreshQuota)await refreshQuota(ct);
            else await Engine.RefreshAsync(ct,waitForCurrent:true);
        }
        catch(OperationCanceledException)when(ct.IsCancellationRequested){throw;}
        catch(Exception exception)when(exception is not OutOfMemoryException){}
    }

    private async Task ExecuteWindowStarterJobAsync(WindowResetJob job,long version)
    {
        ProcessResult? result=null;
        WindowStarterAttempt? attempt=null;
        string? blockedReason=null;
        WindowStarterReceipt receipt;
        SetWindowStarterReport(job,"sending");
        try
        {
            Engine.LifetimeToken.ThrowIfCancellationRequested();
            var current=Engine.Config.Accounts.FirstOrDefault(a=>a.Id==job.AccountId);
            if(current is null||current.ProviderId!=job.ProviderId||!CanStartWindow(current)||CredentialVersion(current.Id)!=version)
                throw new WindowStarterAccountChangedException();
            var selectedModel=current.Extra.GetValueOrDefault("windowStarterModel");
            var selectedExecutable=current.Extra.GetValueOrDefault("executable");var selectedProxy=(Engine.Config.ProxyMode,Engine.Config.ProxyAddress);
            bool Reserve()
            {
                if(!ReferenceEquals(Engine.Config.Accounts.FirstOrDefault(a=>a.Id==job.AccountId),current)||!CanStartWindow(current)||CredentialVersion(current.Id)!=version||selectedModel!=current.Extra.GetValueOrDefault("windowStarterModel")||selectedExecutable!=current.Extra.GetValueOrDefault("executable")||selectedProxy!=(Engine.Config.ProxyMode,Engine.Config.ProxyAddress)){blockedReason="cancelled";return false;}
                try{attempt=ReserveWindowStarterAttempt(current);if(attempt is null)blockedReason="dailyLimit";return attempt is not null;}
                catch(Exception e)when(e is IOException or UnauthorizedAccessException){windowStarterLedgerUnavailable=true;blockedReason="storageError";return false;}
            }
            result=windowStarterSend is {} send?(Reserve()?await send(current,Engine.LifetimeToken):new(-1,"","dispatchBlocked",false)):
                await WindowStarter.StartAsync(current.ProviderId,Storage.Root,Engine.LifetimeToken,current.Extra.GetValueOrDefault("executable"),ProxyEnvironment.ForTool(Engine.Config),configuredModel:selectedModel,beforeDispatch:Reserve);
            receipt=blockedReason is not null?new("failed",false,null,null,blockedReason):WindowStarter.Evaluate(job.ProviderId,result);
        }
        catch(WindowStarterAccountChangedException){receipt=new("failed",false,null,null,"cancelled");}
        catch(OperationCanceledException)when(exiting||Engine.LifetimeToken.IsCancellationRequested){receipt=new("failed",false,null,null,"interrupted");}
        catch(Exception exception)when(exception is not OutOfMemoryException){receipt=new("failed",false,null,null,"failed");}

        CompleteWindowStarterAttempt(attempt,receipt,result);
        var before=JsonSerializer.Serialize(windowResets.State,StorageJson.Options);
        try
        {
            windowResets.Complete(job,receipt,windowStarterNow());
            SaveWindowResetState();
        }
        catch(Exception exception)when(exception is IOException or UnauthorizedAccessException)
        {
            windowResets=new(JsonSerializer.Deserialize<WindowResetState>(before,StorageJson.Options));
            windowResetStateUnavailable=true;
            SetWindowStarterReport(job,"storageError",receipt,result);
            if(!exiting&&!Engine.LifetimeToken.IsCancellationRequested)await RefreshWindowStarterQuotaAsync(Engine.LifetimeToken);
            return;
        }
        if(!exiting&&!Engine.LifetimeToken.IsCancellationRequested)await RefreshWindowStarterQuotaAsync(Engine.LifetimeToken);
        var status=receipt.Completed&&receipt.Status=="success"?"success":receipt.FailureKind switch
        {"timeout"=>"timedOut","toolMissing"=>"toolMissing","auth"=>"auth","network"=>"network","turnIncomplete"=>"turnIncomplete","model"=>"model","modelSelection"=>"modelSelection","cancelled"=>"cancelled","interrupted"=>"interrupted",_=>"failed"};
        SetWindowStarterReport(job,status,receipt,result);
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
        public string? Model {get;set;}
        public string? ModelSource {get;set;}
    }
}

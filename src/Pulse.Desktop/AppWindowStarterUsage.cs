using System.Text.Json;
namespace Pulse.Desktop;
public sealed partial class PulseApp
{
    private WindowStarterLedger windowStarterLedger=new();
    private bool windowStarterLedgerUnavailable;
    private void LoadWindowStarterLedger()
    {
        var path=Path.Combine(Storage.Root,"window-starter-usage.json");
        try
        {
            var state=Storage.Load<WindowStarterLedgerState>("window-starter-usage.json");
            windowStarterLedgerUnavailable=(File.Exists(path)||Directory.Exists(path))&&!WindowStarterLedger.IsValid(state);
            windowStarterLedger=new(windowStarterLedgerUnavailable?null:state);
        }
        catch(UnauthorizedAccessException){windowStarterLedgerUnavailable=true;}
    }
    public int WindowStarterDailyLimit(AccountConfig account)
    {
        var text=account.Extra.GetValueOrDefault("windowStarterDailyLimit");
        return string.IsNullOrWhiteSpace(text)?0:int.TryParse(text,out var value)&&value is >=0 and <=1000?value:-1;
    }
    private bool WindowStarterBudgetAvailable(AccountConfig account)=>!windowStarterLedgerUnavailable&&windowStarterLedger.CanReserve(account.Id,windowStarterNow().ToLocalTime(),WindowStarterDailyLimit(account));
    private WindowStarterAttempt? ReserveWindowStarterAttempt(AccountConfig account)
    {
        if(!WindowStarterBudgetAvailable(account))return null;
        var next=new WindowStarterLedger(JsonSerializer.Deserialize<WindowStarterLedgerState>(JsonSerializer.Serialize(windowStarterLedger.State,StorageJson.Options),StorageJson.Options));
        var reservation=next.Reserve(account.Id,windowStarterNow().ToLocalTime(),WindowStarterDailyLimit(account));
        if(reservation is null)return null;
        Storage.Save("window-starter-usage.json",next.State);windowStarterLedger=next;return reservation;
    }
    private void CompleteWindowStarterAttempt(WindowStarterAttempt? attempt,WindowStarterReceipt receipt,ProcessResult? process)
    {
        if(attempt is null)return;
        try
        {
            var next=new WindowStarterLedger(JsonSerializer.Deserialize<WindowStarterLedgerState>(JsonSerializer.Serialize(windowStarterLedger.State,StorageJson.Options),StorageJson.Options));
            next.Complete(attempt.Id,windowStarterNow().ToLocalTime(),receipt,process?.Model,process?.ModelSource);
            Storage.Save("window-starter-usage.json",next.State);windowStarterLedger=next;
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException){windowStarterLedgerUnavailable=true;}
    }
    public string WindowStarterUsageSummary(string accountId)
    {
        if(windowStarterLedgerUnavailable)return Localization.Translate("无法读取或保存发送计数，自动和手动发送均已暂停。");
        var day=windowStarterLedger.Today(accountId,windowStarterNow().ToLocalTime());
        var totals=Localization.Format("今日已尝试 {0} 次 · 已记录输入 {1} / 输出 {2} Token",day.Attempts,day.InputTokens,day.OutputTokens);
        if(day.Attempts>day.MeasuredAttempts)totals+="\n"+Localization.Format("其中 {0} 次消耗未确认，不计为零。",day.Attempts-day.MeasuredAttempts);
        var entries=windowStarterLedger.State.History.Where(x=>x.AccountId==accountId).TakeLast(5).Reverse().Select(x=>
            Localization.Format("{0} · {1} · 输入 {2} / 输出 {3}",x.At.ToLocalTime().ToString("MM-dd HH:mm"),Localization.Translate(x.Status=="success"?"完成":x.Status=="failed"?"失败":"结果未确认"),x.InputTokens?.ToString()??"—",x.OutputTokens?.ToString()??"—"));
        return totals+Environment.NewLine+string.Join(Environment.NewLine,entries);
    }
}

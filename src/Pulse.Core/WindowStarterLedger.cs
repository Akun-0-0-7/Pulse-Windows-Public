namespace Pulse.Core;
public sealed class WindowStarterLedgerState
{
    public int SchemaVersion {get;set;}
    public List<WindowStarterDayUsage> Days {get;set;}=[];
    public List<WindowStarterAttempt> History {get;set;}=[];
}
public sealed class WindowStarterDayUsage
{
    public string AccountId {get;set;}="";
    public string Day {get;set;}="";
    public int Attempts {get;set;}
    public int MeasuredAttempts {get;set;}
    public long InputTokens {get;set;}
    public long OutputTokens {get;set;}
}
public sealed class WindowStarterAttempt
{
    public string Id {get;set;}="";
    public string AccountId {get;set;}="";
    public DateTimeOffset At {get;set;}
    public DateTimeOffset? CompletedAt {get;set;}
    public string Status {get;set;}="reserved";
    public string? Model {get;set;}
    public string? ModelSource {get;set;}
    public int? InputTokens {get;set;}
    public int? OutputTokens {get;set;}
}
public sealed class WindowStarterLedger
{
    public WindowStarterLedger(WindowStarterLedgerState? state=null)
    {
        State=state??new(){SchemaVersion=1};
        if(!IsValid(State))throw new InvalidDataException("Invalid hi usage history");
    }
    public WindowStarterLedgerState State {get;}
    private static string Day(DateTimeOffset at)=>at.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture);
    public static bool IsValid(WindowStarterLedgerState? value)=>value is {SchemaVersion:1,Days:not null,History:not null}&&
        value.Days.All(d=>d is not null&&!string.IsNullOrWhiteSpace(d.AccountId)&&DateOnly.TryParseExact(d.Day,"yyyy-MM-dd",out _)&&d.Attempts>=0&&d.MeasuredAttempts>=0&&d.MeasuredAttempts<=d.Attempts&&d.InputTokens>=0&&d.OutputTokens>=0&&d.InputTokens<=(long)d.MeasuredAttempts*int.MaxValue&&d.OutputTokens<=(long)d.MeasuredAttempts*int.MaxValue)&&
        value.Days.Select(d=>(d.AccountId,d.Day)).Distinct().Count()==value.Days.Count&&
        value.History.All(a=>a is not null&&Guid.TryParseExact(a.Id,"N",out _)&&!string.IsNullOrWhiteSpace(a.AccountId)&&a.At!=default&&
            a.Status is "reserved" or "success" or "failed"&&a.InputTokens is null or >=0&&a.OutputTokens is null or >=0&&
            (a.Model is null||CodexStarterModel.IsValidName(a.Model))&&a.ModelSource is null or "pulseSetting" or "codexConfig" or "codexDefault"&&
            value.Days.Any(d=>d.AccountId==a.AccountId&&d.Day==Day(a.At)))&&
        value.History.Select(a=>a.Id).Distinct().Count()==value.History.Count&&
        value.History.GroupBy(a=>(a.AccountId,Day(a.At))).All(group=>value.Days.First(d=>d.AccountId==group.Key.AccountId&&d.Day==group.Key.Item2).Attempts>=group.Count());
    public WindowStarterDayUsage Today(string account,DateTimeOffset now)=>State.Days.FirstOrDefault(d=>d.AccountId==account&&d.Day==Day(now))??new(){AccountId=account,Day=Day(now)};
    public bool CanReserve(string account,DateTimeOffset now,int limit)=>!string.IsNullOrWhiteSpace(account)&&now!=default&&limit is >=0 and <=1000&&
        Today(account,now).Attempts<int.MaxValue&&(limit==0||Today(account,now).Attempts<limit);
    public WindowStarterAttempt? Reserve(string account,DateTimeOffset now,int limit)
    {
        if(!CanReserve(account,now,limit))return null;
        var day=Today(account,now);if(!State.Days.Contains(day))State.Days.Add(day);day.Attempts++;
        var attempt=new WindowStarterAttempt{Id=Guid.NewGuid().ToString("N"),AccountId=account,At=now};State.History.Add(attempt);
        var oldest=Day(now.AddDays(-31));State.Days.RemoveAll(d=>string.CompareOrdinal(d.Day,oldest)<0);
        State.History.RemoveAll(a=>string.CompareOrdinal(Day(a.At),oldest)<0);
        if(State.History.Count>200)State.History.RemoveRange(0,State.History.Count-200);
        return attempt;
    }
    public void Complete(string id,DateTimeOffset at,WindowStarterReceipt receipt,string? model,string? source)
    {
        var entry=State.History.FirstOrDefault(a=>a.Id==id);if(entry is null||entry.CompletedAt is not null)return;
        entry.CompletedAt=at;entry.Status=receipt.Completed&&receipt.Status=="success"?"success":"failed";
        entry.Model=CodexStarterModel.IsValidName(model)?model:null;
        entry.ModelSource=source is "pulseSetting" or "codexConfig" or "codexDefault"?source:null;
        if(receipt.InputTokens is >=0&&receipt.OutputTokens is >=0)
        {
            entry.InputTokens=receipt.InputTokens;entry.OutputTokens=receipt.OutputTokens;
            var day=Today(entry.AccountId,entry.At);day.MeasuredAttempts++;
            day.InputTokens+=receipt.InputTokens.Value;day.OutputTokens+=receipt.OutputTokens.Value;
        }
    }
}

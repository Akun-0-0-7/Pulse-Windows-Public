using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Pulse.Core;
using Pulse.Desktop;

internal static partial class Program
{
    private static void WindowStarterRecoveryChecks(PulseApp app)
    {
        var environment=new[]{"OPENAI_API_KEY","CODEX_API_KEY","CODEX_HOME"}.ToDictionary(k=>k,Environment.GetEnvironmentVariable);
        var auth=Field<Func<string,string?>>(app,"windowStarterAuthReader");
        var config=app.Engine.Config;
        var accounts=config.Accounts;var enabled=config.WindowStarterEnabled;var start=config.WindowStarterStartHour;var end=config.WindowStarterEndHour;
        var coordinator=Field<WindowResetCoordinator>(app,"windowResets");var saved=Field<string>(app,"savedWindowResetState");
        var reports=Field<Dictionary<string,PulseApp.WindowStarterReport>>(app,"windowStarterReports");
        var unavailable=Field<bool>(app,"windowResetStateUnavailable");
        var path=Path.Combine(app.Storage.Root,"window-starter-state.json");var original=File.Exists(path)?File.ReadAllBytes(path):null;
        var delegates=new Dictionary<string,object?>();
        var usagePath=Path.Combine(app.Storage.Root,"window-starter-usage.json");var usageOriginal=File.Exists(usagePath)?File.ReadAllBytes(usagePath):null;
        var ledger=Field<WindowStarterLedger>(app,"windowStarterLedger");var ledgerUnavailable=Field<bool>(app,"windowStarterLedgerUnavailable");
        try
        {
            foreach(var key in environment.Keys)Environment.SetEnvironmentVariable(key,null);
            SetField(app,"windowStarterAuthReader",(Func<string,string?>)(_=>"{\"accountId\":\"recovery-fixture\",\"accessToken\":\"fixture-token\"}"));
            var account=new AccountConfig{Id="recovery-fixture",ProviderId="codex",Enabled=true,Extra=new(){{"windowStarter","true"}}};
            config.Accounts=[account];config.WindowStarterEnabled=true;config.WindowStarterStartHour=0;config.WindowStarterEndHour=0;
            using var settingsClose=new WindowCloser(new SettingsWindow(app));var settings=settingsClose.Window;
            settings.Navigate("account:"+account.Id);
            if(!Descendants((DependencyObject)settings.Content).OfType<Button>().Any(b=>Equals(b.Content,"再次发送 hi 并刷新额度")))
                throw new Exception("Account settings lacks a manual hi retry action.");

            foreach(var name in new[]{"windowStarterNow","windowStarterSend","windowStarterRefresh"})
            {
                var field=typeof(PulseApp).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic);
                if(field is null)throw new Exception("Missing isolated window starter hook: "+name);
                delegates[name]=field.GetValue(app);
            }
            var now=DateTimeOffset.Parse("2026-10-27T12:00:00+08:00");var sends=0;var refreshes=0;var weeklyBlocked=false;
            var result=SuccessHi();
            var readings=Field<ConcurrentDictionary<string,UsageReading>>(app.Engine,"readings");
            SetField(app,"windowStarterNow",(Func<DateTimeOffset>)(()=>now));
            SetField(app,"windowStarterRefresh",(Func<System.Threading.CancellationToken,Task>)(_=>
            {
                refreshes++;
                readings[account.Id]=new(){AccountId=account.Id,ProviderId="codex",ObservedAt=now,Windows=[
                    new(){Id="primary",Kind="fiveHour",UsedPercent=0,ResetsAt=now.AddHours(5)},
                    new(){Id="weekly",Kind="weekly",UsedPercent=weeklyBlocked?100:20,Exhausted=weeklyBlocked,ResetsAt=now.AddDays(2)}]};
                return Task.CompletedTask;
            }));
            SetField(app,"windowStarterSend",(Func<AccountConfig,System.Threading.CancellationToken,Task<ProcessResult>>)((_,_)=>{sends++;return Task.FromResult(result);}));
            void Reset()
            {
                SetField(app,"windowResets",new WindowResetCoordinator());SetField(app,"savedWindowResetState","");
                SetField(app,"windowResetStateUnavailable",false);SetField(app,"nextWindowStarterRefreshAt",DateTimeOffset.MinValue);
                SetField(app,"windowStarterReports",new Dictionary<string,PulseApp.WindowStarterReport>());
                config.WindowStarterStartHour=0;config.WindowStarterEndHour=0;
                sends=0;refreshes=0;weeklyBlocked=false;
                SetField(app,"windowStarterLedger",new WindowStarterLedger());SetField(app,"windowStarterLedgerUnavailable",false);account.Extra.Remove("windowStarterDailyLimit");
            }
            void RunTask(string method,params object[] args)
            {
                var task=(Task)typeof(PulseApp).GetMethod(method,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(app,args)!;
                WaitUntil(()=>task.IsCompleted,"Window starter fixture did not finish.");task.GetAwaiter().GetResult();
            }
            Reset();RunTask("StartDueWindows");
            Equal(1,sends,"First full quota send");
            if(!File.Exists(path))throw new Exception("Successful outcome was not persisted.");
            now=now.AddMinutes(1);RunTask("StartDueWindows");RunTask("RetryWindowStarterAsync",account.Id);
            Equal(1,sends,"Persistent full quota and manual retry must not duplicate success");
            if(refreshes<2)throw new Exception("Independent recovery polling did not refresh quota.");
            var refreshCount=refreshes;RunTask("StartDueWindows");Equal(refreshCount,refreshes,"One-minute polling limit");
            now=DateTimeOffset.Parse("2026-10-27T22:00:00+08:00");config.WindowStarterStartHour=9;config.WindowStarterEndHour=18;
            RunTask("StartDueWindows");Equal(1,sends,"New automatic cycle waits outside allowed hours");
            if(!app.CanRetryWindowStarter(account.Id))throw new Exception("A confirmed new cycle did not enable the manual action outside automatic hours.");
            RunTask("RetryWindowStarterAsync",account.Id);Equal(2,sends,"New manual cycle after prior success");

            Reset();account.Extra["windowStarterDailyLimit"]="2";result=new(1,"","dns lookup failed",false);
            RunTask("StartDueWindows");RunTask("RetryWindowStarterAsync",account.Id);RunTask("RetryWindowStarterAsync",account.Id);
            Equal(2,sends,"Automatic and manual attempts share daily cap");
            Invoke(app,"LoadWindowStarterLedger");now=now.AddMinutes(5);RunTask("StartDueWindows");Equal(2,sends,"Restart must not reset daily cap");
            if(!app.WindowStarterUsageSummary(account.Id).Contains("消耗未确认"))throw new Exception("Unknown token use was presented as zero.");
            now=now.AddDays(1);RunTask("StartDueWindows");Equal(3,sends,"Next local day permits retry");
            Reset();File.WriteAllText(usagePath,"{}");Invoke(app,"LoadWindowStarterLedger");RunTask("RetryWindowStarterAsync",account.Id);Equal(0,sends,"Corrupt usage ledger must not reset quota");
            if(!app.WindowStarterStatus(account.Id).Contains("计数"))throw new Exception("Corrupt usage ledger lacks explanation.");
            Reset();if(File.Exists(usagePath))File.Delete(usagePath);Directory.CreateDirectory(usagePath);
            try{RunTask("RetryWindowStarterAsync",account.Id);Equal(0,sends,"Failed daily reservation must prevent dispatch");}
            finally{Directory.Delete(usagePath);}

            Reset();result=new(1,"","dns lookup failed",false);RunTask("StartDueWindows");Equal(1,sends,"Initial network rejection");
            Invoke(app,"LoadWindowResetState");
            now=now.AddMinutes(1);weeklyBlocked=true;RunTask("StartDueWindows");Equal(1,sends,"Weekly exhaustion blocks retries");
            now=now.AddMinutes(1);weeklyBlocked=false;result=SuccessHi();RunTask("StartDueWindows");Equal(2,sends,"Recovered network retry after restart");
            now=now.AddMinutes(1);RunTask("StartDueWindows");Equal(2,sends,"Successful retry deduplication");

            Reset();Field<Dictionary<string,PulseApp.WindowStarterReport>>(app,"windowStarterReports")[account.Id]=new(){Status="success",ProviderId="codex",At=now};
            RunTask("RetryWindowStarterAsync",account.Id);Equal(0,sends,"Legacy successful history cannot be manually replayed");

            Reset();result=new(-1,"","",true);now=DateTimeOffset.Parse("2026-10-27T21:00:00+08:00");
            config.WindowStarterStartHour=9;config.WindowStarterEndHour=18;
            RunTask("RetryWindowStarterAsync",account.Id);Equal(1,sends,"Manual retry bypasses automatic hours");
            now=now.AddMinutes(2);RunTask("StartDueWindows");Equal(1,sends,"Uncertain timeout does not auto replay");
            result=SuccessHi();RunTask("RetryWindowStarterAsync",account.Id);Equal(2,sends,"Explicit manual retry recovers uncertain timeout");

            Reset();config.WindowStarterStartHour=0;config.WindowStarterEndHour=0;
            var release=new TaskCompletionSource<ProcessResult>();
            SetField(app,"windowStarterSend",(Func<AccountConfig,System.Threading.CancellationToken,Task<ProcessResult>>)((_,_)=>{sends++;return release.Task;}));
            var manual=typeof(PulseApp).GetMethod("RetryWindowStarterAsync")!;
            var pending=(Task)manual.Invoke(app,[account.Id])!;
            RunTask("RetryWindowStarterAsync",account.Id);RunTask("StartDueWindows");Equal(1,sends,"Timer and manual calls are single flight");
            release.SetResult(SuccessHi());WaitUntil(()=>pending.IsCompleted,"Delayed send did not finish.");pending.GetAwaiter().GetResult();

            Reset();SetField(app,"windowStarterSend",(Func<AccountConfig,System.Threading.CancellationToken,Task<ProcessResult>>)((_,_)=>{sends++;return Task.FromResult(SuccessHi());}));
            File.Delete(path);Directory.CreateDirectory(path);
            try{RunTask("RetryWindowStarterAsync",account.Id);Equal(0,sends,"Failed claim persistence prevents send");}
            finally{Directory.Delete(path);}

            Reset();string? claimedJson=null;
            SetField(app,"windowStarterSend",(Func<AccountConfig,System.Threading.CancellationToken,Task<ProcessResult>>)((_,_)=>
            {
                sends++;claimedJson=File.ReadAllText(path);File.Delete(path);Directory.CreateDirectory(path);return Task.FromResult(SuccessHi());
            }));
            try
            {
                RunTask("RetryWindowStarterAsync",account.Id);Equal(1,sends,"Outcome persistence fixture sent once");
                Equal(2,refreshes,"Failed outcome persistence still refreshes quota after the send");
                if(!Field<bool>(app,"windowResetStateUnavailable"))throw new Exception("Failed outcome persistence did not pause sending.");
                RunTask("StartDueWindows");Equal(1,sends,"Failed outcome persistence cannot replay success");
            }
            finally{Directory.Delete(path);}
            File.WriteAllText(path,claimedJson!);Invoke(app,"LoadWindowResetState");RunTask("StartDueWindows");
            Equal(1,sends,"Restored in-flight history stays uncertain after restart");

            Reset();SetField(app,"windowStarterSend",(Func<AccountConfig,System.Threading.CancellationToken,Task<ProcessResult>>)((_,_)=>{sends++;return Task.FromResult(SuccessHi());}));
            SetField(app,"windowStarterRefresh",(Func<System.Threading.CancellationToken,Task>)(async _=>{await app.ClearCredentialsAsync(account);}));
            RunTask("RetryWindowStarterAsync",account.Id);Equal(0,sends,"Credential version changed during refresh cancels sending");

            Reset();SetField(app,"windowStarterRefresh",(Func<System.Threading.CancellationToken,Task>)(_=>
            {
                SetField(app,"windowStarterAuthReader",(Func<string,string?>)(_=>null));return Task.CompletedTask;
            }));
            RunTask("RetryWindowStarterAsync",account.Id);Equal(0,sends,"Identity change during refresh prevents send");
            Console.WriteLine("PASS: recovery availability detection, manual action, restart retry, legacy success deduplication, weekly guard, timeout handling, polling, single-flight, claim/outcome persistence barriers and identity changes.");
        }
        finally
        {
            foreach(var pair in delegates)typeof(PulseApp).GetField(pair.Key,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(app,pair.Value);
            SetField(app,"windowStarterAuthReader",auth);SetField(app,"windowResets",coordinator);SetField(app,"savedWindowResetState",saved);SetField(app,"windowResetStateUnavailable",unavailable);
            SetField(app,"windowStarterReports",reports);
            SetField(app,"windowStarterLedger",ledger);SetField(app,"windowStarterLedgerUnavailable",ledgerUnavailable);
            if(Directory.Exists(usagePath))Directory.Delete(usagePath);
            if(usageOriginal is null){if(File.Exists(usagePath))File.Delete(usagePath);}else File.WriteAllBytes(usagePath,usageOriginal);
            if(Directory.Exists(path))Directory.Delete(path);
            if(original is null){if(File.Exists(path))File.Delete(path);}else File.WriteAllBytes(path,original);
            config.Accounts=accounts;config.WindowStarterEnabled=enabled;config.WindowStarterStartHour=start;config.WindowStarterEndHour=end;
            foreach(var pair in environment)Environment.SetEnvironmentVariable(pair.Key,pair.Value);
        }
    }
    private static ProcessResult SuccessHi()=>new(0,"{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":1,\"output_tokens\":1}}","",false);
    private sealed class WindowCloser(SettingsWindow window):IDisposable
    {
        public SettingsWindow Window=>window;
        public void Dispose()=>window.Close();
    }
}

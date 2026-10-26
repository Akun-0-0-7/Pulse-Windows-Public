using Pulse.Core;
namespace Pulse.Tests;

public static class RefreshQueueTests
{
    sealed class Secrets:ISecretStore
    {
        public string? Get(string key)=>null;
        public void Set(string key,string? value){}
    }
    sealed class BlockingReader:IUsageProvider
    {
        readonly TaskCompletionSource started=new(TaskCreationOptions.RunContinuationsAsynchronously);
        readonly TaskCompletionSource release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls;
        public Task Started=>started.Task;
        public void Release()=>release.TrySetResult();
        public async Task<UsageReading> FetchAsync(FetchContext context)
        {
            var first=Interlocked.Increment(ref calls)==1;
            if(first){started.TrySetResult();await release.Task.WaitAsync(context.CancellationToken);}
            return new(){AccountId=context.Account.Id,ProviderId=context.Account.ProviderId,ObservedAt=DateTimeOffset.UtcNow,Plan=first?"outdated":"fresh-"+context.Account.Id};
        }
    }
    sealed class Fixture:IDisposable
    {
        readonly string root=Path.Combine(Path.GetTempPath(),"pulse-refresh-queue-"+Guid.NewGuid().ToString("N"));
        public AppStorage Storage {get;}
        public BlockingReader Reader {get;}=new();
        public UsageEngine Engine {get;}
        public Fixture()
        {
            Storage=new(root);Storage.SaveConfig(new(){Accounts=[new(){Id="first",ProviderId="fixture",Enabled=true},new(){Id="second",ProviderId="fixture",Enabled=false}]});
            Engine=new(Storage,new Secrets(),_=>Reader);
        }
        public void Dispose(){Engine.Dispose();Directory.Delete(root,true);}
    }
    [Check] public static async Task RequestedRefreshWaitsAndReadsNewlyEnabledAccount()
    {
        using var fixture=new Fixture();var active=fixture.Engine.RefreshAsync();Task? requested=null;
        try
        {
            await fixture.Reader.Started.WaitAsync(TimeSpan.FromSeconds(5));
            fixture.Engine.Config.Accounts[1].Enabled=true;fixture.Engine.Save();
            requested=fixture.Engine.RefreshAsync(waitForCurrent:true);
            Assert.True(!requested.IsCompleted,"An explicitly requested refresh was dropped while an earlier refresh was active.");
            fixture.Reader.Release();await Task.WhenAll(active,requested).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("fresh-first",fixture.Engine.Reading("first")?.Plan);Assert.Equal("fresh-second",fixture.Engine.Reading("second")?.Plan);
            var saved=fixture.Storage.LoadCache();Assert.Equal(2,saved.Count);Assert.Equal("fresh-second",saved.Single(reading=>reading.AccountId=="second").Plan);
        }
        finally {fixture.Reader.Release();await active.WaitAsync(TimeSpan.FromSeconds(5));if(requested is not null)await requested.WaitAsync(TimeSpan.FromSeconds(5));}
    }
    [Check] public static async Task CancellingWaitingRefreshLeavesActiveAndFutureRefreshUsable()
    {
        using var fixture=new Fixture();var active=fixture.Engine.RefreshAsync();
        try
        {
            await fixture.Reader.Started.WaitAsync(TimeSpan.FromSeconds(5));using var cancellation=new CancellationTokenSource();
            var requested=fixture.Engine.RefreshAsync(cancellation.Token,waitForCurrent:true);cancellation.Cancel();
            var cancelled=false;try {await requested.WaitAsync(TimeSpan.FromSeconds(5));}catch(OperationCanceledException){cancelled=true;}
            Assert.True(cancelled,"Cancelling a waiting refresh did not cancel its wait.");Assert.True(fixture.Engine.IsRefreshing,"Cancelling a waiter changed the active refresh state.");
            fixture.Reader.Release();await active.WaitAsync(TimeSpan.FromSeconds(5));await fixture.Engine.RefreshAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("fresh-first",fixture.Engine.Reading("first")?.Plan);Assert.True(!fixture.Engine.IsRefreshing);
        }
        finally {fixture.Reader.Release();await active.WaitAsync(TimeSpan.FromSeconds(5));}
    }
    [Check] public static async Task BackgroundRefreshStillSkipsAnActiveRefresh()
    {
        using var fixture=new Fixture();var active=fixture.Engine.RefreshAsync();
        try
        {
            await fixture.Reader.Started.WaitAsync(TimeSpan.FromSeconds(5));
            await fixture.Engine.RefreshAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(fixture.Engine.IsRefreshing,"A skipped background refresh changed the active refresh state.");
            fixture.Reader.Release();await active.WaitAsync(TimeSpan.FromSeconds(5));Assert.Equal("outdated",fixture.Engine.Reading("first")?.Plan);
        }
        finally {fixture.Reader.Release();await active.WaitAsync(TimeSpan.FromSeconds(5));}
    }
}

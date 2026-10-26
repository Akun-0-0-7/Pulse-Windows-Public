using Pulse.Core;
using System.Text.Json;
if(args.Length is <1 or >2||args[0]!="codex"){Console.Error.WriteLine("Explicit provider argument required: codex [tooling]");return 2;}
var account=new AccountConfig {Id="live-check",ProviderId="codex",Enabled=true,Route=args.Length==2?args[1]:"automatic"};using var http=UsageEngine.CreateHttp(new());using var cancellation=new CancellationTokenSource(TimeSpan.FromSeconds(55));
try{var result=await HandwrittenProviders.TryCreate("codex")!.FetchAsync(new(account,null,http,LocalPaths.Home,Path.GetTempPath(),cancellation.Token));Console.WriteLine(JsonSerializer.Serialize(new{at=DateTimeOffset.UtcNow,success=result.Success,source=result.Source,plan=result.Plan,windows=result.Windows.Select(w=>new{w.Kind,w.Scope,w.UsedPercent,w.Exhausted,w.ResetsAt}),error=result.ErrorCode},StorageJson.Options));return result.Success?0:1;}
catch(Exception exception)when(exception is OperationCanceledException or IOException or System.ComponentModel.Win32Exception or System.Net.Http.HttpRequestException){Console.WriteLine("{\"success\":false,\"error\":\"liveReadFailed\"}");return 1;}

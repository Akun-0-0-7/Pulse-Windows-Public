// Protocol translated from Pulse 1.12.2 (qunqin24), Apache-2.0. Windows process adaptation.
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Pulse.Core;
public sealed record ExtensionDefinition(string Id,string Name,string Executable,string Directory,int TimeoutSeconds,string? Error);
public sealed class ExtensionService
{
 public IReadOnlyList<ExtensionDefinition> Discover(string root)
 {
     var result=new List<ExtensionDefinition>(); var seen=new HashSet<string>(StringComparer.Ordinal);
     if(!Directory.Exists(root)) return result;
     foreach(var folder in Directory.EnumerateDirectories(root).OrderBy(Path.GetFileName,StringComparer.OrdinalIgnoreCase))
     {
         var id=Path.GetFileName(folder); var name=id; var executable=""; var timeout=20; string? error=null;
         try
         {
             using var document=JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,"pulse-extension.json"))); var json=document.RootElement;
             if(JsonTools.Number(json,"schemaVersion")!=1) throw new InvalidDataException("扩展清单只支持 schemaVersion 1。");
             id=JsonTools.String(json,"id")??"";
             if(!Regex.IsMatch(id,"^[a-z0-9][a-z0-9._-]{0,63}$")) throw new InvalidDataException("扩展 id 必须由 1–64 个小写字母、数字、点、横线或下划线组成。");
             name=Cut(JsonTools.String(json,"name"),60)??throw new InvalidDataException("扩展缺少名称。");
             var relative=JsonTools.String(json,"executable")??throw new InvalidDataException("扩展缺少运行文件。");
             if(Path.IsPathRooted(relative)) throw new InvalidDataException("运行文件必须位于扩展目录内。");
             executable=Resolve(Path.GetFullPath(Path.Combine(folder,relative)));
             var resolvedFolder=Resolve(Path.GetFullPath(folder)).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
             if(!executable.StartsWith(resolvedFolder,StringComparison.OrdinalIgnoreCase)||!File.Exists(executable)) throw new InvalidDataException("运行文件必须是扩展目录内的现有文件。");
             if(!new[]{".exe",".com",".cmd",".bat",".ps1"}.Contains(Path.GetExtension(executable),StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException("Windows 扩展运行文件需为 exe、com、cmd、bat 或 ps1。");
             timeout=(int)Math.Clamp(JsonTools.Number(json,"timeoutSeconds")??20,1,60);
             if(!seen.Add(id)) throw new InvalidDataException("扩展 id 重复；按目录名称排序的第一个保留。");
         }
         catch(Exception e) when(e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException) { error=e.Message; }
         result.Add(new(id,name,executable,folder,timeout,error));
     }
     return result;
 }
 // Resolve each directory link, not merely the final file. A junction cannot escape the displayed folder.
 static string Resolve(string path)
 {
     var root=Path.GetPathRoot(path)!; var current=root;
     foreach(var part in path[root.Length..].Split(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar))
     {
         current=Path.Combine(current,part); FileSystemInfo info=Directory.Exists(current)?new DirectoryInfo(current):new FileInfo(current);
         if(info.Exists&&info.LinkTarget is not null) current=info.ResolveLinkTarget(true)?.FullName??current;
     }
     return Path.GetFullPath(current);
 }
 public async Task<UsageReading> RunAsync(ExtensionDefinition extension,AppConfig config,CancellationToken cancellationToken=default)
 {
     if(!config.EnabledExtensions.Contains(extension.Id,StringComparer.Ordinal)) return Failure(extension.Id,"disabled","扩展尚未启用。");
     if(extension.Error is not null) return Failure(extension.Id,"invalidManifest",extension.Error);
     try
     {
         var current=Discover(Path.GetDirectoryName(extension.Directory)!).FirstOrDefault(x=>x.Id==extension.Id&&x.Directory==extension.Directory);
         if(current is null||current.Error is not null||current.Executable!=extension.Executable) return Failure(extension.Id,"cannotRun","扩展运行文件已改变，请重新扫描。");
         var environment=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
         foreach(var key in new[]{"SystemRoot","WINDIR","COMSPEC","USERPROFILE","HOME","USERNAME","TEMP","TMP","LANG","LC_ALL","PATH","PATHEXT"}) if(Environment.GetEnvironmentVariable(key) is { } value) environment[key]=value;
         if(config.ProxyMode=="system") foreach(var key in new[]{"HTTP_PROXY","HTTPS_PROXY","ALL_PROXY","NO_PROXY"}) if(Environment.GetEnvironmentVariable(key) is { } value) environment[key]=value;
         if(config.ProxyMode=="custom"&&!string.IsNullOrWhiteSpace(config.ProxyAddress)) { environment["HTTP_PROXY"]=config.ProxyAddress; environment["HTTPS_PROXY"]=config.ProxyAddress; environment["ALL_PROXY"]=config.ProxyAddress; }
         environment["PULSE_EXTENSION_ID"]=extension.Id; environment["PULSE_EXTENSION_SCHEMA"]="1";
         var file=extension.Executable; IEnumerable<string> arguments=[];
         if(Path.GetExtension(file).Equals(".ps1",StringComparison.OrdinalIgnoreCase)) { arguments=["-NoLogo","-NoProfile","-NonInteractive","-File",file]; file=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell/v1.0/powershell.exe"); }
         else if(new[]{".cmd",".bat"}.Contains(Path.GetExtension(file),StringComparer.OrdinalIgnoreCase)) { arguments=["/d","/s","/c","\""+file+"\""]; file=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"cmd.exe"); }
         var run=await ProcessRunner.RunAsync(file,arguments,TimeSpan.FromSeconds(extension.TimeoutSeconds),cancellationToken,extension.Directory,environment,262144);
         if(run.TimedOut) return Failure(extension.Id,"timeout","扩展未在时限内答复。");
         if(run.ExitCode!=0) return Failure(extension.Id,"processError","扩展运行失败。");
         return Parse(extension.Id,run.Output);
     }
     catch(Exception e) when(e is IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException) { return Failure(extension.Id,"cannotRun","扩展运行文件无法启动。"); }
 }
 public static UsageReading Parse(string id,string json)
 {
     if(Encoding.UTF8.GetByteCount(json)>262144) return Failure(id,"invalidReply","扩展输出超过 256 KiB。");
     try
     {
         using var document=JsonDocument.Parse(json); var root=document.RootElement;
         if(root.ValueKind!=JsonValueKind.Object||JsonTools.Number(root,"schemaVersion")!=1) return Failure(id,"invalidReply","无法读取扩展答复。");
         var status=JsonTools.String(root,"status")??"ok";
         if(status!="ok") return status switch { "signedOut"=>Failure(id,status,"扩展登录已过期。"), "unreachable"=>Failure(id,status,"服务没有响应。"), "rateLimited"=>Failure(id,status,"读取过于频繁。"), "serverError"=>Failure(id,status,"服务返回错误。"), "noLimits"=>Failure(id,status,"没有上报限额。"), _=>Failure(id,"invalidReply","无法读取扩展答复。") };
         var reading=new UsageReading{AccountId="extension#"+id,ProviderId="extension",Source="extension",ObservedAt=DateTimeOffset.UtcNow,Plan=Cut(JsonTools.String(root,"plan"),60)};
         var ids=new HashSet<string>(); var position=0;
         foreach(var limit in JsonTools.Array(root,"limits"))
         {
             var row=position++; var label=Cut(JsonTools.String(limit,"label"),60); var used=JsonTools.Number(limit,"usedPercent");
             if(used is null&&JsonTools.Number(limit,"used") is {} amount&&JsonTools.Number(limit,"limit") is {} max&&max>0) used=100*amount/max;
             if(label is null||used is null||used<0||!double.IsFinite(used.Value)) continue;
             var key=Cut(JsonTools.String(limit,"id"),64)??row.ToString(); if(!ids.Add(key)) continue;
             var seconds=JsonTools.Number(limit,"windowSeconds");
             reading.Windows.Add(new(){Id=key,Label=label,UsedPercent=used.Value,Exhausted=used>=100,ResetsAt=JsonTools.Date(limit,"resetsAt"),WindowSeconds=seconds>0?seconds:null,ReportsLength=seconds>0});
         }
         var currency=JsonTools.String(root,"balance.currency"); var balance=JsonTools.Decimal(root,"balance.amount");
         if(balance is not null&&currency is not null&&Regex.IsMatch(currency,"^[A-Za-z]{3}$")) reading.Balance=new(balance.Value,currency.ToUpperInvariant());
         ids.Clear(); position=0;
         foreach(var metric in JsonTools.Array(root,"metrics"))
         {
             var row=position++; var label=Cut(JsonTools.String(metric,"label"),60); var value=JsonTools.Number(metric,"value"); if(label is null||value is null||!double.IsFinite(value.Value)) continue;
             var key=Cut(JsonTools.String(metric,"id"),64)??row.ToString(); if(!ids.Add(key)) continue;
             var history=new Dictionary<DateTimeOffset,MetricPoint>();
             foreach(var p in JsonTools.Array(metric,"history")) if(JsonTools.Date(p,"t") is {} t&&JsonTools.Number(p,"v") is {} v&&double.IsFinite(v)) history.TryAdd(t,new(t,v));
             reading.Metrics.Add(new(key,label,value.Value,Cut(JsonTools.String(metric,"unit"),16),history.Values.OrderBy(p=>p.T).TakeLast(120).ToList()));
             if(reading.Metrics.Count==4) break;
         }
         if(reading.Windows.Count==0&&reading.Balance is null) { reading.ErrorCode="noLimits"; reading.ErrorMessage="没有上报限额。"; }
         return reading;
     }
     catch(JsonException) { return Failure(id,"invalidReply","无法读取扩展答复。"); }
 }
 static string? Cut(string? value,int length) { value=value?.Trim(); return string.IsNullOrEmpty(value)?null:value[..Math.Min(value.Length,length)]; }
 static UsageReading Failure(string id,string code,string message)=>new(){AccountId="extension#"+id,ProviderId="extension",Source="extension",ErrorCode=code,ErrorMessage=message};
}

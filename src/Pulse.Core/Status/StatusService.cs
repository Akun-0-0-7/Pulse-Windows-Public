// Status adapters translated from Pulse 1.12.2 (qunqin24), Apache-2.0.
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Pulse.Core;
public sealed record StatusDay(DateTimeOffset At,string? State,int? Hours=null);
public sealed record StatusComponent(string Id,string Name,string State,string? Group=null,double? Uptime=null,List<StatusDay>? Days=null) { public List<StatusDay> History=>Days??[]; }
public sealed record StatusIncident(string Id,string Name,string State,DateTimeOffset? StartedAt,DateTimeOffset? ResolvedAt);
public sealed record StatusPageDefinition(string ProviderId,string Address,string Company,string Format,string Feed,string? Shows=null,string? Notifies=null);
public sealed class StatusReport
{
 public string ProviderId {get;init;}=""; public string? PageAddress {get;init;} public DateTimeOffset ObservedAt {get;init;}=DateTimeOffset.UtcNow;
 public List<StatusComponent> Components {get;set;}=[]; public List<StatusIncident> Incidents {get;set;}=[]; public string? Error {get;set;}
 public bool HasOutage=>Components.Any(x=>x.State is "degraded" or "partialOutage" or "fullOutage");
 public bool HasNotifiableOutage=>Components.Any(x=>x.State is "degraded" or "partialOutage" or "fullOutage"&&StatusService.Matches(StatusService.Pages.FirstOrDefault(p=>p.ProviderId==ProviderId)?.Notifies,x));
}
public sealed class StatusService
{
 public static IReadOnlyList<StatusPageDefinition> Pages {get;}=StatusPages.Build();
 public async Task<StatusReport> FetchAsync(string providerId,HttpClient http,CancellationToken cancellationToken=default,bool minimaxChina=false)
 {
     var page=Pages.FirstOrDefault(x=>x.ProviderId==providerId); if(page is null) return new(){ProviderId=providerId,Error="该服务没有配置可读取的官方状态页。"};
     if(minimaxChina&&providerId=="minimax") page=page with{Address="https://status.minimaxi.com",Feed="https://status.minimaxi.com/api/v2/summary.json",Notifies="ids:vwp8mgy34fck"};
     using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(15));
     try
     {
         var body=await Get(http,page.Feed,timeout.Token); var report=ParsePage(page,body,DateTimeOffset.UtcNow);
         if(report.Error is not null) return report;
         try
         {
             if(page.Format=="statuspage")
             {
                 var ids=string.Join(',',report.Components.Select(x=>x.Id)); var history=await Get(http,page.Address+"/uptime_showcase?components="+Uri.EscapeDataString(ids),timeout.Token);
                 ApplyStatuspageHistory(report,history);
             }
             else if(page.Format=="incidentIO")
             {
                 var now=DateTimeOffset.UtcNow; var start=now.Date.AddDays(page.ProviderId is "codex" or "openAIPlatform"?-90:-89); var end=now.Date.AddDays(1);
                 var history=await Get(http,page.Feed+"/component_impacts?start_at="+Uri.EscapeDataString(start.ToString("O"))+"&end_at="+Uri.EscapeDataString(end.ToString("O")),timeout.Token);
                 using var doc=JsonDocument.Parse(history); ApplyImpacts(report,doc.RootElement,now,days:page.ProviderId is "codex" or "openAIPlatform"?91:90);
             }
         }
         catch(Exception e) when(e is HttpRequestException or JsonException or OperationCanceledException) { cancellationToken.ThrowIfCancellationRequested(); /* Current state remains valid; absent history stays absent. */ }
         return report;
     }
     catch(Exception e) when(e is HttpRequestException or JsonException or OperationCanceledException or IOException) { cancellationToken.ThrowIfCancellationRequested(); return new(){ProviderId=providerId,PageAddress=page.Address,Error="官方状态页读取失败（"+e.GetType().Name+"）。"}; }
 }
 static async Task<string> Get(HttpClient client,string url,CancellationToken ct)
 {
     using var request=new HttpRequestMessage(HttpMethod.Get,url); request.Headers.TryAddWithoutValidation("Accept","application/json,text/html;q=0.8");
     using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct); response.EnsureSuccessStatusCode();
     await using var stream=await response.Content.ReadAsStreamAsync(ct); using var bytes=new MemoryStream(); var buffer=new byte[16384]; int count;
     while((count=await stream.ReadAsync(buffer,ct))>0) { if(bytes.Length+count>8*1024*1024) throw new IOException("状态页答复超过 8 MiB。"); bytes.Write(buffer,0,count); }
     return Encoding.UTF8.GetString(bytes.ToArray());
 }
 public static StatusReport Parse(string providerId,string json)=>Pages.FirstOrDefault(x=>x.ProviderId==providerId) is {} page?ParsePage(page,json,DateTimeOffset.UtcNow):new(){ProviderId=providerId,Error="没有官方状态页配置。"};
 static StatusReport ParsePage(StatusPageDefinition page,string body,DateTimeOffset now)
 {
     var report=new StatusReport{ProviderId=page.ProviderId,PageAddress=page.Address,ObservedAt=now};
     try
     {
         if(page.Format=="flashcat") body=FlashcatData(body)??throw new JsonException("未找到 Next.js 状态数据。");
         using var doc=JsonDocument.Parse(body); var root=doc.RootElement;
         switch(page.Format)
         {
             case "statuspage":
                 foreach(var c in JsonTools.Array(root,"components").OrderBy(c=>JsonTools.Number(c,"position")??double.MaxValue))
                 {
                     if(JsonTools.Bool(c,"group")==true||JsonTools.Bool(c,"only_show_if_degraded")==true&&JsonTools.String(c,"status")=="operational") continue;
                     Add(c,"id","name","status",JsonTools.String(c,"group_id"));
                 }
                 break;
             case "incidentIO":
                 var affected=JsonTools.Array(root,"summary.affected_components").Where(x=>JsonTools.String(x,"component_id") is not null).GroupBy(x=>JsonTools.String(x,"component_id")!).ToDictionary(g=>g.Key,g=>State(JsonTools.String(g.First(),"status")));
                 void AddMember(JsonElement m,string? group) { if(JsonTools.Bool(m,"hidden")==true) return; var id=JsonTools.String(m,"component_id"); var name=JsonTools.String(m,"name"); if(id is not null&&name is not null) report.Components.Add(new(id,name,affected.GetValueOrDefault(id,"operational"),group)); }
                 foreach(var item in JsonTools.Array(root,"summary.structure.items"))
                 {
                     var group=JsonTools.At(item,"group"); if(group.ValueKind==JsonValueKind.Object) { if(JsonTools.Bool(group,"hidden")==true) continue; foreach(var member in JsonTools.Array(group,"components")) AddMember(member,JsonTools.String(group,"name")); }
                     else if(JsonTools.At(item,"component").ValueKind==JsonValueKind.Object) AddMember(JsonTools.At(item,"component"),null);
                 }
                 break;
             case "betterStack":
                 var included=JsonTools.Array(root,"included").ToList(); var sections=included.Where(x=>JsonTools.String(x,"type")=="status_page_section"&&JsonTools.String(x,"id") is not null).ToDictionary(x=>JsonTools.String(x,"id")!,x=>JsonTools.String(x,"attributes.name"));
                 foreach(var resource in included.Where(x=>JsonTools.String(x,"type")=="status_page_resource"))
                 {
                     var a=JsonTools.At(resource,"attributes"); var id=JsonTools.String(resource,"id");var name=JsonTools.String(a,"public_name"); if(id is null||name is null) continue;
                     var days=JsonTools.Array(a,"status_history").Select(x=>Day(x,"day","status")).Where(x=>x is not null).Cast<StatusDay>().ToList();
                     var section=Value(a,"status_page_section_id"); var availability=JsonTools.Number(a,"availability");
                     report.Components.Add(new(id,name,State(JsonTools.String(a,"status")),section is not null?sections.GetValueOrDefault(section):null,availability*100,days));
                 }
                 break;
             case "instatus":
                 var components=JsonTools.Array(root,"components").ToList(); var headers=components.Select(x=>JsonTools.String(x,"group.id")).Where(x=>x is not null).ToHashSet();
                 foreach(var c in components) if(!headers.Contains(JsonTools.String(c,"id"))) Add(c,"id","name","status",JsonTools.String(c,"group.name"));
                 break;
             case "upptime": foreach(var c in Elements(root)) Add(c,"slug","name","status"); break;
             case "gatus":
                 foreach(var c in Elements(root))
                 {
                     var result=JsonTools.Array(c,"results").LastOrDefault();var state=JsonTools.Bool(result,"success")==true?"operational":JsonTools.Bool(result,"success")==false?(JsonTools.Number(result,"status") is >=200 and <300?"degraded":"fullOutage"):"unrecognised";
                     if(JsonTools.String(c,"key") is {} id&&JsonTools.String(c,"name") is {} name) report.Components.Add(new(id,name,state,JsonTools.String(c,"group")));
                 } break;
             case "uptimeRobot":
                 foreach(var c in JsonTools.Array(root,"data"))
                 {
                     var id=Value(c,"monitorId");var name=JsonTools.String(c,"name");if(id is null||name is null) continue;
                     var days=new List<StatusDay>(); foreach(var d in JsonTools.Array(c,"dailyRatios")) if(JsonTools.Date(d,"date") is {} at) { var ratio=ParseNumber(d,"ratio"); var color=JsonTools.String(d,"color"); days.Add(new(at,color is "grey" or "black"||ratio is null?null:ratio>=100?"operational":ratio<=0?"fullOutage":"partialOutage")); }
                     report.Components.Add(new(id,WebUtility.HtmlDecode(name),State(JsonTools.String(c,"statusClass")),Uptime:ParseNumber(c,"90dRatio.ratio"),Days:days));
                 } break;
             case "openCode":
                 foreach(var c in JsonTools.Array(root,"components"))
                 {
                     var id=JsonTools.String(c,"service");if(id is null)continue;var names=new Dictionary<string,string>{{"console","Console API"},{"logs","Request logs"},{"inference","Inference API"},{"go","Go"},{"payg","Pay-as-you-go"},{"free","Free inference"}};
                     var buckets=JsonTools.Array(root,"history").Where(x=>JsonTools.Number(x,"start") is not null).GroupBy(x=>JsonTools.Number(x,"start")!.Value).ToDictionary(g=>g.Key,g=>g.First());var current=now.ToUnixTimeSeconds()/10800*10800; var days=new List<StatusDay>();
                     for(var i=55;i>=0;i--) { var start=current-i*10800; var word=buckets.TryGetValue(start*1000,out var bucket)?JsonTools.String(JsonTools.At(bucket,"services"),id):null; days.Add(new(DateTimeOffset.FromUnixTimeSeconds(start),word is null or "unknown"?null:State(word),3)); }
                     report.Components.Add(new(id,names.GetValueOrDefault(id,id),State(JsonTools.String(c,"status")),Days:days));
                 } break;
             case "deepInfra": foreach(var c in JsonTools.Array(root,"services")) Add(c,"publicId","displayName","status");break;
             case "ibmBob":
                 foreach(var c in JsonTools.Array(root,"regions").SelectMany(x=>JsonTools.Array(x,"services")))
                 {
                     var name=JsonTools.String(c,"name");if(name is null)continue;var state=State(JsonTools.String(c,"status")); var existing=report.Components.FindIndex(x=>x.Id==name);
                     if(existing<0) report.Components.Add(new(name,name,state)); else if(Severity(state)>Severity(report.Components[existing].State)) report.Components[existing]=report.Components[existing] with{State=state};
                 }break;
             case "googleCloud":
                 const string product="deUeOEPYanfJ9w8cpyBJ"; var impacts=new List<Impact>();
                 foreach(var incident in Elements(root)) if(JsonTools.Array(incident,"affected_products").Any(x=>JsonTools.String(x,"id")==product)&&JsonTools.Date(incident,"begin") is {} start)
                 {
                     var state=JsonTools.String(incident,"status_impact") switch{"SERVICE_OUTAGE"=>"fullOutage","SERVICE_DISRUPTION"=>"partialOutage","SERVICE_INFORMATION"=>"degraded",_=>"unrecognised"};var end=JsonTools.Date(incident,"end");impacts.Add(new(product,start,end,state));report.Incidents.Add(new(JsonTools.String(incident,"id")??start.ToString("O"),JsonTools.String(incident,"external_desc")??"Google Cloud incident",state,start,end));
                 }
                 report.Components.Add(new(product,"Gemini Code Assist",impacts.Where(x=>x.Start<=now&&(x.End is null||x.End>now)).OrderByDescending(x=>Severity(x.State)).FirstOrDefault()?.State??"operational"));
                 ApplyHistory(report,impacts,[],[],now,90);break;
             case "flashcat":
                 var hidden=JsonTools.Array(root,"page.sections").Where(x=>JsonTools.Bool(x,"hide_all")==true).Select(x=>JsonTools.String(x,"section_id")).ToHashSet();
                 foreach(var c in JsonTools.Array(root,"page.components").OrderBy(x=>JsonTools.Number(x,"order_id")??double.MaxValue))
                 {
                     if(JsonTools.Bool(c,"hide_all")==true||hidden.Contains(JsonTools.String(c,"section_id"))) continue;
                     var id=JsonTools.String(c,"component_id");var name=JsonTools.String(c,"name");if(id is null||name is null)continue;
                     var active=JsonTools.Array(root,"component_impacts").Where(x=>JsonTools.String(x,"component_id")==id&&JsonTools.Date(x,"start_at_seconds")<=now&&(JsonTools.Date(x,"end_at_seconds") is null||JsonTools.Date(x,"end_at_seconds")>now)).Select(x=>State(JsonTools.String(x,"status"))).OrderByDescending(Severity).FirstOrDefault();
                     report.Components.Add(new(id,name,JsonTools.String(c,"status") is {} stated?State(stated):active??"operational",JsonTools.String(c,"section_id")));
                 }
                 ApplyImpacts(report,root,now,flashcat:true);break;
         }
         report.Components=report.Components.Where(x=>Matches(page.Shows,x)).ToList();
         foreach(var i in JsonTools.Array(root,"incidents")) if(JsonTools.String(i,"id") is {} id&&JsonTools.String(i,"name") is {} name) report.Incidents.Add(new(id,name,JsonTools.String(i,"status")??"unrecognised",JsonTools.Date(i,"started_at")??JsonTools.Date(i,"created_at"),JsonTools.Date(i,"resolved_at")));
         if(report.Components.Count==0) report.Error="官方状态页未返回属于该服务的组件。";
         void Add(JsonElement c,string idKey,string nameKey,string stateKey,string? group=null) { var id=JsonTools.String(c,idKey);var name=JsonTools.String(c,nameKey);if(id is not null&&name is not null) report.Components.Add(new(id,name,State(JsonTools.String(c,stateKey)),group)); }
     }
     catch(JsonException) { report.Error="官方状态页答复格式无法读取。"; }
     return report;
 }
 internal static bool Matches(string? rule,StatusComponent c)
 {
     if(rule is null) return true; if(rule.StartsWith("except:")) return !Matches(rule[7..],c);
     var split=rule.IndexOf(':');if(split<0)return false;var values=rule[(split+1)..].Split(',');
     return rule[..split] switch {"ids"=>values.Contains(c.Id,StringComparer.Ordinal),"groups"=>values.Contains(c.Group??"",StringComparer.Ordinal),"name"=>values.Any(c.Name.Contains),_=>false};
 }
 internal static string State(string? value)=>Regex.Replace(value?.ToLowerInvariant()??"","[^a-z]","") switch
 {"operational" or "up" or "success"=>"operational","degradedperformance" or "degraded" or "recovereddegraded" or "warning"=>"degraded","partialoutage"=>"partialOutage","fulloutage" or "majoroutage" or "outage" or "downtime" or "down" or "danger"=>"fullOutage","undermaintenance" or "maintenance"=>"maintenance",_=>"unrecognised"};
 static int Severity(string state)=>state switch{"operational"=>0,"maintenance"=>1,"unrecognised"=>2,"degraded"=>3,"partialOutage"=>4,"fullOutage"=>5,_=>2};
 static IEnumerable<JsonElement> Elements(JsonElement e)=>e.ValueKind==JsonValueKind.Array?e.EnumerateArray():[];
 static string? Value(JsonElement e,string path) { var v=JsonTools.At(e,path); return v.ValueKind==JsonValueKind.String?v.GetString():v.ValueKind==JsonValueKind.Number?v.GetRawText():null; }
 static double? ParseNumber(JsonElement e,string path)=>double.TryParse(Value(e,path),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var value)&&double.IsFinite(value)?value:null;
 static StatusDay? Day(JsonElement e,string date,string status)=>JsonTools.Date(e,date) is {} at?new(at,JsonTools.String(e,status)=="not_monitored"?null:State(JsonTools.String(e,status))):null;
 sealed record Impact(string Component,DateTimeOffset Start,DateTimeOffset? End,string State);
 static void ApplyImpacts(StatusReport report,JsonElement root,DateTimeOffset now,int days=90,bool flashcat=false)
 {
     var impacts=new List<Impact>();var since=new Dictionary<string,DateTimeOffset>();var uptimes=new Dictionary<string,double>();
     foreach(var i in JsonTools.Array(root,"component_impacts")) if(JsonTools.String(i,"component_id") is {} id&&JsonTools.Date(i,flashcat?"start_at_seconds":"start_at") is {} start) impacts.Add(new(id,start,JsonTools.Date(i,flashcat?"end_at_seconds":"end_at"),State(JsonTools.String(i,"status"))));
     foreach(var u in JsonTools.Array(root,"component_uptimes")) if(JsonTools.String(u,"component_id") is {} id) { if(JsonTools.Date(u,flashcat?"available_since_seconds":"data_available_since") is {} at)since[id]=at;if(ParseNumber(u,"uptime") is {} up)uptimes[id]=up; }
     ApplyHistory(report,impacts,since,uptimes,now,days);
 }
 static void ApplyHistory(StatusReport report,List<Impact> impacts,Dictionary<string,DateTimeOffset> since,Dictionary<string,double> uptimes,DateTimeOffset now,int count)
 {
     report.Components=report.Components.Select(c=>
     {
         var history=new List<StatusDay>();
         for(var back=count-1;back>=0;back--) { var start=new DateTimeOffset(now.ToLocalTime().Date.AddDays(-back),TimeZoneInfo.Local.GetUtcOffset(now.ToLocalTime().Date.AddDays(-back)));var end=start.AddDays(1); var state=since.TryGetValue(c.Id,out var first)&&end<=first?null:impacts.Where(i=>i.Component==c.Id&&i.Start<end&&(i.End??now)>start).Select(i=>i.State).OrderByDescending(Severity).FirstOrDefault()??"operational";history.Add(new(start,state)); }
         return c with{Days=history,Uptime=uptimes.TryGetValue(c.Id,out var u)?u:null};
     }).ToList();
 }
 static void ApplyStatuspageHistory(StatusReport report,string json)
 {
     using var document=JsonDocument.Parse(json);var root=document.RootElement;var timelines=JsonTools.At(root,"timelines");
     report.Components=report.Components.Select(c=>
     {
         if(!timelines.TryGetProperty(c.Id,out var timeline))return c;var days=new List<StatusDay>();var since=JsonTools.String(timeline,"component.startDate");
         foreach(var d in JsonTools.Array(timeline,"days")) if(JsonTools.Date(d,"date") is {} at) { var date=JsonTools.String(d,"date");var state=since is not null&&string.CompareOrdinal(date,since)<0?null:JsonTools.Number(d,"outages.m")>0?"fullOutage":JsonTools.Number(d,"outages.p")>0?"partialOutage":"operational";days.Add(new(at,state)); }
         var uptime=JsonTools.Array(root,"values").Where(x=>JsonTools.String(x,"component")==c.Id).Select(x=>JsonTools.Number(x,"ninety")).FirstOrDefault();return c with{Days=days,Uptime=uptime};
     }).ToList();
 }
 static string? FlashcatData(string html)
 {
     if(html.TrimStart().StartsWith('{'))return html;
     var flight=new StringBuilder();foreach(Match match in Regex.Matches(html,"self\\.__next_f\\.push\\(\\[1,(\"(?:[^\"\\\\]|\\\\.)*\")\\]\\)")) { try{flight.Append(JsonSerializer.Deserialize<string>(match.Groups[1].Value));}catch(JsonException){} }
     var values=new Dictionary<string,JsonElement>();
     void Collect(JsonElement node) { if(node.ValueKind==JsonValueKind.Object){ if(node.TryGetProperty("initialData",out var initial)&&initial.ValueKind==JsonValueKind.Object)foreach(var p in initial.EnumerateObject())values.TryAdd(p.Name,p.Value.Clone());foreach(var p in node.EnumerateObject())Collect(p.Value);}else if(node.ValueKind==JsonValueKind.Array)foreach(var n in node.EnumerateArray())Collect(n); }
     foreach(var line in flight.ToString().Split('\n')) { var index=line.IndexOf(':');if(index<0||!line.Contains("\"initialData\""))continue;try{using var doc=JsonDocument.Parse(line[(index+1)..]);Collect(doc.RootElement);}catch(JsonException){} }
     return values.ContainsKey("page")?JsonSerializer.Serialize(values):null;
 }
}

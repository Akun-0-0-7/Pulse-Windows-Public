// Pi-family identity and Prime Agent reconciliation translated from Pulse 1.12.2, Apache-2.0.
using System.Text.Json;
namespace Pulse.Core;
internal static class SpendPiReaders
{
    sealed record Tally(long Input=0,long Output=0,long Read=0,long Write=0)
    {
        public long Total=>Input+Output+Read+Write;
        public static Tally operator +(Tally a,Tally b)=>new(a.Input+b.Input,a.Output+b.Output,a.Read+b.Read,a.Write+b.Write);
        public static Tally operator -(Tally a,Tally b)=>new(Math.Max(0,a.Input-b.Input),Math.Max(0,a.Output-b.Output),Math.Max(0,a.Read-b.Read),Math.Max(0,a.Write-b.Write));
    }
    sealed record Message(string? Id,string? Response,DateTimeOffset? At,string? Provider,string? Model,Tally Usage,long Unclassified);
    sealed record Attribution(string? Id,string? Target,Tally Child,Tally Aggregate);
    sealed class Transcript(string path)
    {
        public string Path=System.IO.Path.GetFullPath(path);public string? Id,Parent,Cwd;public int Depth;public List<Message> Messages=[];public List<Attribution> Attributions=[];public bool Valid;
    }
    static Tally Usage(JsonElement u)=>new(SpendReaders.N(u,"input"),SpendReaders.N(u,"output"),SpendReaders.N(u,"cacheRead"),SpendReaders.N(u,"cacheWrite"));
    static Transcript Parse(string file,List<string> notes,CancellationToken ct)
    {
        var transcript=new Transcript(file);try
        {
            foreach(var line in File.ReadLines(file))
            {
                ct.ThrowIfCancellationRequested();JsonDocument doc;try{doc=JsonDocument.Parse(line);}catch(JsonException){notes.Add("Pi 会话含无效或未完成行："+System.IO.Path.GetFileName(file));continue;}
                using(doc)
                {
                    var row=doc.RootElement;var type=SpendReaders.S(row,"type");
                    if(!transcript.Valid)
                    {
                        if(type=="title")continue;
                        if(type!="session"||SpendReaders.S(row,"id") is not {} session)return transcript;
                        transcript.Valid=true;transcript.Id=session;transcript.Cwd=SpendReaders.S(row,"cwd");transcript.Depth=(int)Math.Min(SpendReaders.N(row,"rlmDepth"),int.MaxValue);var parent=SpendReaders.S(row,"parentSession");if(parent is not null)transcript.Parent=System.IO.Path.GetFullPath(parent);continue;
                    }
                    if(type=="message")
                    {
                        var m=JsonTools.At(row,"message");var u=JsonTools.At(m,"usage");if(SpendReaders.S(m,"role")!="assistant"||u.ValueKind!=JsonValueKind.Object)continue;var usage=Usage(u);var unknown=Math.Max(0,SpendReaders.N(u,"totalTokens")-usage.Total);if(usage.Total+unknown<=0)continue;
                        // responseId is standard; some older Pi recordings wrote the same stable response in message.id.
                        transcript.Messages.Add(new(SpendReaders.S(row,"id"),SpendReaders.S(m,"responseId","id"),SpendReaders.D(row,"timestamp")??SpendReaders.D(m,"timestamp"),SpendReaders.S(m,"provider"),SpendReaders.S(m,"model"),usage,unknown));
                    }
                    else if(type=="child_usage_attributed")transcript.Attributions.Add(new(SpendReaders.S(row,"id"),SpendReaders.S(row,"targetId"),Usage(JsonTools.At(row,"childUsage")),Usage(JsonTools.At(row,"aggregateUsage"))));
                }
            }
        }catch(Exception e)when(e is IOException or UnauthorizedAccessException or ArgumentException){notes.Add("Pi 会话读取失败："+System.IO.Path.GetFileName(file));}
        return transcript;
    }
    internal static List<SpendRecord> Read(string agent,IEnumerable<string> roots,List<string> notes,CancellationToken ct)
    {
        var files=SpendReaders.Files(roots,notes).Where(f=>System.IO.Path.GetExtension(f).Equals(".jsonl",StringComparison.OrdinalIgnoreCase)).Select(f=>Parse(f,notes,ct)).ToList();
        if(agent=="senpi")
        {
            var known=files.Select(f=>f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);var childRoots=files.Where(f=>f.Cwd is not null).Select(f=>System.IO.Path.Combine(f.Cwd!,".omo","senpi-task","children"));
            foreach(var child in SpendReaders.Files(childRoots,notes).Where(f=>System.IO.Path.GetExtension(f)==".jsonl"))if(known.Add(System.IO.Path.GetFullPath(child)))files.Add(Parse(child,notes,ct));
        }
        var reductions=new Dictionary<string,Tally>();
        string Identity(Message m)
        {
            if(m.Response is {} response)return agent+":response:"+response;
            return agent+":message:"+m.Id+":"+(m.At?.ToUnixTimeMilliseconds()??0)+":"+(m.Provider??(agent=="omp"?"omp":""))+":"+m.Model+":"+m.Usage.Input+":"+m.Usage.Output+":"+m.Usage.Read+":"+m.Usage.Write;
        }
        if(agent=="prime-agent")
        {
            var parentOf=files.Where(f=>f.Parent is not null).ToDictionary(f=>f.Path,f=>f.Parent!,StringComparer.OrdinalIgnoreCase);
            string Root(string start)
            {
                var chain=new List<string>();var current=start;while(true){var index=chain.FindIndex(p=>p.Equals(current,StringComparison.OrdinalIgnoreCase));if(index>=0)return chain.Skip(index).Order(StringComparer.OrdinalIgnoreCase).First();chain.Add(current);if(!parentOf.TryGetValue(current,out var parent))return current;current=parent;}
            }
            bool Descendant(string child,string ancestor){var visited=new HashSet<string>(StringComparer.OrdinalIgnoreCase);while(visited.Add(child)){if(child.Equals(ancestor,StringComparison.OrdinalIgnoreCase))return true;if(!parentOf.TryGetValue(child,out var parent))return false;child=parent;}return false;}
            var children=files.Where(f=>f.Depth>0).Select(f=>(f.Path,Total:f.Messages.Aggregate(new Tally(),(total,m)=>total+m.Usage))).ToList();var consumed=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var file in files)foreach(var attribution in file.Attributions)
            {
                ct.ThrowIfCancellationRequested();if(attribution.Id is null||attribution.Target is null||!seen.Add(Root(file.Path)+"#"+attribution.Id)||attribution.Aggregate.Total<=0||attribution.Child.Total<=0)continue;
                var message=file.Messages.FirstOrDefault(m=>m.Id==attribution.Target&&m.Usage==attribution.Aggregate);if(message is null)continue;
                var child=children.FirstOrDefault(c=>!consumed.Contains(c.Path)&&c.Total==attribution.Child&&Descendant(c.Path,Root(file.Path)));if(child.Path is null)continue;consumed.Add(child.Path);var key=Identity(message);reductions[key]=reductions.GetValueOrDefault(key,new())+attribution.Child;
            }
        }
        bool incomplete=false;var records=new List<SpendRecord>();
        foreach(var file in files)
        {
            for(var ordinal=0;ordinal<file.Messages.Count;ordinal++)
            {
                var message=file.Messages[ordinal];if(!file.Valid||file.Id is null||message.At is null||message.Model is null){incomplete=true;continue;}
                var identity=agent=="kimchi"?"kimchi:"+file.Id+":"+(message.Id??file.Path+":"+ordinal):Identity(message);var tally=message.Usage-reductions.GetValueOrDefault(identity,new());if(tally.Total+message.Unclassified<=0)continue;
                records.Add(new(agent,identity,message.At.Value,message.Model,tally.Input,tally.Output,tally.Read,tally.Write,Unclassified:message.Unclassified,Session:file.Id,Project:file.Cwd));
            }
        }
        return incomplete?records.Select(r=>r with{IsPartial=true}).ToList():records;
    }
}

// Zed and Antigravity schemas translated from Pulse 1.12.2, Apache-2.0.
using System.Globalization;
using System.Text;
using System.Text.Json;
namespace Pulse.Core;
internal static class SpendBinaryReaders
{
    const int MaxPayload=32*1024*1024;
    static HashSet<string> Tables(string path)=>SqliteReader.Query(path,"SELECT name FROM sqlite_master WHERE type='table'").Select(r=>r.GetValueOrDefault("name")??"").ToHashSet();
    static HashSet<string> Columns(string path,string table)=>SqliteReader.Query(path,"PRAGMA table_info(\""+table+"\")").Select(r=>r.GetValueOrDefault("name")??"").ToHashSet();
    static List<Dictionary<string,string?>> Rows(string path,string table,HashSet<string> tables)=>tables.Contains(table)?SqliteReader.Query(path,"SELECT * FROM \""+table+"\""):[];
    static string? V(Dictionary<string,string?> row,string name)=>row.GetValueOrDefault(name);
    static string Hex(string column)=>"CASE WHEN length(\""+column+"\") <= "+MaxPayload+" THEN hex(\""+column+"\") ELSE NULL END AS payload_hex";
    static byte[]? Blob(Dictionary<string,string?> row)=>V(row,"payload_hex") is {} hex&&hex.Length<=MaxPayload*2?Convert.FromHexString(hex):null;
    static DateTimeOffset? Date(string? text)
    {
        if(text is null)return null;
        if(double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out var number))return number>0&&number<=253402300799000?(number>100000000000?DateTimeOffset.FromUnixTimeMilliseconds((long)number):DateTimeOffset.FromUnixTimeSeconds((long)number)):null;
        return DateTimeOffset.TryParse(text,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var at)?at:null;
    }
    internal static List<SpendRecord> ReadZed(string path,List<string> notes,CancellationToken ct)
    {
        if(!Tables(path).Contains("threads"))return [];
        var columns=Columns(path,"threads");if(!columns.Contains("data"))return [];
        var names=new[]{"id","updated_at","created_at","data_type","folder_paths","folder_paths_order"};
        var sql="SELECT "+string.Join(',',names.Select(n=>columns.Contains(n)?"\""+n+"\"":"NULL AS \""+n+"\""))+","+Hex("data")+" FROM threads";
        var result=new List<SpendRecord>();
        foreach(var row in SqliteReader.Query(path,sql))
        {
            ct.ThrowIfCancellationRequested();if(V(row,"id") is not {} id)continue;
            var raw=Blob(row);var compressed=V(row,"data_type")=="zstd"||raw is not null&&SpendCompression.IsZstd(raw);
            if(raw is null){notes.Add("zed：线程数据超过 32 MiB 或无法读取。");continue;}
            if(!compressed&&V(row,"data_type")!="json")continue;
            try
            {
                if(V(row,"data_type")=="zstd"&&!SpendCompression.IsZstd(raw))throw new IOException("zstd 标记与内容不匹配。");
                using var doc=JsonDocument.Parse(compressed?SpendCompression.Decode(raw,MaxPayload):raw);var root=doc.RootElement;
                if(JsonTools.Bool(root,"imported")==true||SpendReaders.S(root,"model.provider")!="zed.dev"||SpendReaders.S(root,"model.model") is not {} model)continue;
                var usage=JsonTools.At(root,"request_token_usage");var entries=usage.ValueKind==JsonValueKind.Array?usage.EnumerateArray().ToList():usage.ValueKind==JsonValueKind.Object?usage.EnumerateObject().Select(p=>p.Value).ToList():[];
                (long Input,long Output,long Read,long Write) Tally(JsonElement u)=>(SpendReaders.N(u,"input_tokens"),SpendReaders.N(u,"output_tokens"),SpendReaders.N(u,"cache_read_input_tokens"),SpendReaders.N(u,"cache_creation_input_tokens"));
                var counters=entries.Select(Tally).ToList();var input=counters.Sum(t=>t.Input);var output=counters.Sum(t=>t.Output);var read=counters.Sum(t=>t.Read);var write=counters.Sum(t=>t.Write);
                if(input+output+read+write==0){var total=Tally(JsonTools.At(root,"cumulative_token_usage"));(input,output,read,write)=total;}
                var at=Date(V(row,"created_at"))??Date(V(row,"updated_at"))??SpendReaders.D(root,"updated_at");if(at is null||input+output+read+write==0)continue;
                var paths=(V(row,"folder_paths")??"").Split('\n',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);var index=int.TryParse(V(row,"folder_paths_order")?.Split(',')[0],out var chosen)&&chosen>=0&&chosen<paths.Length?chosen:0;
                result.Add(new("zed","zed:"+id,at.Value,model,input,output,read,write,IsAggregate:true,Session:id,Project:paths.Length>0?paths[index]:null));
            }catch(Exception e)when(e is IOException or JsonException){notes.Add("zed："+(compressed?"zstd 线程解码失败":"线程 JSON 无法读取")+"："+id);}
        }
        return result;
    }
    internal static List<SpendRecord> ReadAntigravity(string agent,string path,List<string> notes,CancellationToken ct)
    {
        var tables=Tables(path);if(!tables.Contains("gen_metadata"))return [];
        WireMessage? Parse(Dictionary<string,string?> row)=>Blob(row) is {} bytes?WireMessage.Decode(bytes):null;
        string? project=null;DateTimeOffset? anchor=null;
        if(tables.Contains("trajectory_metadata_blob"))foreach(var row in SqliteReader.Query(path,"SELECT "+Hex("data")+" FROM trajectory_metadata_blob LIMIT 1")){var m=Parse(row);anchor=Timestamp(m?.Nested(2));project=m?.Nested(1)?.Text(1);if(Uri.TryCreate(project,UriKind.Absolute,out var uri)&&uri.IsFile)project=uri.LocalPath;}
        var byResponse=new Dictionary<string,DateTimeOffset>();var byIndex=new Dictionary<ulong,DateTimeOffset>();
        if(tables.Contains("steps"))foreach(var row in SqliteReader.Query(path,"SELECT "+Hex("metadata")+" FROM steps WHERE step_type=15")){ct.ThrowIfCancellationRequested();var m=Parse(row);if(Timestamp(m?.Nested(1)) is not {} at)continue;if(m?.Nested(9)?.Text(11) is {} response)byResponse[response]=at;if(m?.Nested(20)?.Number(3) is {} index)byIndex[index]=at;}
        var generations=new List<(ulong Index,WireMessage Chat)>();
        foreach(var row in SqliteReader.Query(path,"SELECT idx,"+Hex("data")+" FROM gen_metadata ORDER BY idx")){ct.ThrowIfCancellationRequested();if(ulong.TryParse(V(row,"idx"),out var index)&&Parse(row)?.Nested(1) is {} chat)generations.Add((index,chat));else notes.Add(agent+"：跳过无法解码的 protobuf 元数据。");}
        static bool Routing(string? model)=>model is "gemini-default" or "gemini-pro-default";
        var models=generations.Select(g=>g.Chat.Text(19)).Where(m=>m is not null&&!Routing(m)).Distinct().ToList();
        var labels=generations.Where(g=>g.Chat.Text(21) is not null&&g.Chat.Text(19) is not null&&!Routing(g.Chat.Text(19))).GroupBy(g=>g.Chat.Text(21)!).ToDictionary(g=>g.Key,g=>g.Select(x=>x.Chat.Text(19)!).Distinct().ToList());
        var verifiedLabels=new Dictionary<string,string>{{"Gemini 3.5 Flash (Low)","gemini-3.5-flash-extra-low"},{"Gemini 3.5 Flash (Medium)","gemini-3.5-flash-medium"},{"Gemini 3.5 Flash (High)","gemini-3.5-flash-high"},{"Gemini 3.1 Pro (High)","gemini-3.1-pro-preview-high"}};
        var result=new List<SpendRecord>();var seen=new HashSet<string>();var session=Path.GetFileNameWithoutExtension(path);
        foreach(var generation in generations)
        {
            ct.ThrowIfCancellationRequested();var chat=generation.Chat;var u=chat.Nested(4);if(u is null)continue;long Count(int n)=>(long)Math.Min(u.Number(n)??0,(ulong)long.MaxValue/8);var input=Count(2);var read=Count(5);var output=Count(9)+Count(10);if(input+read+output==0)continue;
            var response=u.Text(11);if(response is not null&&!seen.Add(response))continue;
            var eventAt=Timestamp(chat.Nested(9)?.Nested(4))??(response is not null&&byResponse.TryGetValue(response,out var responseAt)?(DateTimeOffset?)responseAt:null)??(byIndex.TryGetValue(generation.Index,out var stepAt)?(DateTimeOffset?)stepAt:null);var at=eventAt??anchor;if(at is null)continue;
            var model=chat.Text(19);var label=chat.Text(21);if(model is null||Routing(model))model=label is not null&&labels.TryGetValue(label,out var identified)&&identified.Count==1?identified[0]:label is not null&&verifiedLabels.TryGetValue(label,out var mapped)?mapped:models.Count==1?models[0]:model??"unknown";
            result.Add(new(agent,"agy:"+(response??session+":"+generation.Index),at.Value,model!,input,output,read,IsPartial:true,IsAggregate:eventAt is null,Session:session,Project:project));
        }
        return result;
    }
    static DateTimeOffset? Timestamp(WireMessage? message)
    {
        var seconds=message?.Number(1);var nanos=message?.Number(2)??0;if(seconds is null or 0||seconds>253402300799||nanos>=1_000_000_000)return null;
        return DateTimeOffset.FromUnixTimeSeconds((long)seconds.Value).AddTicks((long)(nanos/100));
    }
    sealed record WireValue(ulong? Number=null,byte[]? Bytes=null);
    sealed class WireMessage
    {
        readonly Dictionary<int,WireValue> fields=[];
        public ulong? Number(int n)=>fields.GetValueOrDefault(n)?.Number;
        public WireMessage? Nested(int n)=>fields.GetValueOrDefault(n)?.Bytes is {} bytes?Decode(bytes):null;
        public string? Text(int n){if(fields.GetValueOrDefault(n)?.Bytes is not {} bytes)return null;try{var text=new UTF8Encoding(false,true).GetString(bytes).Trim();return text.Length>0?text:null;}catch(DecoderFallbackException){return null;}}
        public static WireMessage? Decode(byte[] bytes)
        {
            if(bytes.Length>MaxPayload)return null;var i=0;var m=new WireMessage();
            ulong? Varint(){ulong value=0;for(var j=0;j<10;j++){if(i>=bytes.Length)return null;var b=bytes[i++];if(j==9&&b>1)return null;value|=(ulong)(b&127)<<(7*j);if((b&128)==0)return value;}return null;}
            while(i<bytes.Length)
            {
                var tag=Varint();if(tag is null||tag.Value>>3 is 0 or >536870911)return null;var field=(int)(tag.Value>>3);WireValue value;
                switch(tag.Value&7)
                {
                    case 0:var number=Varint();if(number is null)return null;value=new(number);break;
                    case 1:if(bytes.Length-i<8)return null;value=new();i+=8;break;
                    case 2:var length=Varint();if(length is null||length>(ulong)(bytes.Length-i))return null;value=new(Bytes:bytes.AsSpan(i,(int)length).ToArray());i+=(int)length;break;
                    case 5:if(bytes.Length-i<4)return null;value=new();i+=4;break;
                    default:return null;
                }
                m.fields.TryAdd(field,value);
            }
            return m;
        }
    }
}

using System.Text;
namespace Pulse.Core;
internal static class SpendCompression
{
    internal const int MaximumBytes=64*1024*1024;
    internal static bool IsZstd(ReadOnlySpan<byte> bytes)=>bytes.Length>=4&&bytes[..4].SequenceEqual(new byte[]{0x28,0xB5,0x2F,0xFD});
    internal static byte[] Decode(byte[] raw,int limit=MaximumBytes,bool allowTruncated=false)
    {
        if(raw.Length>limit)throw new IOException("zstd 输入超过大小上限。");
        if(!IsZstd(raw))return raw;
        using var input=new MemoryStream(raw,false);
        using var decoded=new ZstdSharp.DecompressionStream(input,checkEndOfStream:!allowTruncated);
        using var output=new MemoryStream();var buffer=new byte[16384];int count;
        try{while((count=decoded.Read(buffer,0,buffer.Length))>0){if(output.Length+count>limit)throw new IOException("zstd 解压结果超过大小上限。");output.Write(buffer,0,count);}}
        catch(ZstdSharp.ZstdException exception){throw new IOException("zstd 数据损坏。",exception);}
        return output.ToArray();
    }
    internal static string ReadTranscript(string path)
    {
        if(new FileInfo(path).Length>MaximumBytes)throw new IOException("zstd 日志超过 64 MiB。");
        return Encoding.UTF8.GetString(Decode(File.ReadAllBytes(path),allowTruncated:true));
    }
}

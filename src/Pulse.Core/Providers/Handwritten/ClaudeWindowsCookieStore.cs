// Copyright (c) 2026 qunqin24. Licensed under the Apache License, Version 2.0.
// Reads only Claude's three useful Electron cookies, in memory, as the current Windows user.
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Pulse.Core;

public static class ClaudeWindowsCookieStore
{
    [StructLayout(LayoutKind.Sequential)] struct DataBlob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll",SetLastError=true)] static extern bool CryptUnprotectData(ref DataBlob input,IntPtr description,IntPtr entropy,IntPtr reserved,IntPtr prompt,int flags,out DataBlob output);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr pointer);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string path,out IntPtr db,int flags,IntPtr vfs);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_prepare_v2(IntPtr db,[MarshalAs(UnmanagedType.LPUTF8Str)] string sql,int size,out IntPtr statement,IntPtr tail);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_step(IntPtr statement);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern IntPtr sqlite3_column_text(IntPtr statement,int column);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern IntPtr sqlite3_column_blob(IntPtr statement,int column);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_column_bytes(IntPtr statement,int column);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_finalize(IntPtr statement);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_close(IntPtr db);

    static byte[]? Unprotect(byte[] encrypted) {
        var pointer=Marshal.AllocHGlobal(encrypted.Length);try {Marshal.Copy(encrypted,0,pointer,encrypted.Length);var input=new DataBlob{Length=encrypted.Length,Data=pointer};
            if(!CryptUnprotectData(ref input,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out var output))return null;
            try {var bytes=new byte[output.Length];Marshal.Copy(output.Data,bytes,0,bytes.Length);return bytes;}finally {LocalFree(output.Data);}
        }finally {Marshal.FreeHGlobal(pointer);}
    }
    static byte[]? MasterKey(string profile) {
        var localState=Path.Combine(profile,"Local State");if(!File.Exists(localState))return null;
        try {using var file=File.OpenRead(localState);using var doc=JsonDocument.Parse(file);var encoded=JsonTools.String(doc.RootElement,"os_crypt.encrypted_key");if(encoded==null)return null;
            var blob=Convert.FromBase64String(encoded);return blob.Length>5&&Encoding.ASCII.GetString(blob,0,5)=="DPAPI"?Unprotect(blob[5..]):null;
        }catch(Exception ex)when(ex is IOException or JsonException or FormatException){return null;}
    }
    public static string? DecryptChromiumCookie(byte[] encrypted,byte[]? key,string host) {
        byte[]? clear=null;
        if(encrypted.Length>3&&(encrypted.AsSpan(0,3).SequenceEqual("v10"u8)||encrypted.AsSpan(0,3).SequenceEqual("v11"u8))) {
            if(key?.Length!=32||encrypted.Length<3+12+16)return null;
            var cipherLength=encrypted.Length-3-12-16;clear=new byte[cipherLength];
            try {using var aes=new AesGcm(key,16);aes.Decrypt(encrypted.AsSpan(3,12),encrypted.AsSpan(15,cipherLength),encrypted.AsSpan(15+cipherLength,16),clear);}catch(CryptographicException){return null;}
        }else if(encrypted.Length>0&&!encrypted.AsSpan(0,Math.Min(3,encrypted.Length)).SequenceEqual("v20"u8))clear=OperatingSystem.IsWindows()?Unprotect(encrypted):null;
        if(clear==null)return null;
        var hash=SHA256.HashData(Encoding.UTF8.GetBytes(host));if(clear.AsSpan().StartsWith(hash))clear=clear[32..];
        var value=Encoding.UTF8.GetString(clear);return value.All(ch=>ch is >= ' ' and <= '~')?value:null;
    }
    static byte[] Blob(IntPtr statement,int column) {var count=sqlite3_column_bytes(statement,column);if(count is <=0 or >32768)return [];var value=new byte[count];Marshal.Copy(sqlite3_column_blob(statement,column),value,0,count);return value;}
    static IEnumerable<(string Host,string Name,string Value,byte[] Encrypted)> Rows(string path) {
        IntPtr db=IntPtr.Zero,statement=IntPtr.Zero;
        try {if(sqlite3_open_v2(path,out db,1,IntPtr.Zero)!=0)return [];
            const string sql="SELECT host_key,name,value,encrypted_value FROM cookies WHERE name IN ('sessionKey','sessionKeyV3','lastActiveOrg') AND (host_key='claude.ai' OR host_key='.claude.ai' OR host_key LIKE '%.claude.ai') LIMIT 24";
            if(sqlite3_prepare_v2(db,sql,-1,out statement,IntPtr.Zero)!=0)return [];
            var list=new List<(string,string,string,byte[])>();while(sqlite3_step(statement)==100){var host=Marshal.PtrToStringUTF8(sqlite3_column_text(statement,0))??"";var name=Marshal.PtrToStringUTF8(sqlite3_column_text(statement,1))??"";var value=Marshal.PtrToStringUTF8(sqlite3_column_text(statement,2))??"";list.Add((host,name,value,Blob(statement,3)));}return list;
        }finally {if(statement!=IntPtr.Zero)sqlite3_finalize(statement);if(db!=IntPtr.Zero)sqlite3_close(db);}
    }
    public static string? ReadSession(string? profile=null) {
        if(!OperatingSystem.IsWindows())return null;
        profile??=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Claude");
        var database=new[]{Path.Combine(profile,"Network","Cookies"),Path.Combine(profile,"Cookies")}.FirstOrDefault(File.Exists);if(database==null)return null;
        var key=MasterKey(profile);var values=new Dictionary<string,string>(StringComparer.Ordinal);
        try {foreach(var row in Rows(database)) {
            if(row.Host!="claude.ai"&&row.Host!=".claude.ai"&&!row.Host.EndsWith(".claude.ai",StringComparison.OrdinalIgnoreCase))continue;
            var value=row.Value.Length>0?row.Value:DecryptChromiumCookie(row.Encrypted,key,row.Host);
            if(value!=null&&value.Length>0&&value.Length<16384)values.TryAdd(row.Name,value);
        }}catch(IOException){return null;}
        return values.Count==0?null:string.Join("; ",new[]{"sessionKey","sessionKeyV3","lastActiveOrg"}.Where(values.ContainsKey).Select(name=>name+"="+values[name]));
    }
}

using System.Runtime.InteropServices;
namespace Pulse.Core;
public static class SqliteReader
{
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] private static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string path,out IntPtr db,int flags,IntPtr vfs);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] private static extern int sqlite3_prepare_v2(IntPtr db,[MarshalAs(UnmanagedType.LPUTF8Str)] string sql,int bytes,out IntPtr statement,IntPtr tail);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] private static extern int sqlite3_step(IntPtr statement);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] private static extern int sqlite3_column_count(IntPtr statement);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] private static extern IntPtr sqlite3_column_name(IntPtr statement,int col);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] private static extern IntPtr sqlite3_column_text(IntPtr statement,int col);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] private static extern int sqlite3_column_type(IntPtr statement,int col);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] private static extern int sqlite3_finalize(IntPtr statement);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] private static extern int sqlite3_close(IntPtr db);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] private static extern int sqlite3_busy_timeout(IntPtr db,int milliseconds);
    public static List<Dictionary<string,string?>> Query(string path,string sql)
    {
        if(!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows SQLite required");
        if(!File.Exists(path)) return [];
        IntPtr db=IntPtr.Zero,statement=IntPtr.Zero;
        try {
            if(sqlite3_open_v2(path,out db,1,IntPtr.Zero)!=0) throw new IOException("数据库暂时无法读取");
            sqlite3_busy_timeout(db,1500);
            if(sqlite3_prepare_v2(db,sql,-1,out statement,IntPtr.Zero)!=0) throw new IOException("数据库字段与预期不一致");
            var result=new List<Dictionary<string,string?>>(); int code;
            while((code=sqlite3_step(statement))==100) {
                var row=new Dictionary<string,string?>();
                for(int i=0;i<sqlite3_column_count(statement);i++) row[Marshal.PtrToStringUTF8(sqlite3_column_name(statement,i))??$"column{i}"]=sqlite3_column_type(statement,i)==5?null:Marshal.PtrToStringUTF8(sqlite3_column_text(statement,i));
                result.Add(row); if(result.Count>1_000_000) throw new IOException("数据库查询超过记录限制");
            }
            if(code!=101) throw new IOException("数据库读取未完成"); return result;
        } finally { if(statement!=IntPtr.Zero) sqlite3_finalize(statement); if(db!=IntPtr.Zero) sqlite3_close(db); }
    }
}

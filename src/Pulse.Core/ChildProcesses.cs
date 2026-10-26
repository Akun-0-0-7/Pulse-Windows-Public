using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace Pulse.Core;
public static class ChildProcesses
{
    private static readonly ConcurrentDictionary<int,Process> running=new();
    private static readonly SafeFileHandle? job=CreateJob();
    [StructLayout(LayoutKind.Sequential)] private struct BasicLimit {public long ProcessTime,JobTime;public uint Flags;public UIntPtr MinWorking,MaxWorking;public uint Active;public UIntPtr Affinity;public uint Priority,Scheduling;}
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters {public ulong ReadOps,WriteOps,OtherOps,ReadBytes,WriteBytes,OtherBytes;}
    [StructLayout(LayoutKind.Sequential)] private struct Limits {public BasicLimit Basic;public IoCounters Io;public UIntPtr ProcessMemory,JobMemory,PeakProcess,PeakJob;}
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] private static extern SafeFileHandle CreateJobObject(IntPtr attributes,string? name);
    [DllImport("kernel32.dll")] private static extern bool SetInformationJobObject(SafeFileHandle job,int kind,ref Limits limits,uint size);
    [DllImport("kernel32.dll")] private static extern bool AssignProcessToJobObject(SafeFileHandle job,IntPtr process);
    private static SafeFileHandle? CreateJob(){if(!OperatingSystem.IsWindows())return null;var handle=CreateJobObject(IntPtr.Zero,null);if(handle.IsInvalid)return null;var limits=new Limits{Basic=new(){Flags=0x2000}};if(!SetInformationJobObject(handle,9,ref limits,(uint)Marshal.SizeOf<Limits>())){handle.Dispose();return null;}return handle;}
    public static IDisposable Track(Process process){running[process.Id]=process;if(job is not null&&!job.IsInvalid)try{AssignProcessToJobObject(job,process.Handle);}catch(InvalidOperationException){}return new Lease(process.Id);}
    public static void KillAll(){foreach(var process in running.Values)try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}}
    public static int Count=>running.Count;
    private sealed class Lease(int id):IDisposable {public void Dispose()=>running.TryRemove(id,out _);}
}

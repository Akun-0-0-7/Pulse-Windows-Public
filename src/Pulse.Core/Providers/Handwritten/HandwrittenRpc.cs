// Copyright (c) 2026 qunqin24. Licensed under the Apache License, Version 2.0.
// The Windows transport uses bounded async pipes and ends its child after each quota read.
using System.Diagnostics;
using System.Text;
using System.Text.Json;
namespace Pulse.Core;

public static class HandwrittenRpc
{
    public static Task<string> ReadAsync(string executable,IEnumerable<string> arguments,bool kiro,CancellationToken cancellationToken=default,TimeSpan? timeout=null,Dictionary<string,string>? environment=null)=>InvokeAsync(executable,arguments,kiro?"_kiro/account/getUsage":"account/rateLimits/read",kiro,cancellationToken,timeout,environment);
    public static async Task<string> InvokeAsync(string executable,IEnumerable<string> arguments,string method,bool kiro=false,CancellationToken cancellationToken=default,TimeSpan? timeout=null,Dictionary<string,string>? environment=null,object? parameters=null,string? workingDirectory=null)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);deadline.CancelAfter(timeout??TimeSpan.FromSeconds(20));var ct=deadline.Token;
        using var child=new Process { StartInfo=WindowsCommand.StartInfo(executable,arguments) };
        if(workingDirectory is not null)child.StartInfo.WorkingDirectory=workingDirectory;
        if(environment is not null){child.StartInfo.Environment.Clear();foreach(var pair in environment)child.StartInfo.Environment[pair.Key]=pair.Value;}
        if(environment is null)child.StartInfo.Environment["PATH"]=Path.GetDirectoryName(executable)+Path.PathSeparator+Environment.GetEnvironmentVariable("PATH");
        if(!child.Start())throw new InvalidOperationException("Could not start quota helper");
        using var lease=ChildProcesses.Track(child);
        var budget=new OutputBudget(deadline);
        var errors=Drain(child.StandardError,budget,ct);
        var reader=new BoundedLines(child.StandardOutput,budget);
        try {
            await Send(1,"initialize",kiro?new {protocolVersion=1,clientCapabilities=new{},clientInfo=new{name="PulseWindows",version="0.1"}}:(object)new{clientInfo=new{name="PulseWindows",title="Pulse",version="0.1"}});
            _=await Reply(1);
            if(!kiro)await child.StandardInput.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"method\":\"initialized\"}".AsMemory(),ct);
            await Send(2,method,parameters??new{});
            return await Reply(2);
        } finally {
            deadline.Cancel();try {if(!child.HasExited)child.Kill(true);}catch(InvalidOperationException){}
            try {await child.WaitForExitAsync(CancellationToken.None);}catch(InvalidOperationException){}
            try {await errors;}catch(OperationCanceledException){}
        }
        async Task Send(int id,string method,object parameters) {
            await child.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new{jsonrpc="2.0",id,method,@params=parameters}).AsMemory(),ct);await child.StandardInput.FlushAsync(ct);
        }
        async Task<string> Reply(int requested) {
            while(true) {
                var line=await reader.ReadAsync(ct);if(line==null)throw new InvalidOperationException("Quota helper closed its output");
                JsonDocument doc;try {doc=JsonDocument.Parse(line);}catch(JsonException){continue;}using(doc) {
                    var root=doc.RootElement;if(JsonTools.Number(root,"id")!=requested)continue;
                    if(root.TryGetProperty("error",out _))throw new InvalidOperationException("Quota helper refused the read");
                    return root.TryGetProperty("result",out var result)?result.GetRawText():"{}";
                }
            }
        }
    }
    static async Task Drain(StreamReader stream,OutputBudget budget,CancellationToken ct) {var buffer=new char[2048];int read;while((read=await stream.ReadAsync(buffer,ct))>0)budget.Add(buffer,read); }
    sealed class OutputBudget(CancellationTokenSource deadline) {
        int bytes;
        public void Add(char[] data,int count) {if(Interlocked.Add(ref bytes,Encoding.UTF8.GetByteCount(data,0,count))>524288){deadline.Cancel();throw new IOException("Quota helper exceeded output ceiling");}}
    }
    sealed class BoundedLines(StreamReader stream,OutputBudget budget)
    {
        readonly char[] buffer=new char[4096];readonly StringBuilder pending=new();
        public async Task<string?> ReadAsync(CancellationToken ct) {
            while(true) {
                for(var i=0;i<pending.Length;i++)if(pending[i]=='\n'){var line=pending.ToString(0,i).TrimEnd('\r');pending.Remove(0,i+1);return line;}
                var count=await stream.ReadAsync(buffer,ct);if(count==0)return pending.Length==0?null:TakeRemainder();
                budget.Add(buffer,count);pending.Append(buffer,0,count);
            }
        }
        string TakeRemainder(){var text=pending.ToString();pending.Clear();return text;}
    }
}

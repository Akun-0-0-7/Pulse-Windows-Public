using System.IO.Pipes;
namespace Pulse.Desktop;
public static class InstanceChannel
{
    public static string InstanceKey=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Environment.UserName+"|"+Path.GetFullPath(PulseApp.DataRoot).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant())))[..20];
    private static string Name=>"PulseWindows-"+InstanceKey;
    public static async Task SendAsync(string message)
    {
        try{using var pipe=new NamedPipeClientStream(".",Name,PipeDirection.Out,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);await pipe.ConnectAsync(2000).ConfigureAwait(false);using var writer=new StreamWriter(pipe){AutoFlush=true};await writer.WriteLineAsync(message.Length<=4096?message:"settings").ConfigureAwait(false);}catch(IOException){}catch(TimeoutException){}
    }
    public static async Task ListenAsync(Action<string> received,CancellationToken cancellationToken)
    {
        while(!cancellationToken.IsCancellationRequested) {
            try{await using var pipe=new NamedPipeServerStream(Name,PipeDirection.In,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);await pipe.WaitForConnectionAsync(cancellationToken);using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);deadline.CancelAfter(TimeSpan.FromSeconds(3));using var reader=new StreamReader(pipe);var builder=new System.Text.StringBuilder();var character=new char[1];while(builder.Length<=4096&&await reader.ReadAsync(character.AsMemory(),deadline.Token)>0){if(character[0]=='\n')break;builder.Append(character[0]);}if(builder.Length<=4096)received(builder.ToString().TrimEnd('\r'));}
            catch(OperationCanceledException)when(cancellationToken.IsCancellationRequested){break;}catch(OperationCanceledException){}catch(IOException){await Task.Delay(200,cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);}
        }
    }
}

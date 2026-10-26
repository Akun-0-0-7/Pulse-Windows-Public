using System.Diagnostics;
using System.Text.RegularExpressions;
namespace Pulse.Core;
public static class WindowsCommand
{
    public static ProcessStartInfo StartInfo(string file,IEnumerable<string> arguments)
    {
        var args=arguments.ToList();
        if(Path.GetExtension(file).ToLowerInvariant() is ".cmd" or ".bat") {
            var source=File.ReadAllText(file);var match=Regex.Match(source,"(?i)%dp0%[\\\\/]([^\"\r\n]+\\.(?:c?js|mjs|exe))");
            if(!match.Success)throw new InvalidOperationException("该命令包装器需要直接指定程序路径。");
            var target=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!,match.Groups[1].Value.Replace('/',Path.DirectorySeparatorChar)));
            if(!File.Exists(target))throw new FileNotFoundException("本机工具入口不存在",target);
            if(Path.GetExtension(target).Equals(".exe",StringComparison.OrdinalIgnoreCase))file=target;
            else {var sibling=Path.Combine(Path.GetDirectoryName(file)!,"node.exe");file=File.Exists(sibling)?sibling:LocalPaths.FindExecutable("node")??throw new InvalidOperationException("未找到运行该工具所需的 Node.js");args.Insert(0,target);}
        }
        var start=new ProcessStartInfo(file){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=System.Text.Encoding.UTF8,StandardErrorEncoding=System.Text.Encoding.UTF8};foreach(var argument in args)start.ArgumentList.Add(argument);return start;
    }
}

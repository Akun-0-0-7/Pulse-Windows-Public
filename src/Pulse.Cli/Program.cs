using Pulse.Core;
Console.OutputEncoding=System.Text.Encoding.UTF8;
var root=Environment.GetEnvironmentVariable("PULSE_DATA_DIR")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PulseWindows");
try{return await CommandModes.RunAsync(args,root);}catch(Exception exception) when(exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException){Console.Error.WriteLine("Pulse: 无法读取本机配置或记录，请检查数据目录。");return 1;}

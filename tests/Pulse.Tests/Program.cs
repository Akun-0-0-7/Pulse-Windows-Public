using System.Reflection;
namespace Pulse.Tests;
[AttributeUsage(AttributeTargets.Method)] public sealed class CheckAttribute : Attribute;
public static class Assert
{
    public static void True(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
    public static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; actual {actual}"); }
    public static void Near(double expected, double actual, double tolerance = 0.00001) { True(Math.Abs(expected - actual) <= tolerance, $"Expected {expected}; actual {actual}"); }
    public static T Throws<T>(Action action) where T : Exception { try { action(); } catch(T exception) { return exception; } throw new Exception($"Expected {typeof(T).Name}"); }
}
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if(args is ["--version"]&&Environment.GetEnvironmentVariable("PULSE_TEST_WINDOW_STARTER_FIXTURE")=="true")
        {if(Environment.GetEnvironmentVariable("PULSE_TEST_STARTER_CALLS") is {} calls)File.AppendAllText(calls,"version\n");Console.WriteLine("codex-cli 0.165.0-test");return 0;}
        if(args is ["exec", ..]&&Environment.GetEnvironmentVariable("PULSE_TEST_WINDOW_STARTER_FIXTURE")=="true")
        {
            if(Environment.GetEnvironmentVariable("PULSE_TEST_STARTER_CALLS") is {} calls)File.AppendAllText(calls,"exec\n");
            Console.Write(System.Text.Json.JsonSerializer.Serialize(new {cwd=Environment.CurrentDirectory,args}));
            return 0;
        }
        if(args is ["app-server"]&&Environment.GetEnvironmentVariable("PULSE_TEST_WINDOW_STARTER_FIXTURE")=="true")
        {
            using var initialize=System.Text.Json.JsonDocument.Parse((await Console.In.ReadLineAsync())!);
            if(initialize.RootElement.GetProperty("method").GetString()!="initialize")return 3;
            Console.WriteLine("{\"id\":1,\"result\":{}}");
            using var initialized=System.Text.Json.JsonDocument.Parse((await Console.In.ReadLineAsync())!);
            if(initialized.RootElement.GetProperty("method").GetString()!="initialized")return 4;
            using var read=System.Text.Json.JsonDocument.Parse((await Console.In.ReadLineAsync())!);
            var method=read.RootElement.GetProperty("method").GetString();
            if(Environment.GetEnvironmentVariable("PULSE_TEST_STARTER_CALLS") is {} calls)File.AppendAllText(calls,method+"\n");
            if(method=="config/read")
            {
                if(read.RootElement.GetProperty("params").GetProperty("includeLayers").GetBoolean())return 6;
                if(read.RootElement.GetProperty("params").GetProperty("cwd").GetString()!=Environment.CurrentDirectory)return 7;
                if(Environment.GetEnvironmentVariable("PULSE_TEST_STARTER_EXPECTED_PATH") is {} expectedPath&&Environment.GetEnvironmentVariable("PATH")!=expectedPath)return 8;
                if(Environment.GetEnvironmentVariable("PULSE_TEST_STARTER_EXPECTED_HOME") is {} expectedHome&&Environment.GetEnvironmentVariable("CODEX_HOME")!=expectedHome)return 9;
                var result=Environment.GetEnvironmentVariable("PULSE_TEST_STARTER_CONFIG")??"{\"config\":{\"model\":\"fixture-configured\",\"model_provider\":null}}";
                if(result=="RPC_ERROR")Console.WriteLine("{\"id\":2,\"error\":{\"message\":\"SECRET-fixture\"}}");
                else Console.WriteLine("{\"id\":2,\"result\":"+result+"}");
            }
            else if(method=="model/list")
            {
                var cursor=read.RootElement.GetProperty("params").GetProperty("cursor");
                var next=cursor.ValueKind==System.Text.Json.JsonValueKind.String?Environment.GetEnvironmentVariable("PULSE_TEST_STARTER_MODELS_NEXT"):null;
                Console.WriteLine("{\"id\":2,\"result\":"+(next??Environment.GetEnvironmentVariable("PULSE_TEST_STARTER_MODELS")??"{\"data\":[{\"model\":\"fixture-default\",\"hidden\":false,\"isDefault\":true}],\"nextCursor\":null}")+"}");
            }
            else return 5;
            return 0;
        }
        if(args is ["app-server"]&&Environment.GetEnvironmentVariable("PULSE_TEST_CODEX_FIXTURE")=="true")
        {
            if(Environment.GetEnvironmentVariable("PULSE_TEST_CODEX_MARKER") is {} marker)File.WriteAllText(marker,"started");
            using var initialize=System.Text.Json.JsonDocument.Parse((await Console.In.ReadLineAsync())!);
            if(initialize.RootElement.GetProperty("method").GetString()!="initialize")return 3;
            Console.WriteLine("{\"id\":1,\"result\":{}}");
            using var initialized=System.Text.Json.JsonDocument.Parse((await Console.In.ReadLineAsync())!);
            if(initialized.RootElement.GetProperty("method").GetString()!="initialized")return 4;
            using var read=System.Text.Json.JsonDocument.Parse((await Console.In.ReadLineAsync())!);
            if(read.RootElement.GetProperty("method").GetString()!="account/rateLimits/read")return 5;
            Console.WriteLine("{\"id\":2,\"result\":{\"rateLimits\":{\"primary\":{\"usedPercent\":17,\"windowDurationMins\":300}}}}");
            return 0;
        }
        int passed = 0, failed = 0;
        foreach (var method in Assembly.GetExecutingAssembly().GetTypes().SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static)).Where(m => m.IsDefined(typeof(CheckAttribute))).OrderBy(m => m.DeclaringType!.Name).ThenBy(m => m.Name))
        {
            if (args.Length > 0 && !($"{method.DeclaringType!.Name}.{method.Name}").Contains(args[0], StringComparison.OrdinalIgnoreCase)) continue;
            try { if(method.Invoke(null, null) is Task task) await task; Console.WriteLine($"PASS {method.DeclaringType!.Name}.{method.Name}"); passed++; }
            catch(Exception exception) { Console.WriteLine($"FAIL {method.DeclaringType!.Name}.{method.Name}: {(exception.InnerException ?? exception).Message}"); failed++; }
        }
        Console.WriteLine($"Results: {passed} passed, {failed} failed");
        return failed == 0 && passed > 0 ? 0 : 1;
    }
}

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
        if(args is ["exec", ..]&&Environment.GetEnvironmentVariable("PULSE_TEST_WINDOW_STARTER_FIXTURE")=="true")
        {
            Console.Write(System.Text.Json.JsonSerializer.Serialize(new {cwd=Environment.CurrentDirectory,args}));
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

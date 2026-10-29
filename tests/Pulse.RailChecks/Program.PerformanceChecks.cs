using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Pulse.Desktop;
internal static partial class Program
{
    private static void PerformanceChecks(string output)
    {
        var samples=new List<object>();
        foreach(var (name,response,damping) in new[]{("rail",.32,.86),("detail",.28,.84)})
        {
            foreach(var rate in new[]{60,120,144})
            {
                double position=0,velocity=0;var steps=0;
                while((Math.Abs(1-position)>.0001||Math.Abs(velocity)>.001)&&steps<rate*3)
                {(position,velocity)=RailMotionMath.Step(position,velocity,1,1d/rate,response,damping);steps++;}
                if(steps>=rate*3)throw new Exception("Spring did not settle at "+rate+" Hz.");
                samples.Add(new{name,simulatedHz=rate,settleMilliseconds=Math.Round(1000d*steps/rate,2),position,velocity});
            }
        }
        var callbacks=0;using var motion=new RailMotionValue(0,.32,.86,_=>callbacks++);
        motion.MoveTo(1);RunFor(1500);
        if(motion.IsRunning)throw new Exception("Spring still subscribed after settling.");
        var settledCallbacks=callbacks;using var process=Process.GetCurrentProcess();var cpu=process.TotalProcessorTime;var watch=Stopwatch.StartNew();
        RunFor(2000);var elapsed=watch.Elapsed.TotalMilliseconds;process.Refresh();var cpuMs=(process.TotalProcessorTime-cpu).TotalMilliseconds;
        if(callbacks!=settledCallbacks)throw new Exception("Settled spring kept issuing updates.");
        var report=new{samples,settledCallbacks,idleCallbacks=callbacks-settledCallbacks,idleWallMilliseconds=elapsed,isolatedDispatcherCpuMilliseconds=cpuMs,logicalProcessors=Environment.ProcessorCount,note="Synthetic spring timings and isolated dispatcher sample; not screen FPS, GPU measurements, or whole-app CPU."};
        File.WriteAllText(Path.Combine(output,"motion-performance.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("PASS: simulated 60/120/144 Hz spring settling and zero callbacks after settling; performance sample saved.");
    }
}

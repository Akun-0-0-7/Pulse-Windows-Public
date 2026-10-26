using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace Pulse.Desktop;

/// <summary>A spring whose response is its natural period in seconds.</summary>
public static class RailMotionMath
{
    public static (double Position,double Velocity) Step(double position,double velocity,double target,double seconds,double response,double damping)
    {
        Finite(position,nameof(position));Finite(velocity,nameof(velocity));Finite(target,nameof(target));
        if(!double.IsFinite(seconds)||seconds<0)throw new ArgumentOutOfRangeException(nameof(seconds));
        if(!double.IsFinite(response)||response<=0)throw new ArgumentOutOfRangeException(nameof(response));
        if(!double.IsFinite(damping)||damping<0)throw new ArgumentOutOfRangeException(nameof(damping));
        if(seconds==0)return(position,velocity);

        var omega=2*Math.PI/response;
        var displacement=position-target;
        if(damping<1)
        {
            var ratio=Math.Sqrt((1-damping)*(1+damping));
            var decay=Math.Exp(-damping*omega*seconds);
            if(decay==0)return(target,0);
            var angle=omega*ratio*seconds;
            var cosine=Math.Cos(angle);var sine=Math.Sin(angle);
            var next=decay*(displacement*cosine+(velocity/omega+damping*displacement)/ratio*sine);
            var nextVelocity=decay*(velocity*cosine-(damping*velocity+omega*displacement)/ratio*sine);
            return(target+next,nextVelocity);
        }
        if(damping==1)
        {
            var decay=Math.Exp(-omega*seconds);
            if(decay==0)return(target,0);
            var coefficient=velocity+omega*displacement;
            return(target+decay*(displacement+coefficient*seconds),decay*(velocity-omega*coefficient*seconds));
        }

        var radical=Math.Sqrt((damping-1)*(damping+1));
        var slow=-omega/(damping+radical);var fast=-omega*(damping+radical);
        var slowWeight=(velocity-fast*displacement)/(slow-fast);
        var fastWeight=displacement-slowWeight;
        var slowPart=slowWeight*Math.Exp(slow*seconds);var fastPart=fastWeight*Math.Exp(fast*seconds);
        return(target+slowPart+fastPart,slow*slowPart+fast*fastPart);
    }

    private static void Finite(double value,string name)
    {
        if(!double.IsFinite(value))throw new ArgumentOutOfRangeException(name);
    }
}

/// <summary>One interruptible scalar spring, subscribed to WPF frames only while moving.</summary>
public sealed class RailMotionValue : IDisposable
{
    private const double PositionTolerance=.0001,VelocityTolerance=.001;
    private readonly double response,damping;
    private readonly Action<double> changed;
    private double velocity;
    private long lastTick;
    private bool disposed;
    public double Value {get;private set;}
    public double Target {get;private set;}
    public bool IsRunning {get;private set;}

    public RailMotionValue(double initial,double response,double damping,Action<double> changed)
    {
        ArgumentNullException.ThrowIfNull(changed);
        _=RailMotionMath.Step(initial,0,initial,0,response,damping);
        Value=Target=initial;this.response=response;this.damping=damping;this.changed=changed;
    }

    public void MoveTo(double target,bool animate=true)
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        if(!double.IsFinite(target))throw new ArgumentOutOfRangeException(nameof(target));
        if(!animate||!SystemParameters.ClientAreaAnimation){SnapTo(target);return;}
        if(Target==target)return;
        Target=target;
        // Retarget from the last presented position and velocity. Time before the
        // request must not be integrated against the new target on the next frame.
        lastTick=Stopwatch.GetTimestamp();
        if(IsRunning)return;
        IsRunning=true;
        CompositionTarget.Rendering+=Rendering;
        SystemParameters.StaticPropertyChanged+=SystemParameterChanged;
    }

    public void SnapTo(double value)
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        if(!double.IsFinite(value))throw new ArgumentOutOfRangeException(nameof(value));
        Stop();Value=Target=value;velocity=0;
        changed(Value);
    }

    private void Rendering(object? sender,EventArgs args)
    {
        if(disposed||!IsRunning)return;
        if(!SystemParameters.ClientAreaAnimation){SnapTo(Target);return;}
        var now=Stopwatch.GetTimestamp();var seconds=Stopwatch.GetElapsedTime(lastTick,now).TotalSeconds;lastTick=now;
        if(seconds<=0)return;
        (Value,velocity)=RailMotionMath.Step(Value,velocity,Target,seconds,response,damping);
        if(Math.Abs(Value-Target)<=PositionTolerance&&Math.Abs(velocity)<=VelocityTolerance)
        {
            Value=Target;velocity=0;Stop();
        }
        // Completion observers may close a window or retarget the same spring.
        // They see its settled state, and no old completion runs after this call.
        try{changed(Value);}
        catch{Stop();throw;}
    }

    private void SystemParameterChanged(object? sender,PropertyChangedEventArgs args)
    {
        if(!disposed&&IsRunning&&(string.IsNullOrEmpty(args.PropertyName)||args.PropertyName==nameof(SystemParameters.ClientAreaAnimation))&&!SystemParameters.ClientAreaAnimation)
            SnapTo(Target);
    }

    private void Stop()
    {
        if(!IsRunning)return;
        IsRunning=false;
        CompositionTarget.Rendering-=Rendering;
        SystemParameters.StaticPropertyChanged-=SystemParameterChanged;
    }

    public void Dispose()
    {
        if(disposed)return;
        Stop();disposed=true;velocity=0;
    }
}

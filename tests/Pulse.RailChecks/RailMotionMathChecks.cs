using System;
using Pulse.Desktop;

internal static class RailMotionMathChecks
{
    public static void Run()
    {
        // An undamped quarter-period has zero displacement and maximum velocity.
        // This fixture catches both an immediate snap and the wrong response units.
        var quarter=RailMotionMath.Step(0,0,1,.25,1,0);
        Near(1,quarter.Position,1e-12,"Quarter-period spring position");
        Near(2*Math.PI,quarter.Velocity,1e-12,"Quarter-period spring velocity");

        var halfway=RailMotionMath.Step(0,0,1,.5,1,0);
        Near(2,halfway.Position,1e-12,"Undamped spring overshoot");
        Near(0,halfway.Velocity,1e-12,"Half-period spring velocity");

        // At natural frequency 1, these are the closed-form unit-step responses:
        // damping 1 gives 1-(1+t)e^-t; damping 1.25 has roots -.5 and -2.
        foreach(var (damping,position,velocity) in new[]{
            (.5,.3402998466082983,.533507195114693),
            (1d,.2642411176571153,.36787944117144233),
            (1.25,.23640421479536,.314130250984014)})
        {
            var fixture=RailMotionMath.Step(0,0,1,1,2*Math.PI,damping);
            Near(position,fixture.Position,1e-12,"Damping-ratio unit-step position");
            Near(velocity,fixture.Velocity,1e-12,"Damping-ratio unit-step velocity");
        }

        // An analytical spring has the same trajectory at different frame rates,
        // including a delayed frame; Euler stepping does not satisfy this contract.
        foreach(var (response,damping) in new[]{(.32,.86),(.28,.84)})
        {
            var direct=RailMotionMath.Step(.15,.7,1,.4,response,damping);
            foreach(var frames in new[]{24,60,144})
            {
                var state=(Position:.15,Velocity:.7);
                for(var frame=0;frame<frames;frame++)
                    state=RailMotionMath.Step(state.Position,state.Velocity,1,.4/frames,response,damping);
                Near(direct.Position,state.Position,1e-10,"Frame-rate-independent spring position");
                Near(direct.Velocity,state.Velocity,1e-10,"Frame-rate-independent spring velocity");
            }

            var rested=RailMotionMath.Step(.15,.7,1,10,response,damping);
            Near(1,rested.Position,1e-10,"Damped spring settles at its target");
            Near(0,rested.Velocity,1e-10,"Damped spring loses residual velocity");
        }

        var unchanged=RailMotionMath.Step(.4,2,0,0,.32,.86);
        Near(.4,unchanged.Position,0,"Reversal preserves current position");
        Near(2,unchanged.Velocity,0,"Reversal preserves current velocity");
        var reversal=RailMotionMath.Step(.4,2,0,.0001,.32,.86);
        if(reversal.Position<=.4||reversal.Velocity<=0||reversal.Velocity>=2)
            throw new Exception("Reversing a spring discarded its momentum or failed to decelerate.");
        var reversedRest=RailMotionMath.Step(reversal.Position,reversal.Velocity,0,10,.32,.86);
        Near(0,reversedRest.Position,1e-10,"Reversed spring reaches its new target");

        // Translation changes position only, not the spring's physical velocity.
        var origin=RailMotionMath.Step(.2,.3,1,.12,.32,.86);
        var shifted=RailMotionMath.Step(10.2,.3,11,.12,.32,.86);
        Near(origin.Position+10,shifted.Position,1e-12,"Spring target translation");
        Near(origin.Velocity,shifted.Velocity,1e-12,"Spring velocity translation");

        Invalid(()=>RailMotionMath.Step(double.NaN,0,1,.1,.32,.86),"non-finite position");
        Invalid(()=>RailMotionMath.Step(0,double.PositiveInfinity,1,.1,.32,.86),"non-finite velocity");
        Invalid(()=>RailMotionMath.Step(0,0,double.NaN,.1,.32,.86),"non-finite target");
        Invalid(()=>RailMotionMath.Step(0,0,1,-.1,.32,.86),"negative elapsed time");
        Invalid(()=>RailMotionMath.Step(0,0,1,double.PositiveInfinity,.32,.86),"non-finite elapsed time");
        Invalid(()=>RailMotionMath.Step(0,0,1,.1,0,.86),"zero response");
        Invalid(()=>RailMotionMath.Step(0,0,1,.1,double.NaN,.86),"non-finite response");
        Invalid(()=>RailMotionMath.Step(0,0,1,.1,.32,-.1),"negative damping");
        Invalid(()=>RailMotionMath.Step(0,0,1,.1,.32,double.NaN),"non-finite damping");
    }

    private static void Near(double expected,double actual,double tolerance,string name)
    {
        if(!double.IsFinite(actual)||Math.Abs(expected-actual)>tolerance)
            throw new Exception($"{name}: expected {expected:R}, actual {actual:R}.");
    }

    private static void Invalid(Action action,string name)
    {
        try{action();}
        catch(ArgumentOutOfRangeException){return;}
        throw new Exception("Spring accepted "+name+".");
    }
}

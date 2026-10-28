using System;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using Pulse.Desktop;

internal static class PulseShapeChecks
{
    public static void Run()
    {
        // These are silhouette checks: an ordinary rounded rectangle cannot
        // satisfy the flared bounds or the source's fuller n=4 corners.
        foreach(var edge in new[]{"top","bottom","left","right"})
        {
            var horizontal=edge is "top" or "bottom";
            var body=horizontal?new Rect(24,0,300,64):new Rect(0,24,64,300);
            var shape=Call("Rail",body,edge,26d,24d,38d);
            Bounds(shape,horizontal?new Rect(0,0,348,64):new Rect(0,0,64,348),edge+" berth");
            if(!shape.FillContains(new Point(body.Left+body.Width/2,body.Top+body.Height/2)))
                throw new Exception(edge+" rail has a hole in its body.");
        }
        var top=Call("Rail",new Rect(24,0,300,64),"top",26d,24d,38d);
        if(!top.FillContains(new Point(12,1)))throw new Exception("Top rail lost its outward screen-edge flare.");
        if(!top.FillContains(new Point(29,59)))throw new Exception("Rail corner was replaced by a tighter circular corner.");
        if(top.FillContains(new Point(24.1,63.9)))throw new Exception("Rail far corner became square.");
        foreach(var edge in new[]{"top","bottom","left","right"})
        {
            var horizontal=edge is "top" or "bottom";
            var body=horizontal?new Rect(32,0,300,78):new Rect(0,32,78,300);
            var round=Call("RoundRail",body,edge,32d,32d,32d);
            Bounds(round,horizontal?new Rect(0,0,364,78):new Rect(0,0,78,364),edge+" round berth");
        }
        var roundBody=new Rect(32,0,300,78);
        var roundTop=Call("RoundRail",roundBody,"top",32d,32d,32d);
        var squareTop=Call("Rail",roundBody,"top",32d,32d,32d);
        if(roundTop.FillContains(new Point(38,72))||!squareTop.FillContains(new Point(38,72)))
            throw new Exception("Optional round rail failed to replace the n=4 corner with a circular arc.");
        if(!roundTop.FillContains(new Point(16,1)))throw new Exception("Round rail lost its screen-edge flare.");
        var free=Call("Rail",new Rect(8,9,200,64),"free",32d,24d,38d);
        Bounds(free,new Rect(8,9,200,64),"Free rail");
        if(free.FillContains(new Point(8.1,9.1)))throw new Exception("Free rail retained a docked shoulder.");

        // Tail anchoring moves independently when the body is screen-clamped.
        var card=new Rect(30,40,250,115);
        foreach(var side in new[]{"top","bottom","left","right"})
        {
            var horizontal=side is "top" or "bottom";
            var low=horizontal?card.Left:card.Top;
            var high=horizontal?card.Right:card.Bottom;
            var expected=side switch
            {
                "top"=>new Rect(card.Left,card.Top-20,card.Width,card.Height+20),
                "bottom"=>new Rect(card.Left,card.Top,card.Width,card.Height+20),
                "left"=>new Rect(card.Left-20,card.Top,card.Width+20,card.Height),
                _=>new Rect(card.Left,card.Top,card.Width+20,card.Height)
            };
            foreach(var anchor in new[]{low-1000,(low+high)/2,high+1000})
            {
                var bubble=Call("Bubble",card,side,anchor,20d,20d,40d);
                Bounds(bubble,expected,side+" bubble");
                var clamped=Math.Clamp(anchor,low+40,high-40);
                var nearTip=side switch
                {
                    "top"=>new Point(clamped,card.Top-19.9),
                    "bottom"=>new Point(clamped,card.Bottom+19.9),
                    "left"=>new Point(card.Left-19.9,clamped),
                    _=>new Point(card.Right+19.9,clamped)
                };
                if(!bubble.FillContains(nearTip))throw new Exception(side+" bubble tail missed its clamped anchor.");
                if(!bubble.FillContains(new Point(card.Left+card.Width/2,card.Top+card.Height/2)))
                    throw new Exception(side+" bubble tail winding cut a hole into the body.");
            }
        }
        // The curved flank is narrower than a triangle at half reach.
        var curved=Call("Bubble",card,"top",155d,20d,20d,40d);
        if(curved.FillContains(new Point(164.5,30)))throw new Exception("Bubble pointer became a straight triangle.");
        // SwiftUI's native continuous card corners are visibly rounder than
        // the berth's explicitly sampled n=4 corners at the same nominal radius.
        if(curved.FillContains(new Point(card.Left+4,card.Top+4)))
            throw new Exception("Card body reused the squarer n=4 rail corners.");
        Bounds(Call("Bubble",new Rect(0,0,12,9),"top",1000d,20d,20d,40d),new Rect(0,-20,12,29),"Tiny bubble");
    }

    private static Geometry Call(string method,params object[] args)
    {
        var type=typeof(RailWindow).Assembly.GetType("Pulse.Desktop.PulseShapes")
            ??throw new Exception("Source-derived PulseShapes geometry is missing.");
        var member=type.GetMethod(method,BindingFlags.Public|BindingFlags.Static)
            ??throw new Exception("PulseShapes."+method+" is missing.");
        return (Geometry)(member.Invoke(null,args)??throw new Exception(method+" returned no geometry."));
    }
    private static void Bounds(Geometry geometry,Rect expected,string name)
    {
        var actual=geometry.Bounds;
        Near(expected.X,actual.X,name+" left");Near(expected.Y,actual.Y,name+" top");
        Near(expected.Width,actual.Width,name+" width");Near(expected.Height,actual.Height,name+" height");
    }
    private static void Near(double expected,double actual,string name)
    {
        if(!double.IsFinite(actual)||Math.Abs(expected-actual)>1e-6)
            throw new Exception($"{name}: expected {expected:R}, actual {actual:R}.");
    }
}

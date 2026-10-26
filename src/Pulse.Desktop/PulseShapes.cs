// Copyright (c) 2026 qunqin24. Licensed under the Apache License, Version 2.0.
// Windows geometry adaptation of qunqin24/Pulse (Apache-2.0):
// Sources/Pulse/Panel/UsageDockView.swift, UsageBubbleShape.swift, and
// DropletTether.swift at fc10fb7f13999d16f27f283c4bd9e83c80a2885d.
// https://github.com/qunqin24/Pulse/tree/fc10fb7f13999d16f27f283c4bd9e83c80a2885d/Sources/Pulse/Panel
namespace Pulse.Desktop;

/// <summary>Source-derived dock shoulders and the compact card's curved pointer.</summary>
public static class PulseShapes
{
    private const int CornerSamples=48;
    private static readonly Point[] FlareTurn=CreateFlareTurn();

    /// <summary>
    /// The inner berth body excludes its two end flares. At the top edge,
    /// flare extends left/right of body and shoulder is its depth below the
    /// screen edge. Standard upstream values are radius 26, flare 24, shoulder 38.
    /// </summary>
    public static Geometry Rail(Rect body,string edge,double radius,double flare,double shoulder)
        =>Berth(body,edge,radius,flare,shoulder,false);

    /// <summary>
    /// Upstream's optional usesRoundEnds silhouette, as shown in the reference:
    /// circular far corners, with radius/flare/shoulder each 32 at standard scale.
    /// </summary>
    public static Geometry RoundRail(Rect body,string edge,double radius,double flare,double shoulder)
        =>Berth(body,edge,radius,flare,shoulder,true);

    private static Geometry Berth(Rect body,string edge,double radius,double flare,double shoulder,bool roundEnds)
    {
        if(body.IsEmpty||body.Width<=0||body.Height<=0)return Geometry.Empty;
        if(edge is not ("top" or "bottom" or "left" or "right"))
        {
            var round=Math.Clamp(radius,0,Math.Min(body.Width,body.Height)/2);
            var capsule=new RectangleGeometry(body,round,round);capsule.Freeze();return capsule;
        }
        var across=edge is "top" or "bottom"?body.Height:body.Width;
        var along=edge is "top" or "bottom"?body.Width:body.Height;
        var r=Math.Clamp(radius,0,Math.Min(across,along/2));
        var f=Math.Max(flare,0);
        // A corner and the shoulder share this extent, as in DockBerthShape.
        var reach=Math.Clamp(shoulder,0,across-r);
        Point Map(Point point)=>edge switch
        {
            "left"=>new Point(body.Right-point.X,body.Top+point.Y),
            "top"=>new Point(body.Left+point.Y,body.Bottom-point.X),
            "bottom"=>new Point(body.Left+point.Y,body.Top+point.X),
            _=>new Point(body.Left+point.X,body.Top+point.Y)
        };
        var geometry=new StreamGeometry{FillRule=FillRule.Nonzero};
        using(var path=geometry.Open())
        {
            path.BeginFigure(Map(new Point(r,0)),true,true);
            path.LineTo(Map(new Point(across-reach,0)),true,false);
            FluidTurn(path,Map,new Point(across-reach,0),new Vector(1,0),new Point(across,-f),new Vector(0,-1));
            path.LineTo(Map(new Point(across,along+f)),true,false);
            FluidTurn(path,Map,new Point(across,along+f),new Vector(0,-1),new Point(across-reach,along),new Vector(-1,0));
            path.LineTo(Map(new Point(r,along)),true,false);
            if(roundEnds)ArcCorner(path,Map,new Point(r,along-r),r,new Vector(0,1),new Vector(-1,0));
            else Corner(path,Map,new Point(r,along-r),r,new Vector(0,1),new Vector(-1,0));
            path.LineTo(Map(new Point(0,r)),true,false);
            if(roundEnds)ArcCorner(path,Map,new Point(r,r),r,new Vector(-1,0),new Vector(0,-1));
            else Corner(path,Map,new Point(r,r),r,new Vector(-1,0),new Vector(0,-1));
        }
        geometry.Freeze();return geometry;
    }

    /// <summary>
    /// The tail is included in the returned silhouette, outside body. Anchor
    /// is an absolute X on top/bottom, or Y on left/right, and avoids the corners.
    /// </summary>
    public static Geometry Bubble(Rect body,string arrowSide,double anchor,double radius,double tailReach,double tailBase)
    {
        if(body.IsEmpty||body.Width<=0||body.Height<=0)return Geometry.Empty;
        var across=arrowSide is "top" or "bottom"?body.Height:body.Width;
        var along=arrowSide is "top" or "bottom"?body.Width:body.Height;
        var r=Math.Clamp(radius,0,Math.Min(across,along)/2);
        var half=Math.Clamp(tailBase/2,0,Math.Max(0,along/2-r));
        var reach=Math.Max(0,tailReach);
        var relative=anchor-(arrowSide is "top" or "bottom"?body.Left:body.Top);
        var centre=Math.Clamp(relative,r+half,along-r-half);
        Point Map(Point point)=>arrowSide switch
        {
            "left"=>new Point(body.Right-point.X,body.Top+point.Y),
            "top"=>new Point(body.Left+point.Y,body.Bottom-point.X),
            "bottom"=>new Point(body.Left+point.Y,body.Top+point.X),
            _=>new Point(body.Left+point.X,body.Top+point.Y)
        };
        var geometry=new StreamGeometry{FillRule=FillRule.Nonzero};
        using(var path=geometry.Open())
        {
            // Upstream uses SwiftUI's native .continuous rounded rectangle here,
            // not DockBerthShape's explicit n=4 corner. Circular Bezier corners
            // preserve its nominal radius and better approximate that native
            // outline on WPF; this is not claimed to reproduce Apple's curve.
            path.BeginFigure(Map(new Point(r,0)),true,true);
            path.LineTo(Map(new Point(across-r,0)),true,false);
            CircularCorner(path,Map,new Point(across-r,r),r,new Vector(0,-1),new Vector(1,0));
            path.LineTo(Map(new Point(across,along-r)),true,false);
            CircularCorner(path,Map,new Point(across-r,along-r),r,new Vector(1,0),new Vector(0,1));
            path.LineTo(Map(new Point(r,along)),true,false);
            CircularCorner(path,Map,new Point(r,along-r),r,new Vector(0,1),new Vector(-1,0));
            path.LineTo(Map(new Point(0,r)),true,false);
            CircularCorner(path,Map,new Point(r,r),r,new Vector(-1,0),new Vector(0,-1));
            if(reach>0&&half>0&&arrowSide is "top" or "bottom" or "left" or "right")
            {
                // UsageBubbleShape's softened tail: cubic flanks, not a triangle.
                // The subpath winds with the body and overlaps by 8% of reach,
                // avoiding seams and cancellation at reflected left/top edges.
                path.BeginFigure(Map(new Point(across,centre-half)),true,true);
                path.BezierTo(Map(new Point(across+reach*.24,centre-half*.44)),
                    Map(new Point(across+reach*.55,centre-half*.24)),
                    Map(new Point(across+reach,centre)),true,false);
                path.BezierTo(Map(new Point(across+reach*.55,centre+half*.24)),
                    Map(new Point(across+reach*.24,centre+half*.44)),
                    Map(new Point(across,centre+half)),true,false);
                path.LineTo(Map(new Point(across-reach*.08,centre+half)),true,false);
                path.LineTo(Map(new Point(across-reach*.08,centre-half)),true,false);
            }
        }
        geometry.Freeze();return geometry;
    }

    private static void CircularCorner(StreamGeometryContext path,Func<Point,Point> map,Point centre,double radius,Vector from,Vector to)
    {
        if(radius<=0)return;
        const double k=.5522847498307936;
        path.BezierTo(map(centre+radius*(from+k*to)),map(centre+radius*(to+k*from)),map(centre+radius*to),true,false);
    }

    private static void ArcCorner(StreamGeometryContext path,Func<Point,Point> map,Point centre,double radius,Vector from,Vector to)
    {
        if(radius<=0)return;
        var origin=map(centre);var start=map(centre+radius*from);var end=map(centre+radius*to);
        var sweep=Vector.CrossProduct(start-origin,end-origin)>0?SweepDirection.Clockwise:SweepDirection.Counterclockwise;
        path.ArcTo(end,new Size(radius,radius),0,false,sweep,true,false);
    }

    private static void Corner(StreamGeometryContext path,Func<Point,Point> map,Point centre,double radius,Vector from,Vector to)
    {
        if(radius<=0)return;
        for(var step=1;step<=CornerSamples;step++)
        {
            var angle=(double)step/CornerSamples*Math.PI/2;
            var along=Math.Sqrt(Math.Max(0,Math.Cos(angle)));
            var across=Math.Sqrt(Math.Max(0,Math.Sin(angle)));
            path.LineTo(map(new Point(centre.X+radius*(from.X*along+to.X*across),
                centre.Y+radius*(from.Y*along+to.Y*across))),true,false);
        }
    }

    // DropletTether.fluidTurn: integrate a half linear / half cosine heading,
    // using the same 32 midpoint samples, then map the normalized quarter turn.
    private static Point[] CreateFlareTurn()
    {
        const int steps=32;
        var points=new Point[steps];var x=0d;var y=0d;
        for(var step=0;step<steps;step++)
        {
            var s=(step+.5)/steps;
            var eased=(1-Math.Cos(Math.PI*s))/2;
            var heading=Math.PI/2*(.5*s+.5*eased);
            x+=Math.Cos(heading)/steps;y+=Math.Sin(heading)/steps;
            points[step]=new Point(x,y);
        }
        for(var step=0;step<steps;step++)points[step]=new Point(points[step].X/x,points[step].Y/y);
        return points;
    }

    private static void FluidTurn(StreamGeometryContext path,Func<Point,Point> map,Point from,Vector along,Point to,Vector then)
    {
        var delta=to-from;var first=Vector.Multiply(delta,along);var second=Vector.Multiply(delta,then);
        foreach(var point in FlareTurn)
            path.LineTo(map(new Point(from.X+along.X*point.X*first+then.X*point.Y*second,
                from.Y+along.Y*point.X*first+then.Y*point.Y*second)),true,false);
    }
}

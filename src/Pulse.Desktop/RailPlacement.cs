namespace Pulse.Desktop;

public static class RailPlacement
{
    public static Point Physical(Rect workArea,Size railSize,double scale,string edge,Point? saved)
    {
        if(!double.IsFinite(scale)||scale<=0)throw new ArgumentOutOfRangeException(nameof(scale));
        var offset=Calculate(new Rect(0,0,workArea.Width/scale,workArea.Height/scale),railSize,edge,saved);
        return new Point(workArea.Left+offset.X*scale,workArea.Top+offset.Y*scale);
    }
    public static Point Calculate(Rect workArea,Size railSize,string edge,Point? saved)
    {
        var left=workArea.Left;var top=workArea.Top;var right=workArea.Right;var bottom=workArea.Bottom;
        var x=edge switch{"left"=>left+6,"right"=>right-railSize.Width-6,"top" or "bottom"=>left+(workArea.Width-railSize.Width)/2,_=>Math.Clamp(saved?.X??right-railSize.Width-30,left,Math.Max(left,right-railSize.Width))};
        var y=edge switch{"top"=>top+6,"bottom"=>bottom-railSize.Height-6,"left" or "right"=>top+(workArea.Height-railSize.Height)/2,_=>Math.Clamp(saved?.Y??top+160,top,Math.Max(top,bottom-railSize.Height))};
        return new Point(x,y);
    }
}

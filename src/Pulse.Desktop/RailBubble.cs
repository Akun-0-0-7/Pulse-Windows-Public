namespace Pulse.Desktop;

// The card body and the soft pointer form one source-derived shape, not an overlaid triangle.
internal sealed class RailBubble : Decorator
{
    private string arrowSide="top";
    private double anchor;
    public double Scale {get;set;}=1;
    public Brush Fill {get;set;}=Brushes.Black;
    public string ArrowSide
    {
        get=>arrowSide;
        set{if(value==arrowSide)return;arrowSide=value;InvalidateMeasure();InvalidateVisual();}
    }
    public double Anchor {get=>anchor;set{anchor=value;InvalidateVisual();}}
    public Thickness Insets
    {
        get
        {
            var p=18*Scale;var tail=20*Scale;
            return new Thickness(p+(arrowSide=="left"?tail:0),p+(arrowSide=="top"?tail:0),p+(arrowSide=="right"?tail:0),p+(arrowSide=="bottom"?tail:0));
        }
    }
    protected override Size MeasureOverride(Size available)
    {
        var i=Insets;Child?.Measure(new Size(Math.Max(0,available.Width-i.Left-i.Right),Math.Max(0,available.Height-i.Top-i.Bottom)));
        var size=Child?.DesiredSize??new Size();return new Size(size.Width+i.Left+i.Right,size.Height+i.Top+i.Bottom);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var i=Insets;Child?.Arrange(new Rect(i.Left,i.Top,Math.Max(0,finalSize.Width-i.Left-i.Right),Math.Max(0,finalSize.Height-i.Top-i.Bottom)));return finalSize;
    }
    protected override void OnRender(DrawingContext drawing)
    {
        var t=20*Scale;var rect=new Rect(arrowSide=="left"?t:0,arrowSide=="top"?t:0,Math.Max(0,ActualWidth-(arrowSide is "left" or "right"?t:0)),Math.Max(0,ActualHeight-(arrowSide is "top" or "bottom"?t:0)));
        if(rect.Width<=0||rect.Height<=0)return;
        drawing.DrawGeometry(Fill,null,PulseShapes.Bubble(rect,arrowSide,anchor,20*Scale,t,40*Scale));
    }
}

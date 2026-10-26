namespace Pulse.Desktop;

// Geometry changes invalidate drawing only; the ring layout and native window stay untouched.
internal sealed class RailChrome : FrameworkElement
{
    private readonly SolidColorBrush fill=new();
    private readonly Pen outline=new(Ui.BorderBrush,1);
    private double radius;
    public Rect VisibleBounds {get;private set;}
    public void Update(Rect bounds,Color color,double openness)
    {
        VisibleBounds=bounds;fill.Color=color;outline.Thickness=openness;radius=3+19*openness;InvalidateVisual();
    }
    protected override void OnRender(DrawingContext drawing)
        =>drawing.DrawRoundedRectangle(fill,outline.Thickness>0?outline:null,VisibleBounds,radius,radius);
}

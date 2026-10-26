namespace Pulse.Desktop;

// Geometry changes invalidate drawing only; the ring layout and native window stay untouched.
internal sealed class RailChrome : FrameworkElement
{
    private readonly SolidColorBrush fill=new();
    private readonly Pen outline=new(Ui.BorderBrush,1);
    private Geometry silhouette=Geometry.Empty;
    public Rect VisibleBounds {get;private set;}
    public void Update(Rect bounds,Color color,double openness,string edge,double scale,bool light)
    {
        VisibleBounds=bounds;fill.Color=color;outline.Thickness=light?.7*openness:0;
        var radius=edge=="free"?Math.Min(bounds.Width,bounds.Height)/2:3+(32*scale-3)*openness;
        silhouette=PulseShapes.RoundRail(bounds,edge,radius,32*scale*openness,32*scale*openness);InvalidateVisual();
    }
    protected override void OnRender(DrawingContext drawing)
        =>drawing.DrawGeometry(fill,outline.Thickness>0?outline:null,silhouette);
}

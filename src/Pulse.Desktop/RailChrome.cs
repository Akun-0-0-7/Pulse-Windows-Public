namespace Pulse.Desktop;

// Geometry changes invalidate drawing only; the ring layout and native window stay untouched.
internal sealed class RailChrome : FrameworkElement
{
    private readonly SolidColorBrush fill=new();
    private readonly SolidColorBrush track=new();
    private readonly Pen outline=new(Ui.BorderBrush,1);
    private Geometry silhouette=Geometry.Empty;
    private Rect progressBounds=Rect.Empty;
    private bool showProgress;
    public Rect VisibleBounds {get;private set;}
    public void Update(Rect bounds,Color color,double openness,string edge,double scale,bool light,bool progress=false,double? fraction=null,Color? trackColor=null)
    {
        VisibleBounds=bounds;fill.Color=color;outline.Thickness=light?.7*openness:0;
        showProgress=progress;track.Color=trackColor??color;progressBounds=Rect.Empty;
        if(progress&&fraction is {} value&&double.IsFinite(value)&&value>0)
        {
            value=Math.Clamp(value,0,1);
            progressBounds=edge is "left" or "right"
                ?new Rect(bounds.X,bounds.Bottom-bounds.Height*value,bounds.Width,bounds.Height*value)
                :new Rect(bounds.X,bounds.Y,bounds.Width*value,bounds.Height);
        }
        var radius=edge=="free"?Math.Min(bounds.Width,bounds.Height)/2:3+(32*scale-3)*openness;
        silhouette=PulseShapes.RoundRail(bounds,edge,radius,32*scale*openness,32*scale*openness);InvalidateVisual();
    }
    protected override void OnRender(DrawingContext drawing)
    {
        drawing.DrawGeometry(showProgress?track:fill,outline.Thickness>0?outline:null,silhouette);
        if(!showProgress||progressBounds.IsEmpty)return;
        drawing.PushClip(silhouette);
        drawing.DrawRectangle(fill,null,progressBounds);
        drawing.Pop();
    }
}

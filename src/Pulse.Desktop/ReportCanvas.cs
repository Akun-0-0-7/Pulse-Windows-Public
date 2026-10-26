using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using Gdi=System.Drawing;
using Gdi2D=System.Drawing.Drawing2D;
using GdiImaging=System.Drawing.Imaging;

namespace Pulse.Desktop;

/// <summary>CPU report drawing; exported pixels do not depend on the desktop compositor.</summary>
public sealed class ReportCanvas : IDisposable
{
    private readonly Gdi.Bitmap bitmap;
    private readonly Gdi.Graphics drawing;
    public ReportCanvas(int width,int height)
    {
        bitmap=new Gdi.Bitmap(width,height,GdiImaging.PixelFormat.Format32bppPArgb);bitmap.SetResolution(96,96);
        drawing=Gdi.Graphics.FromImage(bitmap);drawing.SmoothingMode=Gdi2D.SmoothingMode.AntiAlias;drawing.PixelOffsetMode=Gdi2D.PixelOffsetMode.HighQuality;drawing.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;drawing.CompositingQuality=Gdi2D.CompositingQuality.HighQuality;
    }
    public void DrawRectangle(Brush? brush,Pen? pen,Rect rectangle)
    {
        var bounds=Bounds(rectangle);if(bounds.Width<=0||bounds.Height<=0)return;
        if(brush is not null){using var fill=Fill(brush,bounds);drawing.FillRectangle(fill,bounds);}
        if(pen is not null){using var stroke=Stroke(pen);drawing.DrawRectangle(stroke,bounds.X,bounds.Y,bounds.Width,bounds.Height);}
    }
    public void DrawRoundedRectangle(Brush? brush,Pen? pen,Rect rectangle,double radiusX,double radiusY)
    {
        var bounds=Bounds(rectangle);if(bounds.Width<=0||bounds.Height<=0)return;var width=Math.Min(bounds.Width,Math.Max(0,(float)radiusX*2));var height=Math.Min(bounds.Height,Math.Max(0,(float)radiusY*2));
        if(width==0||height==0){DrawRectangle(brush,pen,rectangle);return;}
        using var path=new Gdi2D.GraphicsPath();path.AddArc(bounds.Left,bounds.Top,width,height,180,90);path.AddArc(bounds.Right-width,bounds.Top,width,height,270,90);path.AddArc(bounds.Right-width,bounds.Bottom-height,width,height,0,90);path.AddArc(bounds.Left,bounds.Bottom-height,width,height,90,90);path.CloseFigure();
        if(brush is not null){using var fill=Fill(brush,bounds);drawing.FillPath(fill,path);}if(pen is not null){using var stroke=Stroke(pen);drawing.DrawPath(stroke,path);}
    }
    public void DrawLine(Pen pen,Point start,Point end)
    {
        using var stroke=Stroke(pen);drawing.DrawLine(stroke,(float)start.X,(float)start.Y,(float)end.X,(float)end.Y);
    }
    public void Text(string text,double x,double y,double size,Brush brush,double maxWidth=900,bool singleLine=false,double? maxHeight=null)
    {
        using var font=new Gdi.Font("Microsoft YaHei UI",(float)size,Gdi.FontStyle.Regular,Gdi.GraphicsUnit.Pixel);
        using var fill=Fill(brush,new Gdi.RectangleF((float)x,(float)y,(float)maxWidth,(float)(size*2.8)));
        using var format=(Gdi.StringFormat)Gdi.StringFormat.GenericTypographic.Clone();format.Trimming=Gdi.StringTrimming.EllipsisCharacter;format.FormatFlags|=Gdi.StringFormatFlags.LineLimit;
        if(singleLine)format.FormatFlags|=Gdi.StringFormatFlags.NoWrap;
        drawing.DrawString(text,font,fill,new Gdi.RectangleF((float)x,(float)y,(float)maxWidth,(float)(maxHeight??size*2.8)),format);
    }
    public void Heading(string text,double x,double y,double size,Brush brush,double maxWidth=900)
    {
        using var font=new Gdi.Font("Microsoft YaHei UI",(float)size,Gdi.FontStyle.Regular,Gdi.GraphicsUnit.Pixel);using var format=(Gdi.StringFormat)Gdi.StringFormat.GenericTypographic.Clone();format.FormatFlags|=Gdi.StringFormatFlags.NoWrap;
        var measured=drawing.MeasureString(text,font,new Gdi.SizeF(100000,1000),format).Width;var fitted=Math.Max(32,size*Math.Min(1,maxWidth/Math.Max(1,measured)));Text(text,x,y,fitted,brush,maxWidth,true,115);
    }
    public BitmapSource Snapshot()
    {
        drawing.Flush();var region=new Gdi.Rectangle(0,0,bitmap.Width,bitmap.Height);var data=bitmap.LockBits(region,GdiImaging.ImageLockMode.ReadOnly,GdiImaging.PixelFormat.Format32bppPArgb);
        try
        {
            var stride=bitmap.Width*4;var pixels=new byte[stride*bitmap.Height];
            for(var y=0;y<bitmap.Height;y++)Marshal.Copy(data.Scan0+y*data.Stride,pixels,y*stride,stride);
            var result=BitmapSource.Create(bitmap.Width,bitmap.Height,96,96,PixelFormats.Pbgra32,null,pixels,stride);result.Freeze();return result;
        }
        finally{bitmap.UnlockBits(data);}
    }
    private static Gdi.RectangleF Bounds(Rect rectangle)=>new((float)rectangle.X,(float)rectangle.Y,(float)rectangle.Width,(float)rectangle.Height);
    private static Gdi.Color Color(System.Windows.Media.Color color,double opacity=1)=>Gdi.Color.FromArgb((int)Math.Round(color.A*Math.Clamp(opacity,0,1)),color.R,color.G,color.B);
    private static Gdi.Brush Fill(Brush brush,Gdi.RectangleF bounds)
    {
        if(brush is SolidColorBrush solid)return new Gdi.SolidBrush(Color(solid.Color,solid.Opacity));
        if(brush is LinearGradientBrush gradient&&gradient.GradientStops.Count>=2)
        {
            var stops=gradient.GradientStops.OrderBy(s=>s.Offset).ToArray();var angle=(float)(Math.Atan2(gradient.EndPoint.Y-gradient.StartPoint.Y,gradient.EndPoint.X-gradient.StartPoint.X)*180/Math.PI);
            var fill=new Gdi2D.LinearGradientBrush(bounds,Color(stops[0].Color,gradient.Opacity),Color(stops[^1].Color,gradient.Opacity),angle);
            fill.InterpolationColors=new Gdi2D.ColorBlend(stops.Length){Colors=stops.Select(s=>Color(s.Color,gradient.Opacity)).ToArray(),Positions=stops.Select(s=>(float)Math.Clamp(s.Offset,0,1)).ToArray()};return fill;
        }
        throw new ArgumentException("Report drawing supports solid and linear gradient brushes.",nameof(brush));
    }
    private static Gdi.Pen Stroke(Pen pen)=>pen.Brush is SolidColorBrush solid?new Gdi.Pen(Color(solid.Color,solid.Opacity),(float)pen.Thickness):throw new ArgumentException("Report strokes require a solid brush.",nameof(pen));
    public void Dispose(){drawing.Dispose();bitmap.Dispose();}
}

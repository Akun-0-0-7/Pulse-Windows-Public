namespace Pulse.Desktop;
public static class BotRenderer
{
    public static readonly string[] Bodies=["圆角","方形","圆形","椭圆","胶囊","高方形","宽方形","三角","菱形","六边形","八边形","梯形","拱形","盾形","水滴","云形","双圆","星形"];
    public static readonly string[] Personalities=["平静","活泼","专注","好奇","沉稳","慵懒","俏皮","热情"];
    public static void Draw(DrawingContext drawing,Point center,Brush brush,string state,int shape,int personality)
    {
        var working=state is "working" or "fetching";var phase=DateTimeOffset.Now.ToUnixTimeMilliseconds()/300d;var bounce=working?Math.Sin(phase*(1+personality*.12))*(personality is 1 or 7?3:1.5):0;center.Y+=bounce;double width=shape switch{4 or 6=>22,5=>14,_=>18},height=shape switch{5=>22,3 or 6=>13,_=>17};var rect=new Rect(center.X-width/2,center.Y-height/2,width,height);
        if(shape is 2 or 3)drawing.DrawEllipse(brush,null,center,width/2,height/2);
        else if(shape is 0 or 1 or 4 or 5 or 6)drawing.DrawRoundedRectangle(brush,null,rect,shape==1?1:shape==4?height/2:5,shape==1?1:shape==4?height/2:5);
        else if(shape==16){drawing.DrawEllipse(brush,null,new Point(center.X-5,center.Y),7,9);drawing.DrawEllipse(brush,null,new Point(center.X+5,center.Y),7,9);}
        else if(shape==15){for(int i=0;i<3;i++)drawing.DrawEllipse(brush,null,new Point(center.X-6+i*6,center.Y+(i==1?-3:1)),7,7);drawing.DrawRoundedRectangle(brush,null,new Rect(center.X-11,center.Y,22,7),3,3);}
        else {int count=shape switch{7=>3,8=>4,9=>6,10=>8,17=>10,_=>5};var geometry=new StreamGeometry();using(var path=geometry.Open()){for(int i=0;i<count;i++){var angle=-Math.PI/2+2*Math.PI*i/count;var radius=shape==17&&i%2==1?6:12;var point=new Point(center.X+Math.Cos(angle)*radius,center.Y+Math.Sin(angle)*radius);if(i==0)path.BeginFigure(point,true,true);else path.LineTo(point,true,false);}}drawing.DrawGeometry(brush,null,geometry);}
        var eyes=Ui.Bg;var eyeY=center.Y-1;var eyeHeight=state=="quiet"&&personality==5?1:2;var eyePen=new Pen(eyes,1.4);foreach(var x in new[]{center.X-4,center.X+4}){if(state=="spent"){drawing.DrawLine(eyePen,new Point(x-1.5,eyeY-1.5),new Point(x+1.5,eyeY+1.5));drawing.DrawLine(eyePen,new Point(x-1.5,eyeY+1.5),new Point(x+1.5,eyeY-1.5));}else drawing.DrawEllipse(eyes,null,new Point(x,eyeY),1.5,eyeHeight);}
        if(personality is 3 or 6)drawing.DrawEllipse(eyes,null,new Point(center.X,center.Y+4),1.3,1.1);else drawing.DrawLine(eyePen,new Point(center.X-2,center.Y+4),new Point(center.X+2,center.Y+4));
    }
}

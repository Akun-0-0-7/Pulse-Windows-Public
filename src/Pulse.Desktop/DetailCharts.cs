using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Pulse.Desktop;

public static class UsageBar
{
    public static double[] FillFractions(double percent,int segments=20)
    {
        if(segments<1||segments>100)throw new ArgumentOutOfRangeException(nameof(segments));
        var amount=double.IsFinite(percent)?Math.Clamp(percent,0,100)/100*segments:0;
        return Enumerable.Range(0,segments).Select(i=>Math.Clamp(amount-i,0,1)).ToArray();
    }
    public static FrameworkElement Create(double percent,bool segmented)
    {
        var foreground=percent>=90?Ui.Brush("#FF786C"):Ui.Accent;
        if(!segmented)return new ProgressBar{Value=double.IsFinite(percent)?Math.Clamp(percent,0,100):0,Maximum=100,Height=5,Foreground=foreground,Background=Ui.BorderBrush,Margin=new Thickness(0,0,0,7)};
        var row=new Grid{Height=6,Margin=new Thickness(0,0,0,7)};
        var fills=FillFractions(percent);
        for(var i=0;i<fills.Length;i++)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var cell=new Grid{Margin=new Thickness(0,0,i<fills.Length-1?2:0,0)};
            cell.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(fills[i],GridUnitType.Star)});
            cell.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1-fills[i],GridUnitType.Star)});
            cell.Background=Ui.BorderBrush;
            var filled=new Border{Background=foreground};Grid.SetColumn(filled,0);cell.Children.Add(filled);
            Grid.SetColumn(cell,i);row.Children.Add(cell);
        }
        return row;
    }
}

public sealed class MiniTrendChart(IReadOnlyList<MetricPoint> points,Brush? color=null) : FrameworkElement
{
    protected override Size MeasureOverride(Size availableSize)=>new(double.IsFinite(availableSize.Width)?availableSize.Width:310,64);
    protected override void OnRender(DrawingContext drawing)
    {
        var data=points.Where(p=>double.IsFinite(p.V)).OrderBy(p=>p.T).ToArray();
        if(data.Length==0||ActualWidth<8||ActualHeight<8)return;
        const double padding=3;var width=ActualWidth-2*padding;var height=ActualHeight-2*padding;
        var start=data[0].T.ToUnixTimeMilliseconds();var duration=Math.Max(1,data[^1].T.ToUnixTimeMilliseconds()-start);
        var min=data.Min(p=>p.V);var max=data.Max(p=>p.V);var spread=max-min;
        Point Position(MetricPoint point)=>new(padding+(point.T.ToUnixTimeMilliseconds()-start)/(double)duration*width,padding+(spread<0.0000001?.5:1-(point.V-min)/spread)*height);
        drawing.DrawLine(new Pen(Ui.BorderBrush,1),new Point(padding,ActualHeight-padding),new Point(ActualWidth-padding,ActualHeight-padding));
        var line=color??Ui.Accent;
        if(data.Length==1){drawing.DrawEllipse(line,null,new Point(ActualWidth/2,ActualHeight/2),2.5,2.5);return;}
        var geometry=new StreamGeometry();using(var context=geometry.Open()){context.BeginFigure(Position(data[0]),false,false);foreach(var point in data.Skip(1))context.LineTo(Position(point),true,false);}geometry.Freeze();drawing.DrawGeometry(null,new Pen(line,1.75){LineJoin=PenLineJoin.Round},geometry);
    }
}

public static class DetailCharts
{
    private static readonly HashSet<string> pending=[];
    public static event Action<string>? CodexUpdated;
    public static void AddHistory(StackPanel panel,PulseApp app,AccountConfig account,UsageReading reading)
    {
        if(!account.Detailed)return;
        var charts=new List<(string Label,string? Unit,IReadOnlyList<MetricPoint> Points)>();
        foreach(var metric in reading.Metrics.Where(m=>m.History is {Count:>0}).Take(2))charts.Add((metric.Label,metric.Unit,metric.History!));
        foreach(var window in reading.Windows.OrderByDescending(w=>w.Id==account.PrimaryWindowId))
        {
            if(charts.Count>=2)break;var points=app.History.Read(account.Id,window.Id);if(points.Count>0)charts.Add((Ui.WindowName(window),"%",points));
        }
        if(charts.Count==0){panel.Children.Add(Ui.Label("开启详细卡片后，将在读取额度时积累本地趋势。",11,Ui.Muted));return;}
        foreach(var chart in charts)
        {
            var points=chart.Points.Where(p=>double.IsFinite(p.V)).OrderBy(p=>p.T).ToArray();if(points.Length==0)continue;
            panel.Children.Add(Ui.Label(chart.Label+" · "+Localization.Translate("历史趋势"),12,Ui.Muted));
            panel.Children.Add(new MiniTrendChart(points){Margin=new Thickness(0,0,0,3)});
            var range=points.Min(p=>p.V).ToString("0.##",Localization.Culture)+"–"+points.Max(p=>p.V).ToString("0.##",Localization.Culture)+" "+chart.Unit;
            panel.Children.Add(Ui.Label($"{points[0].T.ToLocalTime():MM-dd HH:mm} → {points[^1].T.ToLocalTime():MM-dd HH:mm} · {range}",10,Ui.Muted));
        }
    }
    public static bool IsPrimaryCodex(PulseApp app,AccountConfig account)=>account.ProviderId=="codex"&&app.UsesLocalLogin(account)&&app.Engine.Config.Accounts.FirstOrDefault(a=>a.Enabled&&a.ProviderId=="codex"&&a.Extra.GetValueOrDefault("isAdditionalAccount")!="true")?.Id==account.Id;
    public static void AddCodex(StackPanel panel,PulseApp app,AccountConfig account)
    {
        if(!account.Detailed||!IsPrimaryCodex(app,account))return;
        var key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(account.Id)))[..20];var file="codex-details-"+key+".json";
        var details=new StackPanel();var cached=app.Storage.Load<CodexAccountDetails>(file);if(cached is not null)Present(cached,details);
        var status=Ui.Label("仅在点击后，通过本机 Codex 工具读取账号历史与重置积分。",11,Ui.Muted);
        var button=Ui.Button(pending.Contains(key)?"正在读取 Codex 历史…":"读取 Codex 账号历史",()=>{});button.IsEnabled=!pending.Contains(key);
        button.Click+=async(_,_)=>
        {
            if(!IsPrimaryCodex(app,account)||!pending.Add(key))return;button.IsEnabled=false;status.Text=Localization.Translate("正在读取 Codex 历史…");
            try
            {
                var result=await CodexDetailsService.FetchAsync(account.Extra.GetValueOrDefault("executable"),app.Engine.LifetimeToken,ProxyEnvironment.For(app.Engine.Config));
                if(result.Error is {} error){status.Text=Localization.Translate(error);return;}
                if(!IsPrimaryCodex(app,account))return;app.Storage.Save(file,result);Present(result,details);status.Text=Localization.Translate("已读取本机 Codex 登录账号的历史。");
            }
            catch(OperationCanceledException){status.Text=Localization.Translate("读取已取消");}
            catch(Exception exception)when(exception is JsonException or IOException or InvalidOperationException){status.Text=Localization.Translate("Codex 工具未提供账号历史或重置积分");}
            finally{pending.Remove(key);button.IsEnabled=true;button.Content=Localization.Translate("读取 Codex 账号历史");CodexUpdated?.Invoke(account.Id);}
        };
        panel.Children.Add(details);panel.Children.Add(button);panel.Children.Add(status);
    }
    private static void Present(CodexAccountDetails value,StackPanel panel)
    {
        panel.Children.Clear();panel.Children.Add(Ui.Label("Codex 账号历史",13,Ui.Accent));
        if(value.LifetimeTokens is {} tokens)panel.Children.Add(Ui.Label("累计 Token  "+Ui.Compact(tokens),13));
        if(value.ResetCredits is {} credits)panel.Children.Add(Ui.Label("可用额度重置积分  "+credits.ToString(CultureInfo.InvariantCulture),13));
        if(value.NextExpiry is {} expires)panel.Children.Add(Ui.Label("最近积分到期  "+expires.ToLocalTime().ToString("yyyy-MM-dd HH:mm",Localization.Culture),11,Ui.Muted));
        if(value.Days.Count>0){panel.Children.Add(Ui.Label("每日 Token",12,Ui.Muted));panel.Children.Add(new MiniTrendChart(value.Days.Select(d=>new MetricPoint(d.At,d.Tokens)).ToArray()));}
        if(value.Days.Count==0&&value.LifetimeTokens is null&&value.ResetCredits is null)panel.Children.Add(Ui.Label("本机 Codex 工具没有返回历史数据。",11,Ui.Muted));
    }
}

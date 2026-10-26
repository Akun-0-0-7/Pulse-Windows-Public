using System.Windows.Media.Imaging;
namespace Pulse.Desktop;
public sealed class ReportWindow : Window
{
    private readonly PulseApp app;private readonly SpendReport report;private readonly SpendReport previousReport;private readonly string period;private readonly DateTimeOffset referenceDate;private readonly Image preview=new(){Width=324,Height=576,Stretch=Stretch.Uniform};private int selected;
    private static readonly string[] Titles=["这一段，做了多少","每天都有记录","模型的分工","一起工作的工具","你的工作节奏","项目的足迹","持续创造","订阅价值"];
    public ReportWindow(PulseApp app,SpendReport all,string period,DateTimeOffset? referenceDate=null)
    {
        this.app=app;this.period=period;this.referenceDate=referenceDate??DateTimeOffset.Now;var span=ReportPeriods.Range(period,this.referenceDate);report=all.InRange(span.Start,span.End);var previousSpan=ReportPeriods.Range(period,span.Start.AddSeconds(-1));previousReport=all.InRange(previousSpan.Start,previousSpan.End);Title=Localization.Translate("Pulse · 用量报告");Width=570;Height=800;Background=Ui.Bg;WindowStartupLocation=WindowStartupLocation.CenterScreen;var root=new StackPanel{Margin=new Thickness(24)};
        var available=Enumerable.Range(0,Titles.Length).Where(i=>Available(report,i,app.Engine.Config)).ToArray();
        if(available.Length==0){root.Children.Add(Ui.Label("选定期间没有可生成的报告卡片。",16,Ui.Muted));selected=0;}
        else{selected=available[0];var choices=available.Select(i=>Titles[i]).ToArray();root.Children.Add(Ui.Select(choices,Titles[selected],title=>{selected=Array.IndexOf(Titles,title);Render();}));}
        root.Children.Add(preview);var save=Ui.Button("保存图片",Save);var copy=Ui.Button("复制图片",()=>Clipboard.SetImage(Bitmap()));var share=Ui.Button("分享图片",Share);var allButton=Ui.Button("保存全部",SaveAll);save.IsEnabled=copy.IsEnabled=share.IsEnabled=allButton.IsEnabled=available.Length>0;root.Children.Add(Ui.Row(save,copy,share,allButton));root.Children.Add(Ui.Label("1080 × 1920 · 金额为公开 API 价格估算",11,Ui.Muted));Content=new ScrollViewer{Content=root};Render();
    }
    private void Render()=>preview.Source=Bitmap();
    public BitmapSource Bitmap()=>RenderCard(report,period,selected,app.Engine.Config,referenceDate,SpendPresentation.Rate(app.Engine.Config,app.Storage),previousReport);
    private void Save(){var dialog=new Microsoft.Win32.SaveFileDialog{Filter="PNG 图片|*.png",FileName=$"Pulse-{period}-{selected+1}.png"};if(dialog.ShowDialog(this)==true)Write(Bitmap(),dialog.FileName);}
    private void Share(){var file=Path.Combine(Path.GetTempPath(),$"Pulse-{period}-{DateTime.Now:yyyyMMdd-HHmmss}.png");Write(Bitmap(),file);var files=new System.Collections.Specialized.StringCollection();files.Add(file);System.Windows.Forms.Clipboard.SetFileDropList(files);MessageBox.Show("图片文件已放入剪贴板，可粘贴到分享应用。\n"+file,"Pulse");}
    private void SaveAll(){using var dialog=new System.Windows.Forms.FolderBrowserDialog{Description=Localization.Translate("选择报告图片保存目录"),UseDescriptionForTitle=true};if(dialog.ShowDialog()!=System.Windows.Forms.DialogResult.OK)return;var indexes=Enumerable.Range(0,Titles.Length).Where(i=>Available(report,i,app.Engine.Config)).ToArray();foreach(var index in indexes)Write(RenderCard(report,period,index,app.Engine.Config,referenceDate,SpendPresentation.Rate(app.Engine.Config,app.Storage),previousReport),Path.Combine(dialog.SelectedPath,$"Pulse-{period}-{index+1}.png"));MessageBox.Show($"{indexes.Length} 张有数据的报告卡片已保存。","Pulse");}
    public static void Write(BitmapSource bitmap,string file){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(file);encoder.Save(output);}
    public static bool Available(SpendReport report,int template,AppConfig config)=>template switch
    {
        0=>report.Records.Count>0,
        1=>report.ByDay.Count>0,
        2=>report.ByModel.Count>0,
        3=>report.ByAgent.Count>0,
        4=>report.Records.Any(r=>!r.IsAggregate&&r.TotalTokens is >0),
        5=>report.Records.Any(r=>!string.IsNullOrWhiteSpace(r.Project)),
        6=>report.Records.Count>0,
        7=>report.EstimatedCost.HasValue&&config.MonthlySubscriptionUsd is >0m,
        _=>false
    };
    public static BitmapSource RenderCard(SpendReport report,string period,int template,AppConfig config,DateTimeOffset? referenceDate=null,decimal? rate=null,SpendReport? priorReport=null)
    {
        var span=ReportPeriods.Range(period,referenceDate??DateTimeOffset.Now);
        using var draw=new ReportCanvas(1080,1920);{
            var gradient=new LinearGradientBrush(Ui.Brush("#25253C").Color,Ui.Brush("#101318").Color,90);draw.DrawRectangle(gradient,null,new Rect(0,0,1080,1920));var soft=Ui.Brush("#B5ADFF");var muted=Ui.Brush("#A6ADC3");
            void Text(string text,double x,double y,double size,Brush? brush=null)=>draw.Text(Localization.Translate(text),x,y,size,brush??Ui.Brush("#F1F2F6"));
            Text("PULSE  /  YOUR AI IN NUMBERS",90,88,24,soft);Text(span.Start.ToString(period switch{"year"=>"yyyy 年","month"=>"yyyy 年 MM 月",_=>"yyyy 年 MM 月 dd 日"}),90,145,35,muted);draw.Heading(Localization.Translate(Titles[Math.Clamp(template,0,7)]),90,240,65,Ui.Brush("#F1F2F6"));draw.DrawLine(new Pen(Ui.Brush("#43465C"),2),new Point(90,365),new Point(990,365));
            if(report.Records.Count==0){Text("本期没有使用记录",90,540,52,muted);Text("选择其他日期或重新扫描本机记录",90,640,27,muted);}
            void Bars(IEnumerable<(string Name,long? Number)> input,double y){var list=input.Take(10).ToList();if(list.Count==0){Text("本期没有对应记录",90,y,34,muted);return;}double max=Math.Max(1,list.Max(x=>x.Number??0));foreach(var item in list){Text(item.Name,90,y,24,muted);Text(Ui.Compact(item.Number),780,y,28);draw.DrawRoundedRectangle(Ui.Brush("#303347"),null,new Rect(90,y+48,900,15),7,7);if(item.Number is long n && n>0)draw.DrawRoundedRectangle(soft,null,new Rect(90,y+48,Math.Max(2,900*n/max),15),7,7);y+=110;}}
            if(report.Records.Count>0)switch(template){
                case 0:Text(Ui.Compact(report.TotalTokens),90,470,145,soft);Text("TOKEN",95,655,30,muted);Text(SpendPresentation.Money(report.EstimatedCost,config,rate),90,820,85);Text("公开 API 价格估算",95,955,25,muted);if(report.ReportedCost is {} original)Text($"来源报告费用：{original:0.##} USD",95,1040,28,muted);Text($"{report.Records.Count:N0} 条使用记录",90,1160,42);Text($"{report.ByModel.Count} 个模型  ·  {report.ByAgent.Count} 个客户端",90,1240,32,muted);if(priorReport?.TotalTokens is >0&&report.TotalTokens is {} currentTokens)Text($"相比上期 Token：{(currentTokens-priorReport.TotalTokens.Value)/(double)priorReport.TotalTokens.Value:+0.#%;-0.#%;0%}",90,1390,30,muted);if(priorReport?.EstimatedCost is >0&&report.EstimatedCost is {} currentCost)Text($"相比上期估算费用：{(currentCost-priorReport.EstimatedCost.Value)/priorReport.EstimatedCost.Value:+0.#%;-0.#%;0%}",90,1460,30,muted);break;
                case 1:{var days=report.ByDay.OrderBy(p=>p.Key).ToList();var max=days.Count==0?1:Math.Max(1,days.Max(p=>p.Value.TotalTokens??0));var count=Math.Min(days.Count,366);var step=Math.Min(47,1120d/Math.Max(1,(count+13)/14));for(int i=0;i<count;i++){var column=i%14;var row=i/14;var value=days[i].Value.TotalTokens;var brush=value is null?Ui.BorderBrush:new SolidColorBrush(Color.FromArgb((byte)(60+195*(value.Value/(double)max)),181,173,255));draw.DrawRoundedRectangle(brush,null,new Rect(90+column*64,460+row*step,48,Math.Min(34,step-6)),7,7);}Text($"{days.Count} 个有记录的日子",90,1640,34,muted);break;}
                case 2:Bars(report.ByModel.OrderByDescending(p=>p.Value.TotalTokens).Select(p=>(p.Key,p.Value.TotalTokens)),450);break;
                case 3:Bars(report.ByAgent.OrderByDescending(p=>p.Value.TotalTokens).Select(p=>(p.Key,p.Value.TotalTokens)),450);break;
                case 4:{var hours=report.Records.Where(r=>!r.IsAggregate&&r.TotalTokens is >0).GroupBy(r=>r.At.ToLocalTime().Hour).ToDictionary(g=>g.Key,g=>g.Sum(r=>r.TotalTokens??0));if(hours.Count==0){Text("没有可统计的 Token 记录",90,540,34,muted);break;}var max=Math.Max(1,hours.Values.Max());for(int hour=0;hour<24;hour++){if(hours.TryGetValue(hour,out var amount)){var height=800*amount/(double)max;draw.DrawRoundedRectangle(soft,null,new Rect(90+hour*37,1350-height,24,Math.Max(2,height)),5,5);}if(hour%4==0)Text(hour.ToString("00"),90+hour*37,1380,21,muted);}Text("一天中的使用时间 · 本机时区",90,1550,30,muted);break;}
                case 5:{var projects=report.Records.Where(r=>!string.IsNullOrWhiteSpace(r.Project)).GroupBy(r=>r.Project!).OrderByDescending(g=>g.Sum(r=>r.TotalTokens??0)).Select((g,i)=>(config.HideProjectNames?$"项目 {i+1}":Path.GetFileName(g.Key.TrimEnd('\\','/')),g.Any(r=>r.ReportsTokens)?(long?)g.Sum(r=>r.TotalTokens??0):null)).ToList();if(projects.Count==0)Text("记录未提供项目字段",90,540,36,muted);else Bars(projects,450);break;}
                case 6:{var days=report.Records.Select(r=>r.At.ToLocalTime().Date).Distinct().OrderBy(d=>d).ToList();int longest=0,streak=0;DateTime? previous=null;foreach(var day in days){streak=previous?.AddDays(1)==day?streak+1:1;longest=Math.Max(longest,streak);previous=day;}Text(longest.ToString(),90,490,200,soft);Text("天 · 最长连续使用",90,760,40,muted);Text(days.Count.ToString(),90,1000,150);Text("个有记录的日子",90,1210,35,muted);break;}
                default:{var subscription=SpendPresentation.SubscriptionUsd(config,span);if(report.EstimatedCost is not {} estimated||subscription<=0){Text("缺少价格或订阅月费",90,540,36,muted);break;}Text(SpendPresentation.Money(estimated,config,rate),90,520,105,soft);Text("公开 API 等值（估算）",90,680,30,muted);Text("同期订阅费用："+SpendPresentation.Money(subscription,config,rate),90,850,43);Text($"等值 / 订阅费用：{estimated/subscription:0.##} 倍",90,970,43);Text($"差额：{SpendPresentation.Money(estimated-subscription,config,rate)}",90,1090,40);Text("按所选日期的实际天数折算订阅月费",90,1300,27,muted);break;}
            }
            if(report.UnpricedTokens>0)Text($"未定价 Token：{Ui.Compact(report.UnpricedTokens)}",90,1620,24,muted);if(report.Records.Any(r=>r.IsPartial))Text("含部分计数记录",90,1680,24,muted);Text("本机记录 · "+(config.HideProjectNames?"项目名称已隐藏":"保留项目名称"),90,1750,23,muted);Text("Created with Pulse Windows",90,1810,24,soft);
        }
        return draw.Snapshot();
    }
}

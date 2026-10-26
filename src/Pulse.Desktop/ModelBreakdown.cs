namespace Pulse.Desktop;
public sealed partial class SettingsWindow
{
    private void ModelBreakdown(SpendReport all)
    {
        if(spendModel is null)return;
        var report=new SpendReport{Records=all.Records.Where(r=>r.Model==spendModel).ToList()};var records=report.Records.Where(r=>r.ReportsTokens).ToList();
        var content=new StackPanel();content.Children.Add(Ui.Label("模型："+spendModel,18));content.Children.Add(Ui.Label($"输入 {Ui.Compact(records.Count>0?records.Sum(r=>r.Input):null)} · 输出 {Ui.Compact(records.Count>0?records.Sum(r=>r.Output):null)}",13));content.Children.Add(Ui.Label($"缓存读取 {Ui.Compact(records.Count>0?records.Sum(r=>r.CacheRead):null)} · 缓存写入 {Ui.Compact(records.Count>0?records.Sum(r=>r.CacheWrite):null)}",13));content.Children.Add(Ui.Label("费用估算："+SpendPresentation.Money(report.EstimatedCost,app.Engine.Config,SpendPresentation.Rate(app.Engine.Config,app.Storage)),13));page.Children.Add(Ui.Box(content));
        SectionChart("模型每日消耗",report.ByDay.OrderBy(p=>p.Key).Select(p=>(p.Key,p.Value.TotalTokens)));
        var hours=records.Where(r=>!r.IsAggregate).GroupBy(r=>r.At.ToLocalTime().Hour).OrderBy(g=>g.Key).Select(g=>(g.Key.ToString("00")+":00",(long?)g.Sum(r=>r.TotalTokens??0))).ToList();if(hours.Count>0)SectionChart("模型小时分布",hours);else page.Children.Add(Ui.Label("来源没有可定位到小时的请求记录。",12,Ui.Muted));
        SectionChart("模型按客户端分布",report.ByAgent.OrderByDescending(p=>p.Value.TotalTokens).Select(p=>(p.Key,p.Value.TotalTokens)));
    }
}

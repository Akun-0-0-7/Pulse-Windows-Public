using System.Windows.Controls.Primitives;
namespace Pulse.Desktop;
/// <summary>Primary-account local ledger and prompt-cache summaries; no network requests.</summary>
public static class LocalUsageCard
{
    public static FrameworkElement? Create(PulseApp app,AccountConfig account)
    {
        if(!app.Engine.Config.SpendEnabled||!app.UsesLocalLogin(account))return null;
        var panel=new StackPanel{Margin=new Thickness(0,10,0,0)};var summary=app.LocalSpend is {} report?LocalUsageSummary.Build(report,account.ProviderId,DateTimeOffset.Now):null;
        if(summary is not null){panel.Children.Add(Ui.Label("本机最近用量",14));panel.Children.Add(Ui.Label($"今日 {Ui.Compact(summary.Today)} · 7 天 {Ui.Compact(summary.SevenDays)} · 31 天 {Ui.Compact(summary.Month)} Token",12,Ui.Muted));
            var days=new UniformGrid{Columns=31,Height=42,Margin=new Thickness(0,5,0,7)};var maximum=summary.Days.Select(d=>d.V).DefaultIfEmpty(0).Max();for(var date=DateTime.Today.AddDays(-30);date<=DateTime.Today;date=date.AddDays(1)){var point=summary.Days.FirstOrDefault(p=>p.T.LocalDateTime.Date==date);days.Children.Add(new Border{Height=point is null?2:Math.Max(2,40*point.V/Math.Max(1,maximum)),VerticalAlignment=VerticalAlignment.Bottom,Background=point is null?Ui.BorderBrush:Ui.Accent,Margin=new Thickness(1,0,1,0),ToolTip=$"{date:MM-dd} · {Ui.Compact(point is null?null:(long?)point.V)} Token"});}panel.Children.Add(days);
            if(summary.TopModel is {} model)panel.Children.Add(Ui.Label("主要模型："+model,12,Ui.Muted));if(summary.CacheHitRate is {} rate)panel.Children.Add(Ui.Label($"缓存命中率：{rate:P0}",12,Ui.Muted));
            panel.Children.Add(Ui.Label("31 天 API 费用估算："+SpendPresentation.Money(summary.Cost,app.Engine.Config,SpendPresentation.Rate(app.Engine.Config,app.Storage)),12,Ui.Muted));
            var reading=app.Engine.Reading(account.Id);if(reading is not null&&app.LocalSpend is {} ledger)foreach(var window in reading.Windows.Where(w=>!w.Estimated&&w.WindowSeconds>0&&w.ResetsAt is not null&&w.UsedPercent>0)){
                var records=ledger.Records.Where(r=>r.Agent==LocalUsageSummary.Source(account.ProviderId)&&r.At>=window.ResetsAt!.Value.AddSeconds(-window.WindowSeconds!.Value)&&r.At<=DateTimeOffset.Now).ToList();
                if(records.Count>0&&records.All(r=>r.EstimatedCost is not null)){var total=records.Sum(r=>r.EstimatedCost)!.Value;panel.Children.Add(Ui.Label($"{Ui.WindowName(window)} · 已用约 {SpendPresentation.Money(total,app.Engine.Config,SpendPresentation.Rate(app.Engine.Config,app.Storage))}",11,Ui.Muted));}
            }
        }
        var sessions=app.PromptCaches.GetValueOrDefault(account.ProviderId)?.Where(s=>s.ExpiresAt>DateTimeOffset.Now).ToList()??[];
        if(sessions.Count>0){panel.Children.Add(Ui.Label("提示词缓存",14));var first=sessions[0];panel.Children.Add(Ui.Label((first.IsMinimum?"至少可复用 ":"记录的缓存剩余 ")+$"{Math.Ceiling((first.ExpiresAt-DateTimeOffset.Now).TotalMinutes):0} 分钟 · {sessions.Count} 个会话",12,Ui.Muted));panel.Children.Add(Ui.Label(first.Session,11,Ui.Muted));panel.Children.Add(Ui.Button("查看缓存会话",()=>ShowSessions(app,account)));}
        return panel.Children.Count==0?null:panel;
    }
    public static void ShowSessions(PulseApp app,AccountConfig account)
    {
        var panel=new StackPanel{Margin=new Thickness(24)};panel.Children.Add(Ui.Title("提示词缓存会话"));panel.Children.Add(Ui.Label(account.ProviderId=="codex"?"根据本机 Codex 请求时间和模型读取。GPT-5.6 及以后模型显示至少 30 分钟可复用期；服务商可能保留更久。":"根据 Claude 回复记录的 5 分钟或 1 小时缓存层级读取。",12,Ui.Muted));
        foreach(var session in app.PromptCaches.GetValueOrDefault(account.ProviderId)??[]){var row=new StackPanel();row.Children.Add(Ui.Label(session.Session,13));row.Children.Add(Ui.Label($"最近请求 {session.LastRequest.ToLocalTime():HH:mm:ss} · {(session.IsMinimum?"至少至":"记录期限")}{session.ExpiresAt.ToLocalTime():HH:mm:ss}",12,Ui.Muted));panel.Children.Add(Ui.Box(row));}
        new Window{Title=Localization.Translate("Pulse · 提示词缓存"),Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto},Width=650,Height=520,Background=Ui.Bg,Foreground=Ui.Text,WindowStartupLocation=WindowStartupLocation.CenterScreen}.Show();
    }
}

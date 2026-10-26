using System.Text;
namespace Pulse.Desktop;
public sealed partial class SettingsWindow
{
    private SpendReport? spendReport;
    private CancellationTokenSource? scanning;
    private DateTime start=DateTime.Today.AddDays(-6),end=DateTime.Today;
    private int spendPage;
    private string spendSort="时间";
    private bool spendDescending=true;
    private string? spendModel;
    private string reportPeriod="month";
    private DateTime reportDate=DateTime.Today;
    public void ApplySpendReport(SpendReport report){spendReport=report;if(current is "spend" or "reports"&&Keyboard.FocusedElement is not TextBox)Navigate(current);else UpdateReadings();}
    private void Spend()
    {
        Header("Token 消耗","Token 是模型处理文本的计数单位。金额使用公开 API 价格估算，与订阅费用分开。来源不提供的计数和价格显示为“—”。");
        var config=app.Engine.Config;page.Children.Add(Ui.Check("读取本机消耗记录",config.SpendEnabled,value=>{config.SpendEnabled=value;app.Save();if(value)_=ScanSpend();else{scanning?.Cancel();spendReport=null;Navigate("spend");}}));
        var begin=new DatePicker {SelectedDate=start,Width=150,Margin=new Thickness(0,0,12,12)};var finish=new DatePicker {SelectedDate=end,Width=150,Margin=new Thickness(0,0,12,12)};begin.SelectedDateChanged+=(_,_)=>{start=begin.SelectedDate??start;};finish.SelectedDateChanged+=(_,_)=>{end=finish.SelectedDate??end;};page.Children.Add(Ui.Row(begin,finish,Ui.Button("应用范围",()=>Navigate("spend"))));
        page.Children.Add(Ui.Row(Ui.Button("重新扫描",()=>_=ScanSpend(),true),Ui.Button("取消扫描",()=>scanning?.Cancel()),Ui.Button("导出明细 CSV",ExportSpend)));
        SpendSourceSettings(config);
        spendReport??=app.Storage.Load<SpendReport>("spend-cache.json");if(!config.SpendEnabled){page.Children.Add(Ui.Label("开启后才会扫描各客户端的本机记录。",13,Ui.Muted));return;}
        if(spendReport is null){page.Children.Add(Ui.Label("点击“重新扫描”读取记录。",13,Ui.Muted));return;}
        var report=spendReport.InRange(new DateTimeOffset(start),new DateTimeOffset(end.AddDays(1)));var summary=new StackPanel();summary.Children.Add(Ui.Label(Ui.Compact(report.TotalTokens)+" Token",30,Ui.Accent));summary.Children.Add(Ui.Label("费用估算："+SpendPresentation.Money(report.EstimatedCost,config,SpendPresentation.Rate(config,app.Storage))));if(report.ReportedCost is {} reported)summary.Children.Add(Ui.Label($"来源报告的原始费用：{reported:0.##} USD",13,Ui.Muted));summary.Children.Add(Ui.Label($"{report.Records.Count:N0} 条记录 · 未取得价格的 Token：{Ui.Compact(report.UnpricedTokens)}",12,Ui.Muted));summary.Children.Add(Ui.Label($"部分计数记录：{report.Records.Count(r=>r.IsPartial)} · 会话累计记录：{report.Records.Count(r=>r.IsAggregate)}（累计记录不计入小时分布）",11,Ui.Muted));page.Children.Add(Ui.Box(summary));
        SectionChart("每日消耗",report.ByDay.OrderBy(p=>p.Key).Select(p=>(p.Key,p.Value.TotalTokens)));SectionChart("模型",report.ByModel.OrderByDescending(p=>p.Value.TotalTokens).Take(12).Select(p=>(p.Key,p.Value.TotalTokens)),true);SectionChart("客户端",report.ByAgent.OrderByDescending(p=>p.Value.TotalTokens).Select(p=>(p.Key,p.Value.TotalTokens)));
        ModelBreakdown(report);SpendDetails(report);
        foreach(var note in report.Notes.Take(30))page.Children.Add(Ui.Label(note,11,Ui.Muted));page.Children.Add(Ui.Button("打开来源覆盖说明",()=>WindowsServices.Open(Path.Combine(AppContext.BaseDirectory,"Docs","spend-status-implementation.md"))));
    }
    private void SpendSourceSettings(AppConfig config)
    {
        var enabled=config.EnabledSpendSources;
        var selected=enabled is null?SpendSources.All.Count:SpendSources.All.Count(s=>enabled.Contains(s.Id));
        var panel=new StackPanel();var sourceCount=Ui.Label($"本机来源：已选择 {selected}/{SpendSources.All.Count} 项",14);panel.Children.Add(sourceCount);
        panel.Children.Add(Ui.Row(Ui.Button("选择全部",()=>{config.EnabledSpendSources=null;app.Save();Navigate("spend");}),Ui.Button("全部关闭",()=>{config.EnabledSpendSources=[];app.Save();Navigate("spend");})));
        var items=new WrapPanel();foreach(var source in SpendSources.All){var id=source.Id;var check=Ui.Check(source.Name,enabled is null||enabled.Contains(id),value=>{var choices=config.EnabledSpendSources is null?SpendSources.All.Select(s=>s.Id).ToList():[..config.EnabledSpendSources];choices.Remove(id);if(value)choices.Add(id);config.EnabledSpendSources=choices;sourceCount.Text=$"本机来源：已选择 {choices.Count}/{SpendSources.All.Count} 项";app.Save();});check.ToolTip=id;items.Children.Add(check);}
        panel.Children.Add(new Expander{Header="选择读取来源",Content=new ScrollViewer{Content=items,MaxHeight=260},Foreground=Ui.Text});
        panel.Children.Add(Ui.Label("导入目录内按来源编号建立子目录，例如 codex。扫描时读取所选来源的子目录。",12,Ui.Muted));
        panel.Children.Add(Ui.Button("添加导入目录",()=>{using var dialog=new System.Windows.Forms.FolderBrowserDialog{Description=Localization.Translate("选择包含来源子目录的导入根目录"),UseDescriptionForTitle=true};if(dialog.ShowDialog()!=System.Windows.Forms.DialogResult.OK)return;if(!config.SpendPaths.Contains(dialog.SelectedPath,StringComparer.OrdinalIgnoreCase)){config.SpendPaths.Add(dialog.SelectedPath);app.Save();Navigate("spend");}}));
        foreach(var path in config.SpendPaths.ToList()){var local=path;panel.Children.Add(Ui.Row(Ui.Label(local,12,Ui.Muted),Ui.Button("打开",()=>WindowsServices.Open(local)),Ui.Button("移除",()=>{config.SpendPaths.Remove(local);app.Save();Navigate("spend");})));}
        page.Children.Add(Ui.Box(panel));
    }
    private void SpendDetails(SpendReport report)
    {
        var models=new[]{"全部模型"}.Concat(report.ByModel.Keys.OrderBy(x=>x,StringComparer.OrdinalIgnoreCase)).ToArray();
        page.Children.Add(Ui.Label("模型筛选",13,Ui.Muted));
        page.Children.Add(Ui.Select(models,spendModel??"全部模型",value=>{spendModel=value=="全部模型"?null:value;spendPage=0;Navigate("spend");}));
        var rows=spendModel is null?report.Records:report.Records.Where(r=>r.Model==spendModel).ToList();
        if(spendModel is not null)page.Children.Add(Ui.Row(Ui.Label("模型："+spendModel,14),Ui.Button("显示全部模型",()=>{spendModel=null;spendPage=0;Navigate("spend");})));
        page.Children.Add(Ui.Label("明细排序",13,Ui.Muted));
        page.Children.Add(Ui.Row(Ui.Select(["时间","客户端","模型","Token","估算美元"],spendSort,value=>{spendSort=value;spendPage=0;Navigate("spend");}),Ui.Button(spendDescending?"降序 ↓":"升序 ↑",()=>{spendDescending=!spendDescending;spendPage=0;Navigate("spend");})));
        var ordered=spendSort switch{"客户端"=>rows.OrderBy(r=>r.Agent,StringComparer.OrdinalIgnoreCase),"模型"=>rows.OrderBy(r=>r.Model,StringComparer.OrdinalIgnoreCase),"Token"=>rows.OrderBy(r=>r.TotalTokens),"估算美元"=>rows.OrderBy(r=>r.EstimatedCost),_=>rows.OrderBy(r=>r.At)};
        var sorted=spendDescending?ordered.Reverse().ToList():ordered.ToList();const int pageSize=100;var count=Math.Max(1,(sorted.Count+pageSize-1)/pageSize);spendPage=Math.Clamp(spendPage,0,count-1);
        page.Children.Add(Ui.Label($"明细：{sorted.Count:N0} 条 · 第 {spendPage+1}/{count} 页，每页最多 {pageSize} 条",12,Ui.Muted));
        var table=new DataGrid{ItemsSource=sorted.Skip(spendPage*pageSize).Take(pageSize).Select(r=>new{时间=r.At.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),客户端=r.Agent,模型=r.Model,输入=r.ReportsTokens?r.Input:(long?)null,输出=r.ReportsTokens?r.Output:(long?)null,缓存读取=r.ReportsTokens?r.CacheRead:(long?)null,Token=r.TotalTokens,估算美元=r.EstimatedCost,原始费用美元=r.ReportedCost,部分计数=r.IsPartial,会话累计=r.IsAggregate}).ToList(),AutoGenerateColumns=true,CanUserSortColumns=false,IsReadOnly=true,Height=300,Background=Ui.Card,Margin=new Thickness(0,0,0,8)};page.Children.Add(table);
        page.Children.Add(Ui.Row(Ui.Button("上一页",()=>{spendPage=Math.Max(0,spendPage-1);Navigate("spend");}),Ui.Button("下一页",()=>{spendPage=Math.Min(count-1,spendPage+1);Navigate("spend");})));
    }
    private void SectionChart(string title,IEnumerable<(string Label,long? Value)> rows,bool drillDown=false)
    {
        var values=rows.ToList();if(values.Count==0)return;var panel=new StackPanel();panel.Children.Add(Ui.Label(title,17));var maximum=Math.Max(1,values.Max(v=>v.Value??0));foreach(var row in values){if(drillDown){var model=row.Label;panel.Children.Add(Ui.Button(row.Label+"  "+Ui.Compact(row.Value),()=>{spendModel=model;spendPage=0;Navigate("spend");}));}else panel.Children.Add(Ui.Label(row.Label+"  "+Ui.Compact(row.Value),12,Ui.Muted));if(row.Value.HasValue)panel.Children.Add(new ProgressBar {Value=row.Value.Value/(double)maximum*100,Maximum=100,Height=6,Foreground=Ui.Accent,Background=Ui.Bg,Margin=new Thickness(0,0,0,13)});}page.Children.Add(Ui.Box(panel));
    }
    private async Task ScanSpend()
    {
        if(!app.Engine.Config.SpendEnabled)return;scanning?.Cancel();scanning=new();status.Text=Localization.Translate("正在扫描本机记录…");var token=scanning.Token;
        try{var report=await app.ScanSpendAsync(token);if(report is not null)spendReport=report;}
        catch(OperationCanceledException){status.Text="扫描已取消";}catch(Exception exception) when(exception is IOException or UnauthorizedAccessException){status.Text="部分记录无法读取，请检查目录权限。";}finally{UpdateStatus();}
    }
    private void ExportSpend()
    {
        if(spendReport is null)return;var dialog=new Microsoft.Win32.SaveFileDialog {Filter="CSV 文件|*.csv",FileName="Pulse-消耗明细.csv"};if(dialog.ShowDialog(this)!=true)return;
        var output=SpendCsv.Format(spendReport.InRange(new DateTimeOffset(start),new DateTimeOffset(end.AddDays(1))));File.WriteAllText(dialog.FileName,output,new UTF8Encoding(true));
    }
    private void Reports()
    {
        Header("用量报告","日、周、月、年卡片使用同一份本机消耗记录。导出为 1080 × 1920 像素图片。选定日期对应所在日、周、月或年。");var period=Ui.Select(["day","week","month","year"],reportPeriod,value=>reportPeriod=value);page.Children.Add(Ui.Label("报告范围",13,Ui.Muted));page.Children.Add(period);page.Children.Add(Ui.Label("选择日期",13,Ui.Muted));var date=new DatePicker{SelectedDate=reportDate,Width=180,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,0,0,12)};date.SelectedDateChanged+=(_,_)=>reportDate=date.SelectedDate??reportDate;page.Children.Add(date);AddCheck("隐藏项目名称",app.Engine.Config.HideProjectNames,value=>app.Engine.Config.HideProjectNames=value);
        AddField("每月订阅费用（美元；留空则不计算订阅价值）",app.Engine.Config.MonthlySubscriptionUsd?.ToString(System.Globalization.CultureInfo.InvariantCulture)??"",value=>app.Engine.Config.MonthlySubscriptionUsd=decimal.TryParse(value,System.Globalization.NumberStyles.Number,System.Globalization.CultureInfo.InvariantCulture,out var amount)&&amount>0?amount:null);
        AddCheck("每月回顾提醒",app.Engine.Config.RecapAlerts,value=>app.Engine.Config.RecapAlerts=value);
        page.Children.Add(Ui.Button("生成报告卡片",()=>{if(!app.Engine.Config.SpendEnabled){MessageBox.Show("先在 Token 消耗页开启本机记录读取。","Pulse");return;}spendReport??=app.Storage.Load<SpendReport>("spend-cache.json");if(spendReport is null){_=ScanSpend();return;}new ReportWindow(app,spendReport,reportPeriod,new DateTimeOffset(reportDate)).Show();},true));page.Children.Add(Ui.Button("重新扫描记录",()=>_=ScanSpend()));
    }
    private void Status()
    {
        Header("服务状态","读取服务商自己的公开状态页。打开时读取，页面停留期间每 5 分钟刷新。账号认证失败与服务商故障分别显示。");
        foreach(var provider in app.Engine.Config.Accounts.Where(a=>a.Enabled).Select(a=>a.ProviderId).Distinct()) {
            var definition=StatusService.Pages.FirstOrDefault(p=>p.ProviderId==provider);if(definition is null)continue;
            var block=new StackPanel();block.Children.Add(Ui.Label(ProviderCatalog.Find(provider)?.Name??provider,17));var body=new StackPanel();var local=provider;
            block.Children.Add(Ui.Button("读取官方状态",()=>_=StatusView.Fill(body,local,app)));block.Children.Add(Ui.Button("打开官方状态页",()=>WindowsServices.Open(definition.Address)));block.Children.Add(body);page.Children.Add(Ui.Box(block));_=StatusView.Fill(body,local,app);
        }
    }
    private void Extensions()
    {
        Header("扩展","扩展是你自行添加的本机程序，输出额度或指标。发现目录不会运行程序，启用后才执行。");var root=Path.Combine(PulseApp.DataRoot,"extensions");page.Children.Add(Ui.Row(Ui.Button("打开扩展目录",()=>{Directory.CreateDirectory(root);WindowsServices.Open(root);}),Ui.Button("重新扫描",()=>Navigate("extensions"))));foreach(var extension in new ExtensionService().Discover(root)) {
            var block=new StackPanel();block.Children.Add(Ui.Check(extension.Name,app.Engine.Config.EnabledExtensions.Contains(extension.Id),value=>{var config=app.Engine.Config;config.EnabledExtensions.Remove(extension.Id);if(value)config.EnabledExtensions.Add(extension.Id);var id="extension#"+extension.Id;var account=config.Accounts.FirstOrDefault(a=>a.Id==id);if(account is null&&value){account=new(){Id=id,ProviderId="extension",Label=extension.Name,Extra=new(){{"extensionId",extension.Id}}};config.Accounts.Add(account);}if(account is not null)account.Enabled=value;app.Save();if(value)_=app.Engine.RefreshAsync();}));if(extension.Error is {} error)block.Children.Add(Ui.Label(error,12,Ui.Brush("#FFBA83")));block.Children.Add(Ui.Label($"运行时限 {extension.TimeoutSeconds} 秒 · 输出上限 256 KiB",11,Ui.Muted));page.Children.Add(Ui.Box(block));
        }
    }
}

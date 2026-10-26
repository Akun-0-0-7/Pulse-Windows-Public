namespace Pulse.Desktop;

internal static class RailCard
{
    public static double Scale(AppConfig config)=>Math.Clamp(config.RingSize/36,.5,3);
    public static Brush Foreground(AppConfig config)=>config.Theme=="light"?Ui.Text:Brushes.White;
    public static Brush Secondary(AppConfig config)=>config.Theme=="light"?Ui.Muted:Ui.Brush("#73FFFFFF");
    public static StackPanel Content(PulseApp app,AccountConfig account)
    {
        var config=app.Engine.Config;var s=Scale(config);var white=Foreground(config);var muted=Secondary(config);
        var panel=new StackPanel{Width=(account.Detailed?310:214)*s};
        var header=new Grid{Height=19*s,Margin=new Thickness(0,0,0,14*s)};
        header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});header.ColumnDefinitions.Add(new ColumnDefinition());
        if(ProviderMark.IsKnown(account.ProviderId)){var mark=ProviderMark.Create(account.ProviderId,16*s,white);mark.Margin=new Thickness(0,0,8,0);mark.VerticalAlignment=VerticalAlignment.Center;header.Children.Add(mark);}
        var title=new TextBlock{Text=Localization.Format("{0} 用量",app.AccountName(account.Id)),FontSize=14*s,FontWeight=FontWeights.SemiBold,Foreground=white,VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis};
        Grid.SetColumn(title,1);header.Children.Add(title);panel.Children.Add(header);
        TextBlock Label(string text,double size,Brush? brush=null)=>new(){Text=Localization.Translate(text),FontSize=size*s,Foreground=brush??white,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,7*s)};
        if(!account.Enabled){panel.Children.Add(Label("账号未启用。勾选“启用这个账号”后读取额度。",11.5,muted));panel.Children.Add(Ui.Button("账号设置",()=>app.ShowSettings("account:"+account.Id)));return panel;}
        var reading=app.Engine.Reading(account.Id);
        if(account.Detailed&&reading?.Plan is {} plan)panel.Children.Add(Label(plan,11.5,muted));
        if(reading is null)panel.Children.Add(Label("等待首次读取",11.5,muted));
        else
        {
            foreach(var window in reading.Windows)
            {
                var row=new StackPanel{Margin=new Thickness(0,0,0,reading.Windows.Count>1?14*s:0)};
                row.Children.Add(new TextBlock{Text=window.Label??(window.Kind=="weekly"?Localization.Translate("每周限额"):Ui.WindowName(window)),FontSize=11.5*s,Foreground=white,Height=14*s,Margin=new Thickness(0,0,0,7*s)});
                row.Children.Add(new Meter(window.UsedPercent,config.SegmentedBars,config.Theme=="light"){Height=6*s,Margin=new Thickness(0,0,0,7*s)});
                var footer=new Grid{Height=14*s};footer.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});footer.ColumnDefinitions.Add(new ColumnDefinition());
                var used=new TextBlock{Text=Localization.Format("已用 {0}%",UsageFigures.Percent(window.UsedPercent)),Foreground=config.Theme=="light"?white:Ui.Brush("#E6FFFFFF"),FontSize=11.5*s};footer.Children.Add(used);
                var reset=new TextBlock{Text=Reset(window.ResetsAt),Foreground=muted,FontSize=11.5*s,HorizontalAlignment=HorizontalAlignment.Right};Grid.SetColumn(reset,1);footer.Children.Add(reset);row.Children.Add(footer);
                if(window.Estimated)row.Children.Add(Label("按所选基准估算",10,muted));
                panel.Children.Add(row);
            }
            if(reading.Balance is {} balance)panel.Children.Add(Label(Localization.Format("余额  {0}",Balance(balance)),16));
            if(reading.CreditBalance is {} credits)panel.Children.Add(Label("积分  "+credits,11.5));
            if(config.ForecastEnabled&&app.Engine.Forecast(account.Id) is {} forecast)panel.Children.Add(Label($"按最近消耗速度，约 {forecast.TotalHours:0.#} 小时后用尽",11.5,Ui.Brush("#FFBA83")));
            if(account.Detailed)
            {
                foreach(var metric in reading.Metrics)panel.Children.Add(Label($"{metric.Label}  {metric.Value:0.##} {metric.Unit}",11.5));
                DetailCharts.AddHistory(panel,app,account,reading);
                if(reading.ObservedAt is {} at)panel.Children.Add(Label($"读取于 {at.ToLocalTime():MM-dd HH:mm:ss} · {reading.Source}",10,muted));
            }
            if(reading.ErrorMessage is {} error)panel.Children.Add(Label((reading.IsStale?"上次读数 · ":"")+error,11.5,Ui.Brush("#FFBA83")));
        }
        if(account.Detailed)
        {
            if(LocalUsageCard.Create(app,account) is {} local)panel.Children.Add(local);
            DetailCharts.AddCodex(panel,app,account);panel.Children.Add(Ui.Button("账号设置",()=>app.ShowSettings("account:"+account.Id)));
        }
        return panel;
    }
    private static string Reset(DateTimeOffset? time)
    {
        if(time is not {} at)return "";if(at<=DateTimeOffset.Now)return Ui.ResetText(at);
        var format=Localization.Language is "zh-CN" or "zh-Hant"?"M月d日 HH:mm":"MMM d HH:mm";
        return Localization.Format("{0} 重置",at.ToLocalTime().ToString(format,Localization.Culture));
    }
    public static string Balance(MoneyBalance balance)
    {
        var amount=balance.Amount.ToString("0.##",Localization.Culture);
        return balance.Currency.ToUpperInvariant() switch{"CNY" or "RMB"=>"¥"+amount,"USD"=>"$"+amount,"EUR"=>"€"+amount,"JPY"=>"¥"+amount,"GBP"=>"£"+amount,_=>amount+" "+balance.Currency};
    }
    private sealed class Meter(double percent,bool segmented,bool light) : FrameworkElement
    {
        protected override void OnRender(DrawingContext drawing)
        {
            var color=percent>=90?Ui.Brush("#EF1630"):light?Ui.Text:Ui.Brush("#E6FFFFFF");
            var track=light?Ui.BorderBrush:Ui.Brush("#2B2B2B");var bounds=new Rect(0,0,ActualWidth,ActualHeight);
            drawing.DrawRoundedRectangle(track,null,bounds,ActualHeight/2,ActualHeight/2);
            drawing.PushClip(new RectangleGeometry(bounds,ActualHeight/2,ActualHeight/2));
            var value=double.IsFinite(percent)?Math.Clamp(percent,0,100)/100:0;
            if(!segmented)drawing.DrawRectangle(color,null,new Rect(0,0,ActualWidth*value,ActualHeight));
            else
            {
                var fractions=UsageBar.FillFractions(percent);var step=ActualWidth/fractions.Length;
                for(var i=0;i<fractions.Length;i++)drawing.DrawRectangle(color,null,new Rect(i*step,0,Math.Max(0,step-2)*fractions[i],ActualHeight));
            }
            drawing.Pop();
        }
    }
}

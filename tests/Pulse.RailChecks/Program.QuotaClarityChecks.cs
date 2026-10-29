using System.Collections.Concurrent;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Pulse.Core;
using Pulse.Desktop;

internal static partial class Program
{
    private static void QuotaClarityChecks(PulseApp app)
    {
        var language=Pulse.Desktop.Localization.Language;
        var config=app.Engine.Config;var saved=JsonSerializer.Serialize(config,StorageJson.Options);
        var accounts=config.Accounts;
        var readings=Field<ConcurrentDictionary<string,UsageReading>>(app.Engine,"readings");var cached=readings.ToArray();
        SettingsWindow? settings=null;
        try
        {
            Pulse.Desktop.Localization.Language="zh-CN";
            var account=new AccountConfig{Id="quota-clarity-fixture",ProviderId="unknown-offline-fixture",Enabled=true,PrimaryWindowId="five"};
            var reading=new UsageReading{AccountId=account.Id,ProviderId=account.ProviderId,Windows=[
                new(){Id="five",Kind="fiveHour",UsedPercent=2,ResetsAt=DateTimeOffset.Now.AddHours(5)},
                new(){Id="week",Kind="weekly",UsedPercent=51,ResetsAt=DateTimeOffset.Now.AddDays(7)}]};
            config.RingSize=36;config.ShowClock=false;config.ShowSecondaryRing=true;config.ShowRemaining=true;config.SecondaryRingShowRemaining=false;
            var tile=new RingTile(account,reading,null,config,false);
            void Tip(params string[] lines)
            {
                var actual=tile.ToolTip?.ToString()??"";
                if(actual!=string.Join(Environment.NewLine,lines))throw new Exception("Ring tooltip must name actual windows and modes. Actual: "+actual);
            }
            Tip("外环：5 小时额度 · 剩余 98%","内环：每周额度 · 已用 51%");
            config.SecondaryRingShowRemaining=true;tile.Update(reading,null,false,false);
            Tip("外环：5 小时额度 · 剩余 98%","内环：每周额度 · 剩余 49%");
            account.PrimaryWindowId="week";tile.Update(reading,null,false,false);
            Tip("外环：每周额度 · 剩余 49%","内环：5 小时额度 · 剩余 98%");
            account.PrimaryWindowId="five";reading.Windows[1].Exhausted=true;tile.Update(reading,null,false,false);
            Tip("外环：5 小时额度 · 剩余 98%","内环：每周额度 · 剩余 0%");
            reading.Windows[1].Exhausted=false;reading.Windows[1].UsedPercent=double.NaN;tile.Update(reading,null,false,false);
            Tip("外环：5 小时额度 · 剩余 98%","内环：每周额度 · 额度未知");
            config.ShowSecondaryRing=false;tile.Update(reading,null,false,false);Tip("外环：5 小时额度 · 剩余 98%");
            tile.Update(null,null,false,false);Tip("暂无额度数据");

            reading.Windows[1].UsedPercent=51;config.Accounts=[account];readings.Clear();readings[account.Id]=reading;
            foreach(var locale in new[]{"zh-CN","zh-Hant","en","ja","ko","ru"})
            {
                Pulse.Desktop.Localization.Language=locale;
                var card=RailWindow.DetailContent(app,account);
                card.Measure(new Size(card.Width,double.PositiveInfinity));card.Arrange(new Rect(0,0,card.Width,card.DesiredSize.Height));card.UpdateLayout();
                var rows=Descendants(card).OfType<StackPanel>().Where(row=>Equals(row.Tag,"quota-window")).ToArray();
                if(rows.Length!=2)throw new Exception("Detail card omitted quota rows.");
                foreach(var (row,index) in rows.Select((row,index)=>(row,index)))
                {
                    var texts=Descendants(row).OfType<TextBlock>().ToArray();
                    var used=texts.Single(t=>Equals(t.Tag,"quota-used"));var remaining=texts.Single(t=>Equals(t.Tag,"quota-remaining"));var reset=texts.Single(t=>Equals(t.Tag,"quota-reset"));
                    if(used.Text!=Pulse.Desktop.Localization.Format("已用 {0}%",index==0?2:51)||remaining.Text!=Pulse.Desktop.Localization.Format("剩余 {0}%",index==0?98:49))throw new Exception("Detail card must show both used and remaining values.");
                    var usedBounds=new Rect(used.TranslatePoint(new Point(),card),used.RenderSize);
                    var remainingBounds=new Rect(remaining.TranslatePoint(new Point(),card),remaining.RenderSize);
                    var resetBounds=new Rect(reset.TranslatePoint(new Point(),card),reset.RenderSize);
                    if(Rect.Intersect(usedBounds,remainingBounds) is var overlap&&!overlap.IsEmpty&&overlap.Width>.1&&overlap.Height>.1)throw new Exception("Used and remaining labels overlap: "+locale);
                    if(resetBounds.Top+0.1<Math.Max(usedBounds.Bottom,remainingBounds.Bottom)||resetBounds.Right>card.Width+.1)throw new Exception("Compact reset label overlaps percentages or exceeds card: "+locale);
                }
            }
            Pulse.Desktop.Localization.Language="zh-CN";
            reading.Windows[1].UsedPercent=double.NaN;
            var unknown=RailWindow.DetailContent(app,account);
            var unknownText=Descendants(unknown).OfType<TextBlock>().Select(t=>t.Text).ToArray();
            if(!unknownText.Contains("额度未知")||unknownText.Contains("剩余 100%")||unknownText.Contains("已用 0%"))throw new Exception("Unknown quota was presented as zero used/full remaining.");

            config.ShowRemaining=false;config.SecondaryRingShowRemaining=false;config.ShowSecondaryRing=false;
            var before=JsonSerializer.Serialize(config,StorageJson.Options);settings=new SettingsWindow(app);settings.Navigate("appearance");
            var button=Descendants((DependencyObject)settings.Content).OfType<Button>().SingleOrDefault(b=>Equals(b.Tag,"quota-all-remaining"))??throw new Exception("One-click remaining quota setting is missing.");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var persisted=app.Storage.LoadConfig();
            if(!config.ShowRemaining||!config.SecondaryRingShowRemaining||!persisted.ShowRemaining||!persisted.SecondaryRingShowRemaining)throw new Exception("One-click remaining mode did not save both ring choices.");
            persisted.ShowRemaining=false;persisted.SecondaryRingShowRemaining=false;
            if(JsonSerializer.Serialize(persisted,StorageJson.Options)!=before)throw new Exception("One-click remaining mode changed unrelated preferences.");
            var visible=Descendants((DependencyObject)settings.Content).OfType<CheckBox>().Where(b=>Equals(b.Content,"显示剩余额度")||Equals(b.Content,"内圈显示剩余额度")).ToArray();
            if(visible.Length!=2||visible.Any(b=>b.IsChecked!=true))throw new Exception("One-click remaining mode did not update both visible switches.");
            Console.WriteLine("PASS: actual outer/inner quota tooltips, alternate windows, unknown/exhausted states, compact bilingual percentages/reset layout, and one-click remaining save without unrelated changes.");
        }
        finally
        {
            settings?.Close();Pulse.Desktop.Localization.Language=language;
            var restored=JsonSerializer.Deserialize<AppConfig>(saved,StorageJson.Options)!;
            foreach(var property in typeof(AppConfig).GetProperties().Where(p=>p.CanRead&&p.CanWrite))property.SetValue(config,property.GetValue(restored));
            config.Accounts=accounts;readings.Clear();foreach(var item in cached)readings[item.Key]=item.Value;
            Ui.ApplyTheme(config);app.Storage.SaveConfig(config);
        }
    }
}

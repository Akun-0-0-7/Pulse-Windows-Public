namespace Pulse.Desktop;
public static class StatusView
{
    public static async Task Fill(StackPanel body,string provider,PulseApp app)
    {
        body.Children.Clear();body.Children.Add(Ui.Label("正在读取…",12,Ui.Muted));
        try{
            using var http=UsageEngine.CreateHttp(app.Engine.Config);var report=await new StatusService().FetchAsync(provider,http,app.Engine.LifetimeToken);body.Children.Clear();
            if(report.Error is {} error)body.Children.Add(Ui.Label(error,12,Ui.Muted));
            foreach(var component in report.Components){body.Children.Add(Ui.Label(component.Name+"  ·  "+component.State,13,component.State=="operational"?Ui.Brush("#72D9AD"):Ui.Brush("#FFBA83")));if(component.Uptime is {} uptime)body.Children.Add(Ui.Label($"官方可用率：{uptime:0.##}%",11,Ui.Muted));if(component.History.Count>0){var strip=new WrapPanel();foreach(var day in component.History){strip.Children.Add(new Border{Width=5,Height=20,Margin=new Thickness(1),Background=day.State is "operational" or "up"?Ui.Brush("#72D9AD"):day.State is null?Ui.BorderBrush:Ui.Brush("#FFBA83"),ToolTip=$"{day.At:yyyy-MM-dd HH:mm} {day.State}"});}body.Children.Add(strip);}}
            foreach(var incident in report.Incidents)body.Children.Add(Ui.Label(incident.Name+" · "+incident.State,12,Ui.Muted));
        }catch(OperationCanceledException)when(app.Engine.LifetimeToken.IsCancellationRequested){}
    }
}

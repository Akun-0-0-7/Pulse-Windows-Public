namespace Pulse.Core;
/// <summary>Remembers only the current known fault states, suitable for status-alert-memory.json.</summary>
public sealed class StatusAlertRules
{
    public HashSet<string> Seen {get;set;}=[];
    public List<string> Evaluate(AppConfig config,StatusReport report)
    {
        var selected=config.Accounts.Where(a=>a.Enabled).Select(a=>a.ProviderId).ToHashSet(StringComparer.Ordinal);
        if(!config.AlertsEnabled||!config.StatusAlerts){Seen.Clear();return [];}
        Seen.RemoveWhere(key=>!selected.Any(id=>key.StartsWith(id+":",StringComparison.Ordinal)));
        if(!selected.Contains(report.ProviderId)||report.Error is not null)return [];
        var rule=StatusService.Pages.FirstOrDefault(p=>p.ProviderId==report.ProviderId)?.Notifies;
        var prefix=report.ProviderId+":";
        var previous=Seen.Where(x=>x.StartsWith(prefix,StringComparison.Ordinal)).ToHashSet();
        var faults=report.Components.Where(c=>StatusService.Matches(rule,c)&&c.State is "degraded" or "partialOutage" or "fullOutage").ToList();
        var current=faults.Select(c=>prefix+Uri.EscapeDataString(c.Id)+":"+c.State).ToHashSet();
        var changed=faults.Where(c=>!previous.Contains(prefix+Uri.EscapeDataString(c.Id)+":"+c.State)).ToList();
        // An unreadable or absent component is not evidence of recovery.
        var recovered=report.Components.Where(c=>c.State=="operational"&&previous.Any(p=>p.StartsWith(prefix+Uri.EscapeDataString(c.Id)+":",StringComparison.Ordinal))).ToList();
        foreach(var component in report.Components.Where(c=>c.State is "operational" or "degraded" or "partialOutage" or "fullOutage"))Seen.RemoveWhere(p=>p.StartsWith(prefix+Uri.EscapeDataString(component.Id)+":",StringComparison.Ordinal));
        Seen.UnionWith(current);
        var alerts=new List<string>();
        if(changed.Count>0)alerts.Add("官方状态："+string.Join("、",changed.Select(c=>c.Name+"（"+Name(c.State)+"）")));
        if(recovered.Count>0)alerts.Add("官方状态已恢复："+string.Join("、",recovered.Select(c=>c.Name)));
        return alerts;
    }
    static string Name(string state)=>state switch{"degraded"=>"性能下降","partialOutage"=>"部分中断","fullOutage"=>"服务中断",_=>state};
}

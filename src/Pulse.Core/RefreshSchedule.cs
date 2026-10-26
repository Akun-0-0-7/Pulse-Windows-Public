namespace Pulse.Core;
/// <summary>Choose the polling interval from tool activity and Windows idle time; fractional minutes support seconds.</summary>
public static class RefreshSchedule
{
    public static double Minutes(double activeMinutes,bool agentActive,bool settingsVisible,TimeSpan idle)
    {
        var basis=Math.Clamp(double.IsFinite(activeMinutes)?activeMinutes:2,1d/6,30);
        if(agentActive||settingsVisible||idle<TimeSpan.FromMinutes(2))return basis;
        return Math.Max(basis,idle>=TimeSpan.FromMinutes(10)?30:idle>=TimeSpan.FromMinutes(5)?10:5);
    }
}

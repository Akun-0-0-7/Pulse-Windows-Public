using System.Globalization;
using System.Text;
namespace Pulse.Core;
public static class SpendCsv
{
    public static string Format(SpendReport report)
    {
        string Text(string? value){var text=value??"";if(text.TrimStart().FirstOrDefault() is '=' or '+' or '-' or '@')text="'"+text;return "\""+text.Replace("\"","\"\"")+"\"";}
        string Number(long value,bool reported)=>reported?value.ToString(CultureInfo.InvariantCulture):"";
        var output=new StringBuilder("时间,客户端,模型,输入,输出,缓存读取,缓存写入,Token,估算美元\r\n");
        foreach(var r in report.Records)output.AppendLine(string.Join(',',Text(r.At.ToString("O")),Text(r.Agent),Text(r.Model),Number(r.Input,r.ReportsTokens),Number(r.Output,r.ReportsTokens),Number(r.CacheRead,r.ReportsTokens),Number(r.CacheWrite,r.ReportsTokens),r.TotalTokens?.ToString(CultureInfo.InvariantCulture)??"",r.EstimatedCost?.ToString(CultureInfo.InvariantCulture)??""));
        return output.ToString();
    }
}

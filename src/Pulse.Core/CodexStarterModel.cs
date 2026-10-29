using System.Text.Json;
using System.Text.RegularExpressions;

namespace Pulse.Core;

public sealed record CodexStarterModel(string Model,string Source)
{
    // Read settings without starting a thread. A configured model may be newer than
    // the CLI's bundled catalog, so that catalog must never veto the user's choice.
    public static async Task<CodexStarterModel?> ResolveAsync(string executable,string workingDirectory,
        CancellationToken cancellationToken=default,Dictionary<string,string>? environment=null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            async Task<string> Read(string method,object parameters)=>await HandwrittenRpc.InvokeAsync(executable,["app-server"],method,
                cancellationToken:deadline.Token,environment:environment,parameters:parameters,workingDirectory:workingDirectory);
            using var document=JsonDocument.Parse(await Read("config/read",new{includeLayers=false,cwd=workingDirectory}));
            var root=document.RootElement;
            if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty("config",out var config)||config.ValueKind!=JsonValueKind.Object)return null;
            // The starter uses the monitored ChatGPT login with isolated OpenAI settings.
            // Importing only a model name from another provider would silently change its route.
            if(config.TryGetProperty("model_provider",out var provider)&&provider.ValueKind!=JsonValueKind.Null&&
                (provider.ValueKind!=JsonValueKind.String||provider.GetString()!="openai"))return null;
            if(config.TryGetProperty("model",out var selected)&&selected.ValueKind!=JsonValueKind.Null)
                return ValidModel(selected)?new(selected.GetString()!,"codexConfig"):null;

            string? cursor=null;
            var visited=new HashSet<string>();
            for(var page=0;page<5;page++)
            {
                using var catalog=JsonDocument.Parse(await Read("model/list",new{limit=100,includeHidden=false,cursor}));
                var list=catalog.RootElement;
                if(list.ValueKind!=JsonValueKind.Object||!list.TryGetProperty("data",out var data)||data.ValueKind!=JsonValueKind.Array)return null;
                foreach(var item in data.EnumerateArray())
                    if(item.ValueKind==JsonValueKind.Object&&item.TryGetProperty("isDefault",out var isDefault)&&isDefault.ValueKind==JsonValueKind.True&&
                        (!item.TryGetProperty("hidden",out var hidden)||hidden.ValueKind==JsonValueKind.False)&&
                        item.TryGetProperty("model",out var model)&&ValidModel(model))return new(model.GetString()!,"codexDefault");
                if(!list.TryGetProperty("nextCursor",out var next)||next.ValueKind!=JsonValueKind.String)return null;
                cursor=next.GetString();
                if(string.IsNullOrEmpty(cursor)||!visited.Add(cursor))return null;
            }
            return null;
        }
        catch(Exception exception)when(exception is IOException or UnauthorizedAccessException or InvalidOperationException or
            JsonException or OperationCanceledException or System.ComponentModel.Win32Exception)
        {cancellationToken.ThrowIfCancellationRequested();return null;}
    }

    private static bool ValidModel(JsonElement value)=>value.ValueKind==JsonValueKind.String&&
        Regex.IsMatch(value.GetString()!,"\\A[A-Za-z0-9][A-Za-z0-9._-]{0,127}\\z",RegexOptions.CultureInvariant);
}

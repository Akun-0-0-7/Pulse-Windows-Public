using System.Text;
using System.Text.Json;

namespace Pulse.Core;

public static class WindowStarterIdentity
{
    public static bool SameCodexAccount(string? savedLogin, string? localAuth)
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OPENAI_API_KEY"))) return false;
        var savedAccount = AccountOf(savedLogin);
        var localAccount = AccountOf(localAuth);
        return savedAccount is not null && localAccount is not null &&
            string.Equals(savedAccount, localAccount, StringComparison.Ordinal);
    }

    private static string? AccountOf(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            var identities = new List<string>();
            var accessTokens = new List<string>();
            foreach (var field in root.EnumerateObject())
            {
                if (field.Name is "accountId" or "account_id")
                {
                    if (!AddString(field.Value, identities)) return null;
                }
                else if (field.Name is "accessToken" or "access_token" or "token")
                {
                    if (!AddString(field.Value, accessTokens)) return null;
                }
                else if (field.Name == "auth_mode")
                {
                    if (field.Value.ValueKind != JsonValueKind.String ||
                        !string.Equals(field.Value.GetString(), "chatgpt", StringComparison.OrdinalIgnoreCase)) return null;
                }
                else if (field.Name == "OPENAI_API_KEY")
                {
                    if (field.Value.ValueKind != JsonValueKind.Null &&
                        (field.Value.ValueKind != JsonValueKind.String || field.Value.GetString()!.Length != 0)) return null;
                }
                else if (field.Name == "tokens")
                {
                    if (field.Value.ValueKind != JsonValueKind.Object) return null;
                    foreach (var nested in field.Value.EnumerateObject())
                    {
                        if (nested.Name == "account_id")
                        {
                            if (!AddString(nested.Value, identities)) return null;
                        }
                        else if (nested.Name == "access_token")
                        {
                            if (!AddString(nested.Value, accessTokens)) return null;
                        }
                    }
                }
            }

            if (!OneValue(accessTokens, out var accessToken) || accessToken.StartsWith("sk-", StringComparison.OrdinalIgnoreCase))
                return null;
            if (!CollectJwtClaims(accessToken, identities)) return null;
            return OneValue(identities, out var accountId) ? accountId : null;
        }
        catch (JsonException) { return null; }
        catch (FormatException) { return null; }
    }

    private static bool AddString(JsonElement value, List<string> values)
    {
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString())) return false;
        values.Add(value.GetString()!);
        return true;
    }

    private static bool OneValue(List<string> values, out string value)
    {
        value = "";
        if (values.Count == 0) return false;
        var first = values[0];
        value = first;
        return values.All(candidate => string.Equals(candidate, first, StringComparison.Ordinal));
    }

    private static bool CollectJwtClaims(string accessToken, List<string> identities)
    {
        if (!accessToken.Contains('.')) return true;
        var pieces = accessToken.Split('.');
        if (pieces.Length != 3 || pieces[1].Length == 0) return false;
        var payload = pieces[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
        using var document = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
        var claims = document.RootElement;
        if (claims.ValueKind != JsonValueKind.Object) return false;
        foreach (var field in claims.EnumerateObject())
        {
            if (field.Name == "chatgpt_account_id" && !AddString(field.Value, identities)) return false;
            if (field.Name != "https://api.openai.com/auth") continue;
            if (field.Value.ValueKind != JsonValueKind.Object) return false;
            foreach (var authField in field.Value.EnumerateObject())
                if (authField.Name == "chatgpt_account_id" && !AddString(authField.Value, identities)) return false;
        }
        return true;
    }
}

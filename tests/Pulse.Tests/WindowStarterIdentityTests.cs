using System.Text;
using System.Text.Json;
using Pulse.Core;

namespace Pulse.Tests;

public static class WindowStarterIdentityTests
{
    private static string Jwt(string payload) => "fixture." +
        Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)).TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".fixture";

    [Check] public static void MatchingExplicitAccountsAcceptSupportedTokenWrappers()
    {
        const string local = """{"auth_mode":"chatgpt","tokens":{"access_token":"fixture-local-access","account_id":"account-A"}}""";
        Assert.True(WindowStarterIdentity.SameCodexAccount("""{"accountId":"account-A","accessToken":"fixture-saved-access"}""", local));
        Assert.True(WindowStarterIdentity.SameCodexAccount("""{"account_id":"account-A","access_token":"fixture-saved-access"}""", local));
        Assert.True(WindowStarterIdentity.SameCodexAccount("""{"tokens":{"account_id":"account-A","access_token":"fixture-saved-access"}}""", local));
        Assert.True(WindowStarterIdentity.SameCodexAccount("""{"accountId":"account-A","token":"fixture-saved-access"}""", local));
    }

    [Check] public static void JwtAuthClaimCanIdentifyAccountWithoutExplicitField()
    {
        var saved = JsonSerializer.Serialize(new { accessToken = Jwt("""{"https://api.openai.com/auth":{"chatgpt_account_id":"account-A"}}""") });
        var local = JsonSerializer.Serialize(new { auth_mode = "chatgpt", tokens = new { access_token = Jwt("""{"https://api.openai.com/auth":{"chatgpt_account_id":"account-A"}}""") } });
        Assert.True(WindowStarterIdentity.SameCodexAccount(saved, local));
    }

    [Check] public static void ExplicitAccountCanIdentifyOpaqueAccessTokenWithoutJwtClaim()
    {
        Assert.True(WindowStarterIdentity.SameCodexAccount(
            """{"accountId":"account-A","accessToken":"fixture-opaque-access"}""",
            """{"auth_mode":"chatgpt","account_id":"account-A","tokens":{"access_token":"fixture-local-access"}}"""));
        var savedJwtWithoutClaim = JsonSerializer.Serialize(new { accountId = "account-A", accessToken = Jwt("{}") });
        Assert.True(WindowStarterIdentity.SameCodexAccount(savedJwtWithoutClaim,
            """{"auth_mode":"chatgpt","account_id":"account-A","tokens":{"access_token":"fixture-local-access"}}"""));
    }

    [Check] public static void ConflictingIdentityFieldsOrJwtClaimAreRejected()
    {
        var local = """{"auth_mode":"chatgpt","tokens":{"access_token":"fixture-local-access","account_id":"account-A"}}""";
        Assert.True(!WindowStarterIdentity.SameCodexAccount("""{"accountId":"account-A","account_id":"account-B","accessToken":"fixture-access"}""", local));
        Assert.True(!WindowStarterIdentity.SameCodexAccount("""{"accountId":"account-A","tokens":{"account_id":"account-B"},"accessToken":"fixture-access"}""", local));
        var conflict = JsonSerializer.Serialize(new { accountId = "account-A", accessToken = Jwt("""{"https://api.openai.com/auth":{"chatgpt_account_id":"account-B"}}""") });
        Assert.True(!WindowStarterIdentity.SameCodexAccount(conflict, local));
        Assert.True(!WindowStarterIdentity.SameCodexAccount("""{"accountId":"account-A","accountId":"account-B","accessToken":"fixture-access"}""", local));
    }

    [Check] public static void UnknownInvalidAndDifferentAccountsAreRejected()
    {
        var local = """{"auth_mode":"chatgpt","account_id":"account-A","tokens":{"access_token":"fixture-local-access"}}""";
        Assert.True(!WindowStarterIdentity.SameCodexAccount("""{"accessToken":"fixture-access"}""", local));
        Assert.True(!WindowStarterIdentity.SameCodexAccount("""{"accountId":"account-a","accessToken":"fixture-access"}""", local));
        Assert.True(!WindowStarterIdentity.SameCodexAccount("""{"accountId":"account-A"}""", local));
        Assert.True(!WindowStarterIdentity.SameCodexAccount("sk-fixture-api-key", local));
        Assert.True(!WindowStarterIdentity.SameCodexAccount("{invalid", local));
        Assert.True(!WindowStarterIdentity.SameCodexAccount("""{"accountId":"account-A","accessToken":"fixture-access"}""", "{invalid"));
        Assert.True(!WindowStarterIdentity.SameCodexAccount("""{"accountId":"account-A","accessToken":"fixture-access","tokens":"malformed"}""", local));
    }

    [Check] public static void ApiKeyModeAndEnvironmentOverrideAreRejected()
    {
        const string saved = """{"accountId":"account-A","accessToken":"fixture-saved-access"}""";
        Assert.True(!WindowStarterIdentity.SameCodexAccount(saved,
            """{"auth_mode":"apikey","account_id":"account-A","tokens":{"access_token":"fixture-local-access"}}"""));
        var prior = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", "fixture-key");
            Assert.True(!WindowStarterIdentity.SameCodexAccount(saved,
                """{"auth_mode":"chatgpt","account_id":"account-A","tokens":{"access_token":"fixture-local-access"}}"""));
        }
        finally { Environment.SetEnvironmentVariable("OPENAI_API_KEY", prior); }
    }

    [Check] public static void LocalAuthJsonApiKeyRejectsLegacyTokensUnlessValueIsEmpty()
    {
        const string saved = """{"accountId":"account-A","accessToken":"fixture-saved-access"}""";
        Assert.True(!WindowStarterIdentity.SameCodexAccount(saved,
            """{"OPENAI_API_KEY":"fixture-key","account_id":"account-A","tokens":{"access_token":"fixture-local-access"}}"""));
        Assert.True(!WindowStarterIdentity.SameCodexAccount(saved,
            """{"OPENAI_API_KEY":17,"account_id":"account-A","tokens":{"access_token":"fixture-local-access"}}"""));
        Assert.True(WindowStarterIdentity.SameCodexAccount(saved,
            """{"OPENAI_API_KEY":"","account_id":"account-A","tokens":{"access_token":"fixture-local-access"}}"""));
        Assert.True(WindowStarterIdentity.SameCodexAccount(saved,
            """{"OPENAI_API_KEY":null,"account_id":"account-A","tokens":{"access_token":"fixture-local-access"}}"""));
    }
}

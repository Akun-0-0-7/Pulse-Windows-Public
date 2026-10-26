using Pulse.Core;
using System.Net;
using System.Net.Http;
namespace Pulse.Tests;
public static class ProfiledTests
{
    static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","Profiled",name+".json"));
    static UsageReading Parse(string id,string fixture,Dictionary<string,string>? extra=null) => ProfiledProviders.Parse(new FetchContext(new AccountConfig { ProviderId=id,Id="test",Enabled=true },"fixture",new HttpClient(),"X:\\fixture-home","X:\\fixture-data",default),Fixture(fixture),extra,DateTimeOffset.Parse("2026-09-20T12:00:00Z"));
    [Check] public static void ProfiledCatalogHas76UniqueEntries() { Assert.Equal(76,ProviderCatalog.All.Count); Assert.Equal(76,ProviderCatalog.All.Select(x=>x.Id).Distinct().Count()); }
    [Check] public static void ProfiledSyntheticNamedLanes() { var r=Parse("synthetic","synthetic-quotas"); Assert.True(r.Success); Assert.Equal(3,r.Windows.Count); Assert.Near(0.8,r.Windows[0].UsedPercent); Assert.Near(20,r.Windows[1].UsedPercent); Assert.True(r.Windows[1].ResetsAt is null); Assert.Near(2,r.Windows[2].UsedPercent); }
    [Check] public static void ProfiledElevenLabsCharacters() { var r=Parse("elevenLabs","elevenlabs-subscription"); Assert.True(r.Success); Assert.Equal("Creator",r.Plan); Assert.Near(25,r.Windows.Single().UsedPercent); }
    [Check] public static void ProfiledAtlasMoneyNoRing() { var r=Parse("atlasCloud","atlas-cloud-balance"); Assert.Equal(new MoneyBalance(42.5m,"USD"),r.Balance); Assert.Equal(0,r.Windows.Count); }
    [Check] public static void ProfiledXaiSignedCents() { var r=Parse("xaiAPI","xai-api-prepaid-balance"); Assert.Equal(new MoneyBalance(12.34m,"USD"),r.Balance); }
    [Check] public static void ProfiledNoGenericQuotaGuess() { var c=new FetchContext(new AccountConfig{ProviderId="synthetic"},"fixture",new HttpClient(),"X:\\unused","X:\\unused",default); var r=ProfiledProviders.Parse(c,"{\"quotas\":[{\"used\":4,\"limit\":10}]}"); Assert.True(!r.Success); }
    [Check] public static void ProfiledFixtureCoverage() {
        var cases=new (string Id,string Fixture)[]{
            ("clinePass","clinepass-usage-limits"),("alibabaCodingPlan","alibaba-coding-plan-instances"),("alibabaTokenPlan","alibaba-token-plan-cli-usage"),("qwenCloud","qwen-cloud-usage"),("factory","factory-subscription-usage"),("gemini","gemini-retrieve-user-quota"),("kiloCode","kilo-code-trpc-batch"),("augment","augment-credits"),("t3Chat","t3-chat-customer-data"),("synthetic","synthetic-quotas"),("elevenLabs","elevenlabs-subscription"),("warp","warp-request-limit"),("windsurf","windsurf-plan-status"),("bifrost","bifrost-quota"),("chutes","chutes-subscription-usage"),("longCat","longcat-token-packs"),("zoomMate","zoommate-credits-status"),("notionAI","notion-ai-rate-limit"),("ibmBob","ibm-bob-profile"),("nousPortal","nous-portal-account"),("raycastAI","raycast-ai-credits"),("gitKraken","gitkraken-usage"),("xKiro","xkiro-usage"),("abacus","abacus-compute-points"),("moonshot","moonshot-balance"),("hyper","hyper-credits"),("atlasCloud","atlas-cloud-balance"),("poe","poe-current-balance"),("venice","venice-balance"),("openAIPlatform","openai-api-credit-grants"),("amp","amp-balance-tier"),("zed","zed-billing-usage"),("mistral","mistral-credits"),("codebuff","codebuff-usage"),("llmProxy","llm-proxy-quota-stats"),("liteLLM","litellm-key-info"),("aixy","aixy-usage"),("neuralwatt","neuralwatt-quota"),("clawRouter","clawrouter-usage"),("zenMux","zenmux-subscription-detail"),("v0","v0-billing-token"),("devPass","devpass-key"),("perplexity","perplexity-credits"),("manus","manus-credits"),("huggingFace","hugging-face-zero-gpu-quota"),("deepInfra","deepinfra-checklist"),("xaiAPI","xai-api-prepaid-balance"),("replicate","replicate-unused-credit"),("typeSafe","typesafe-billing-result"),("vercelAIGateway","vercel-ai-gateway-credits")
        };
        Assert.Equal(50,cases.Length);
        foreach(var (id,fixture) in cases) {var r=Parse(id,fixture);Assert.True(r.Success||r.ErrorCode is "noPlan" or "noLimitsReported",id+": "+r.ErrorCode);}
    }
    sealed class Stub(Func<HttpRequestMessage,HttpResponseMessage> response) : HttpMessageHandler {
        public List<(string Method,string Url,string? Authorization,string? Cookie)> Requests { get; }=[];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken) {
            Requests.Add((request.Method.Method,request.RequestUri!.ToString(),request.Headers.Authorization?.ToString(),request.Headers.TryGetValues("Cookie",out var values)?values.Single():null));
            return Task.FromResult(response(request));
        }
    }
    static HttpResponseMessage JsonReply(string fixture, HttpStatusCode status=HttpStatusCode.OK) => new(status) { Content=new StringContent(Fixture(fixture)) };
    [Check] public static async Task ProfiledFetchUsesInjectedHttpAndRequiredAuth() {
        var stub=new Stub(_=>JsonReply("synthetic-quotas"));
        using var client=new HttpClient(stub);
        var account=new AccountConfig {ProviderId="synthetic",Enabled=true,Id="synthetic-test"};
        var reader=ProfiledProviders.TryCreate("synthetic")!;
        var context=new FetchContext(account,"fixture-token",client,"X:\\fixture-home","X:\\fixture-data",default);
        var result=await reader.FetchAsync(context);
        Assert.True(result.Success);
        Assert.Equal(1,stub.Requests.Count);
        Assert.Equal("GET",stub.Requests[0].Method);
        Assert.Equal("https://api.synthetic.new/v2/quotas",stub.Requests[0].Url);
        Assert.Equal("Bearer fixture-token",stub.Requests[0].Authorization);
        account.Enabled=false;
        Assert.Equal("disabled",(await reader.FetchAsync(context)).ErrorCode);
        Assert.Equal(1,stub.Requests.Count);
    }
    [Check] public static async Task ProfiledFetchPreservesRefusal() {
        var stub=new Stub(_=>JsonReply("synthetic-quotas",HttpStatusCode.Unauthorized));
        using var client=new HttpClient(stub);
        var account=new AccountConfig {ProviderId="synthetic",Enabled=true};
        var result=await ProfiledProviders.TryCreate("synthetic")!.FetchAsync(new(account,"fixture-token",client,"X:\\fixture-home","X:\\fixture-data",default));
        Assert.Equal("authRefused",result.ErrorCode);
    }
    static FetchContext Context(string id,HttpClient client,string? credential="fixture-token",string? home=null) => new(new AccountConfig {ProviderId=id,Enabled=true,Id="protocol-test"},credential,client,home??"X:\\fixture-home","X:\\fixture-data",default);
    static HttpResponseMessage TextReply(string text) => new(HttpStatusCode.OK) {Content=new StringContent(text)};
    [Check] public static async Task ProfiledQwenUsesSecurityTokenAndFormGateway() {
        var step=0;
        var stub=new Stub(request=> {
            step++;
            if(step==1) {Assert.Equal("home.qwencloud.com",request.RequestUri!.Host);return TextReply("<script>window.config={secToken:'a+b&c=d'}</script>");}
            Assert.True(request.RequestUri!.Query.Contains("IntlBroadScopeAspnGateway"));
            Assert.Equal("application/x-www-form-urlencoded",request.Content!.Headers.ContentType!.MediaType);
            var body=request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            Assert.True(body.Contains("sec_token=a%2Bb%26c%3Dd"));
            Assert.Equal("https://home.qwencloud.com",request.Headers.GetValues("Origin").Single());
            return request.RequestUri.Query.Contains("subscription")?TextReply("{\"data\":{\"specCode\":\"pro\"}}"):JsonReply("qwen-cloud-usage");
        });
        using var client=new HttpClient(stub);
        var reading=await ProfiledProviders.TryCreate("qwenCloud")!.FetchAsync(Context("qwenCloud",client,"login_qwencloud_ticket=t; csrf=x"));
        Assert.True(reading.Success,reading.ErrorCode??"success");Assert.Equal("Pro",reading.Plan);Assert.Equal(3,step);
    }
    [Check] public static async Task ProfiledAlibabaRetriesCorrectRegionalGateway() {
        var step=0;
        var stub=new Stub(request=> {
            step++;Assert.True(request.RequestUri!.Query.Contains("queryCodingPlanInstanceInfoV2"));
            Assert.Equal("fixture-token",request.Headers.GetValues("x-api-key").Single());
            var body=request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            if(step==1) {Assert.Equal("modelstudio.console.alibabacloud.com",request.RequestUri.Host);Assert.True(body.Contains("sfm_codingplan_public_intl"));return new(HttpStatusCode.Unauthorized);}
            Assert.Equal("bailian.console.aliyun.com",request.RequestUri.Host);Assert.True(body.Contains("sfm_codingplan_public_cn"));return JsonReply("alibaba-coding-plan-instances");
        });
        using var client=new HttpClient(stub);
        Assert.True((await ProfiledProviders.TryCreate("alibabaCodingPlan")!.FetchAsync(Context("alibabaCodingPlan",client))).Success);Assert.Equal(2,step);
    }
    [Check] public static async Task ProfiledGeminiReadsLocalGoogleLoginAndPassesProject() {
        var home=Path.Combine(Path.GetTempPath(),"PulseGemini-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(home,".gemini"));
        try {
            File.WriteAllText(Path.Combine(home,".gemini","oauth_creds.json"),"{\"access_token\":\"google-fixture\",\"expiry_date\":4102444800000}");
            var stub=new Stub(request=> {
                Assert.Equal("Bearer google-fixture",request.Headers.Authorization!.ToString());
                var body=request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                if(request.RequestUri!.AbsolutePath.EndsWith(":loadCodeAssist")){Assert.True(body.Contains("GEMINI_CLI"));return TextReply("{\"cloudaicompanionProject\":{\"id\":\"fixture-project\"},\"paidTier\":{\"name\":\"Pro\"}}");}
                Assert.True(body.Contains("fixture-project"));return JsonReply("gemini-retrieve-user-quota");
            });
            using var client=new HttpClient(stub);
            var result=await ProfiledProviders.TryCreate("gemini")!.FetchAsync(Context("gemini",client,null,home));
            Assert.True(result.Success,result.ErrorCode??"success");Assert.Equal("Pro",result.Plan);Assert.Equal(2,stub.Requests.Count);
            File.WriteAllText(Path.Combine(home,".gemini","settings.json"),"{\"security\":{\"auth\":{\"selectedType\":\"vertex-ai\"}}}");
            Assert.Equal("localLoginMissing",(await ProfiledProviders.TryCreate("gemini")!.FetchAsync(Context("gemini",client,null,home))).ErrorCode);
            Assert.Equal(2,stub.Requests.Count);
        } finally {Directory.Delete(home,true);}
    }
    [Check] public static async Task ProfiledNotionChoosesEligibleWorkspaceBeforeQuota() {
        var step=0;
        var stub=new Stub(request=> {
            step++;
            if(step==1){Assert.True(request.RequestUri!.AbsolutePath.EndsWith("getSpaces"));return TextReply("{\"user\":{\"space\":{\"free\":{\"value\":{\"id\":\"free-id\",\"subscription_tier\":\"free\"}},\"paid\":{\"value\":{\"id\":\"paid-id\",\"subscription_tier\":\"business\"}}}}}");}
            Assert.True(request.RequestUri!.AbsolutePath.EndsWith("getCreditRateLimitStatus"));
            Assert.True(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult().Contains("paid-id"));return JsonReply("notion-ai-rate-limit");
        });
        using var client=new HttpClient(stub);var reading=await ProfiledProviders.TryCreate("notionAI")!.FetchAsync(Context("notionAI",client,"token_v2=fixture"));
        Assert.True(reading.Success,reading.ErrorCode??"success");Assert.Equal("Business",reading.Plan);Assert.Equal(2,step);
    }
    [Check] public static async Task ProfiledWindsurfSendsProtobufAndParsesRawBinary() {
        var stub=new Stub(request=> {
            Assert.Equal("application/proto",request.Content!.Headers.ContentType!.MediaType);
            Assert.Equal("fixture",request.Headers.GetValues("x-devin-session-token").Single());
            Assert.True(request.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult().SequenceEqual(new byte[]{10,7,102,105,120,116,117,114,101,16,1}));
            var fixture=System.Text.Json.JsonDocument.Parse(Fixture("windsurf-plan-status")).RootElement.GetProperty("base64").GetString()!;
            return new(HttpStatusCode.OK){Content=new ByteArrayContent(Convert.FromBase64String(fixture))};
        });
        using var client=new HttpClient(stub);var reading=await ProfiledProviders.TryCreate("windsurf")!.FetchAsync(Context("windsurf",client,"{\"devin_session_token\":\"fixture\",\"devin_auth1_token\":\"a\",\"devin_account_id\":\"b\",\"devin_primary_org_id\":\"c\"}"));
        Assert.True(reading.Success,reading.ErrorCode??"success");Assert.Equal(2,reading.Windows.Count);
    }
    [Check] public static void ProfiledOverageAndExplicitLimitedRemainAccurate() {
        var c=Context("synthetic",new HttpClient());
        var limited=ProfiledProviders.Parse(c,"{\"weeklyTokenLimit\":{\"remainingCredits\":\"9\",\"maxCredits\":\"10\",\"limited\":true}}");
        Assert.Near(10,limited.Windows.Single().UsedPercent);Assert.True(limited.Windows.Single().Exhausted);
        var notion=ProfiledProviders.Parse(c with {Account=new AccountConfig{ProviderId="notionAI"}},"{\"window\":{\"used\":12,\"limit\":10,\"window\":\"5h\"}}");
        Assert.Near(120,notion.Windows.Single().UsedPercent);Assert.True(notion.Windows.Single().Exhausted);
    }
    [Check] public static async Task ProfiledTypeSafeDiscoversPublicActionThenPostsCookie() {
        const string action="0123456789abcdef0123456789abcdef0123456789";var step=0;
        var stub=new Stub(request=> {
            step++;
            if(step==1)return TextReply("<script src='https://unrelated.example/ignore.js'></script><script src='/_next/static/billing.js'></script>");
            if(step==2){Assert.Equal("console.typesafe.ai",request.RequestUri!.Host);Assert.True(!request.Headers.Contains("Cookie"));return TextReply("call(\""+action+"\", 0, \"getBillingOverviewResult\")");}
            Assert.Equal("POST",request.Method.Method);Assert.Equal(action,request.Headers.GetValues("Next-Action").Single());Assert.Equal("session=fixture",request.Headers.GetValues("Cookie").Single());
            Assert.Equal("[]",request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());return JsonReply("typesafe-billing-result");
        });
        using var client=new HttpClient(stub);var reading=await ProfiledProviders.TryCreate("typeSafe")!.FetchAsync(Context("typeSafe",client,"Cookie: session=fixture"));
        Assert.True(reading.Success,reading.ErrorCode??"success");Assert.Equal(3,step);
    }
    [Check] public static async Task ProfiledFactoryOldBillingFallsBackEvenWith200Limits() {
        var step=0;
        var stub=new Stub(request=> {
            step++;Assert.Equal("web-app",request.Headers.GetValues("x-factory-client").Single());
            if(step==1)return TextReply("{\"userProfile\":{\"id\":\"user-1\"}}");
            if(step==2)return TextReply("{\"usesTokenRateLimitsBilling\":false}");
            Assert.True(request.RequestUri!.Query.Contains("userId=user-1"));return JsonReply("factory-subscription-usage");
        });
        using var client=new HttpClient(stub);Assert.True((await ProfiledProviders.TryCreate("factory")!.FetchAsync(Context("factory",client))).Success);Assert.Equal(3,step);
    }
    [Check] public static async Task ProfiledLongCatMissingPacksStillUsesTokenUsage() {
        var stub=new Stub(request=> request.RequestUri!.AbsolutePath switch {
            "/api/v1/user-current"=>TextReply("{}"),
            "/api/pay/quota/metering/token-packs/summary"=>new(HttpStatusCode.Forbidden),
            "/api/lc-platform/v1/tokenUsage"=>TextReply("{\"usage\":{\"usedToken\":25,\"totalToken\":100}}"),
            _=>TextReply("{}")
        });
        using var client=new HttpClient(stub);var reading=await ProfiledProviders.TryCreate("longCat")!.FetchAsync(Context("longCat",client,"passport_token=fixture; uid=1"));
        Assert.True(reading.Success,reading.ErrorCode??"success");Assert.Near(25,reading.Windows.Single().UsedPercent);Assert.Equal(4,stub.Requests.Count);
    }
    [Check] public static async Task ProfiledBifrostUsesVirtualKeyHeader() {
        var stub=new Stub(request=> {Assert.Equal("fixture-token",request.Headers.GetValues("x-bf-vk").Single());return JsonReply("bifrost-quota");});
        using var client=new HttpClient(stub);var c=Context("bifrost",client);c.Account.ServerAddress="https://gateway.example/v1";
        Assert.True((await ProfiledProviders.TryCreate("bifrost")!.FetchAsync(c)).Success);Assert.Equal("https://gateway.example/api/governance/virtual-keys/quota",stub.Requests.Single().Url);
    }
    [Check] public static async Task ProfiledNousReadsHermesLoginWithoutChangingFile() {
        var home=Path.Combine(Path.GetTempPath(),"PulseNous-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(home,".hermes"));
        const string auth="{\"providers\":{\"nous\":{\"access_token\":\"hermes-fixture\",\"expires_at\":\"2100-01-01T00:00:00Z\",\"portal_base_url\":\"https://portal.nousresearch.com\"}}}";
        var path=Path.Combine(home,".hermes","auth.json");File.WriteAllText(path,auth);
        try{var stub=new Stub(request=>{Assert.Equal("Bearer hermes-fixture",request.Headers.Authorization!.ToString());return JsonReply("nous-portal-account");});using var client=new HttpClient(stub);
            Assert.True((await ProfiledProviders.TryCreate("nousPortal")!.FetchAsync(Context("nousPortal",client,null,home))).Success);Assert.Equal(auth,File.ReadAllText(path));
        }finally{Directory.Delete(home,true);}
    }
    [Check] public static async Task ProfiledManusExtractsOnlySessionCookie() {
        var stub=new Stub(request=>{Assert.Equal("Bearer fixture",request.Headers.Authorization!.ToString());Assert.True(!request.Headers.Contains("Cookie"));Assert.Equal("1",request.Headers.GetValues("Connect-Protocol-Version").Single());return JsonReply("manus-credits");});
        using var client=new HttpClient(stub);Assert.True((await ProfiledProviders.TryCreate("manus")!.FetchAsync(Context("manus",client,"session_id=fixture; analytics=not-sent"))).Success);
    }
    [Check] public static async Task ProfiledJetBrainsDiscoversWindowsRoamingQuotaFile() {
        var home=Path.Combine(Path.GetTempPath(),"PulseJetBrains-"+Guid.NewGuid().ToString("N"));var directory=Path.Combine(home,"AppData","Roaming","JetBrains","IntelliJIdea2026.1","options");Directory.CreateDirectory(directory);
        const string xml="<application><component name='AIAssistantQuotaManager2'><option name='quotaInfo' value='{&quot;current&quot;:25,&quot;maximum&quot;:100}'/></component></application>";
        File.WriteAllText(Path.Combine(directory,"AIAssistantQuotaManager2.xml"),xml);
        try{using var client=new HttpClient(new Stub(_=>throw new Exception("Local quota must not send HTTP")));var reading=await ProfiledProviders.TryCreate("jetBrainsAI")!.FetchAsync(Context("jetBrainsAI",client,null,home));Assert.True(reading.Success);Assert.Near(25,reading.Windows.Single().UsedPercent);Assert.Equal("localQuotaFile",reading.Source);}
        finally{Directory.Delete(home,true);}
    }
    [Check] public static void ProfiledSakanaReadsHtmlQuotaLanes() {
        var c=Context("sakana",new HttpClient());var html="<p>5-hour</p><p>12.5% used</p><p>Weekly</p><p>105% used</p>";
        var reading=ProfiledProviders.Parse(c,html);Assert.True(reading.Success);Assert.Equal(2,reading.Windows.Count);Assert.Near(105,reading.Windows[1].UsedPercent);Assert.True(reading.Windows[1].Exhausted);
    }
    [Check] public static void ProfiledAlibabaRefusalPrecedesAnyCachedQuota() {
        var c=Context("alibabaCodingPlan",new HttpClient());
        var reading=ProfiledProviders.Parse(c,"{\"statusCode\":403,\"message\":\"invalid api key\",\"codingPlanQuotaInfo\":{\"per5HourUsedQuota\":1,\"per5HourTotalQuota\":10}}");
        Assert.Equal("apiKeyRefused",reading.ErrorCode);Assert.Equal(0,reading.Windows.Count);
        Assert.Equal("noPlan",ProfiledProviders.Parse(c,"{\"codingPlanInstanceInfos\":[]}").ErrorCode);
    }
}

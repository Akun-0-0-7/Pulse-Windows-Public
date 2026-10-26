// Official feed addresses and component rules translated from Pulse StatusPage.swift, Apache-2.0.
namespace Pulse.Core;
internal static class StatusPages
{
 public static IReadOnlyList<StatusPageDefinition> Build()
 {
     var pages=new List<StatusPageDefinition>();
     void Add(string id,string root,string company,string format="statuspage",string? shows=null,string? notifies=null,string? feed=null)=>pages.Add(new(id,root,company,format,feed??(format switch{"statuspage"=>root+"/api/v2/summary.json","incidentIO"=>root+"/proxy/"+new Uri(root).Host,"betterStack"=>root+"/index.json","instatus"=>root+"/v2/components.json",_=>root}),shows,notifies));
     Add("claudeCode","https://status.claude.com","Anthropic",notifies:"ids:yyzkbfz2thpt,k8w3r06qmzrp");
     Add("cursor","https://status.cursor.com","Cursor",shows:"except:ids:sm5wkcnqkvr9",notifies:"ids:rflc60xp5jp2,vsny1qv7v86c,mwv1g9sc7kdh");
     Add("grokBot","https://status.cursor.com","Cursor",shows:"ids:sm5wkcnqkvr9");
     Add("copilot","https://www.githubstatus.com","GitHub",shows:"ids:pjmpxvq2cmr2,cnnb39dkkk82");
     Add("devin","https://www.devinstatus.com","Cognition",shows:"except:groups:92j5h0ttqxfb,9ydbk2b55pk1",notifies:"ids:q72cy1kjpk4r,d87cp5jknh1c");
     Add("windsurf","https://status.windsurf.com","Windsurf",shows:"ids:r5wf1ykd7y1m,8q19cygxvshj");
     Add("augment","https://status.augmentcode.com","Augment Code",notifies:"ids:1k3gvg4j889s");
     Add("factory","https://status.factory.ai","Factory",shows:"except:groups:c9sjq8nsz2kq",notifies:"ids:kfqc4mfdkrqx,jfcmwr3grbyc,z1frly8fm1l5");
     Add("kiloCode","https://status.kilo.ai","Kilo",shows:"except:groups:frgc645htq00",notifies:"ids:kdk5t7ljzmm1,rkvq7kvz2zlh");
     Add("minimax","https://status.minimax.io","MiniMax",notifies:"ids:pr0d8qr59svt");
     Add("kimiCode","https://status.moonshot.cn","Moonshot AI",shows:"groups:cyrtlddz0q8c");
     Add("moonshot","https://status.moonshot.cn","Moonshot AI",shows:"groups:87jpctq5cp71",notifies:"ids:8psr5dfdld0s,pjrph68g94yr");
     Add("warp","https://status.warp.dev","Warp",notifies:"ids:z7hlxwbqx7ry,z90zfbttkx9f,7z5yq9wznb4f");
     Add("manus","https://status.manus.im","Manus"); Add("venice","https://veniceai-status.com","Venice",notifies:"ids:6knqw5vybqzk");
     Add("zoomMate","https://www.zoomstatus.com","Zoom",shows:"ids:n1llzwc1cxtr"); Add("replicate","https://www.cloudflarestatus.com","Cloudflare",shows:"ids:fvgfcmy66tdr");
     Add("codex","https://status.openai.com","OpenAI","incidentIO",shows:"groups:Codex"); Add("openAIPlatform","https://status.openai.com","OpenAI","incidentIO",shows:"groups:APIs");
     Add("jetBrainsAI","https://status.jetbrains.cloud","JetBrains","incidentIO",shows:"except:groups:AI Providers",notifies:"ids:01KX3EN535A0SKSZK3S84949V1");
     Add("amp","https://ampcodestatus.com","Amp","incidentIO",shows:"ids:01KBQ3ZK9NMBWKR7DY9H7NQ17D,01M0WN9KPF89TDEHSPQ918XHTH");
     Add("aixy","https://status.aixy-gateway.com","AIXY","incidentIO",notifies:"ids:01M08789J59XQG5AR373SVQZ6Z");
     Add("elevenLabs","https://status.elevenlabs.io","ElevenLabs","incidentIO",shows:"groups:ElevenAPI"); Add("notionAI","https://www.notion-status.com","Notion","incidentIO",shows:"groups:Notion AI");
     Add("perplexity","https://status.perplexity.com","Perplexity","incidentIO",notifies:"ids:01M2R6E5FPASTEW3A3V8TSYHJ0"); Add("poe","https://status.poe.com","Poe","incidentIO");
     Add("v0","https://v0-status.com","Vercel","incidentIO",notifies:"ids:01KHYYTCKK0NS8FN857PCJB63W,01J5EED92VTMVYNWYKYS9B1G1F");
     Add("deepSeek","https://status.deepseek.com","DeepSeek","flashcat",notifies:"name:API");
     Add("huggingFace","https://status.huggingface.co","Hugging Face","betterStack",notifies:"ids:306621,84641");
     Add("chutes","https://status.chutes.ai","Chutes","betterStack",notifies:"ids:8599285"); Add("synthetic","https://status.synthetic.new","Synthetic","betterStack",notifies:"groups:Synthetic - API (Synthetic hosted Models)");
     Add("devPass","https://status.llmgateway.io","LLM Gateway","betterStack",notifies:"ids:8768001"); Add("raycastAI","https://status.raycast.com","Raycast","betterStack",notifies:"ids:5341215"); Add("typeSafe","https://status.typesafe.ai","TypeSafe","betterStack",notifies:"ids:8735312");
     Add("zed","https://status.zed.dev","Zed","instatus",shows:"except:groups:Delta",notifies:"ids:cmaw974wo0001xh7v9vfihlul,cm75bq8t9000rsdcb9bfls8wt");
     Add("gitKraken","https://cloud.gitkrakenstatus.com","GitKraken","instatus",notifies:"ids:clvkz1ojq2917bgo9peyj3bha,clvkz1ok12924bgo956mkaa0r"); Add("atlasCloud","https://atlascloud.instatus.com","Atlas Cloud","instatus");
     Add("commandCode","https://status.commandcode.ai","Command Code","upptime",feed:"https://raw.githubusercontent.com/CommandCodeAI/commandcode-status/master/history/summary.json");
     Add("clinePass","https://status.cline.bot","Cline","gatus",feed:"https://status.cline.bot/api/v1/endpoints/statuses");
     Add("hyper","https://status.charm.sh","Charm","uptimeRobot",shows:"ids:803945156",feed:"https://status.charm.sh/api/getMonitorList/JrppyTBzE3?page=1");
     Add("openCodeGo","https://status.opencode.ai","OpenCode","openCode",notifies:"ids:go",feed:"https://status.opencode.ai/api/status");
     Add("deepInfra","https://status.deepinfra.com","DeepInfra","deepInfra",notifies:"ids:api",feed:"https://status.deepinfra.com/api/v1/status");
     Add("ibmBob","https://status.bob.ibm.com","IBM","ibmBob",notifies:"ids:Chat/Code Completion,Login/SSO",feed:"https://status.bob.ibm.com/api/v1/status");
     Add("gemini","https://status.cloud.google.com","Google Cloud","googleCloud",feed:"https://status.cloud.google.com/incidents.json");
     return pages.AsReadOnly();
 }
}

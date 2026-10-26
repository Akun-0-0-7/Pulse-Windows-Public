// Source IDs and native format evidence: Pulse 1.12.2, Apache-2.0.
namespace Pulse.Core;
public static class SpendSources
{
    // All locations are relative to the explicitly supplied home; never read this process's real home in tests.
    public static IReadOnlyList<SpendSource> All {get;} = Build();
    static IReadOnlyList<SpendSource> Build()
    {
        var rows=new List<SpendSource>();
        void Add(string id,string name,string paths,bool machine=false,bool tokens=true) => rows.Add(new(id,name,machine?"machine":"spec",tokens,paths.Split('|')));
        Add("claude","Claude Code",".claude/projects",true);
        Add("codex","Codex",".codex/sessions|.codex/archived_sessions",true);
        Add("opencode","OpenCode",".local/share/opencode/opencode.db|AppData/Local/opencode/opencode.db",true);
        Add("kilo","Kilo CLI",".local/share/kilo/kilo.db|AppData/Local/kilo/kilo.db",true);
        Add("grok","Grok Build",".grok/sessions",true); Add("kimi","Kimi CLI",".kimi/sessions",true);
        Add("devin-cli","Devin",".local/share/devin/cli/sessions.db|AppData/Local/devin/cli/sessions.db",true);
        Add("pi","Pi",".pi/agent/sessions"); Add("omp","Oh My Pi",".omp/agent/sessions"); Add("senpi","OmO Native",".senpi/agent/sessions");
        Add("kimchi","Kimchi",".config/kimchi/harness/sessions"); Add("prime-agent","Prime Agent",".prime/agent/sessions|.prime/agent/session-artifacts");
        Add("gemini","Gemini CLI",".gemini/tmp"); Add("qwen","Qwen Code",".qwen/projects"); Add("amp","Amp",".local/share/amp/threads|AppData/Local/amp/threads");
        Add("droid","Droid",".factory/sessions"); Add("openclaw","OpenClaw",".openclaw/agents|.clawdbot|.moltbot|.moldbot");
        Add("roocode","Roo Code",Editor("rooveterinaryinc.roo-cline")); Add("kilocode","Kilo Code",Editor("kilocode.kilo-code"));
        Add("cline","Cline",Editor("saoudrizwan.claude-dev")+"|.cline/data/sessions");
        Add("codebuddy","CodeBuddy",".codebuddy"); Add("workbuddy","WorkBuddy",".workbuddy|.workbuddy-ai");
        Add("cherrystudio","Cherry Studio","AppData/Roaming/CherryStudio/Data/Agents/.claude/projects|AppData/Roaming/CherryStudio/.claude/projects");
        Add("commandcode","Command Code",".commandcode/projects|.commandcode/config.json"); Add("opencodereview","OpenCodeReview",".opencodereview/sessions");
        Add("zcode","ZCode",".zcode/projects|.zcode/cli/db/db.sqlite"); Add("hermes","Hermes",".hermes/state.db|.hermes/profiles|AppData/Local/hermes/state.db");
        Add("goose","Goose","AppData/Roaming/goose/sessions/sessions.db|.local/share/goose/sessions/sessions.db|AppData/Roaming/Block/goose/sessions/sessions.db");
        Add("zed","Zed","AppData/Local/Zed/threads/threads.db|AppData/Roaming/Zed/threads/threads.db"); Add("kiro","Kiro",".kiro/sessions/cli");
        Add("crush","Crush",".local/share/crush/projects.json|AppData/Local/crush/projects.json",tokens:false); Add("unsloth","Unsloth",".unsloth/studio/studio.db");
        Add("antigravity-cli","Antigravity CLI",".gemini/antigravity-cli/conversations"); Add("antigravity-ide","Antigravity IDE",".gemini/antigravity/conversations|.gemini/antigravity-ide/conversations");
        Add("micode","MiMo Code",".local/share/mimocode|AppData/Local/mimocode|AppData/Roaming/orca"); Add("devin-desktop","Devin Desktop","AppData/Roaming/Devin/User/acp-events|.config/devin/User/acp-events|.local/share/devin/cli/sessions.db|AppData/Local/devin/cli/sessions.db");
        Add("mux","Mux",".mux/sessions"); Add("codebuff","Codebuff",".config/manicode/projects|.config/manicode-dev/projects|.config/manicode-staging/projects");
        Add("freebuff","Freebuff",".config/manicode/projects|.config/manicode-dev/projects|.config/manicode-staging/projects",tokens:false);
        Add("jcode","JCode",".jcode/sessions"); Add("augment","Augment",".augment/sessions"); Add("gjc","Gajae Code",".gjc/agent/sessions");
        Add("junie","Junie",".junie/sessions"); Add("dsh","DeepSeek Harness",".dsh/sessions"); Add("fx","FX",".fx/sessions");
        Add("lmstudio","LM Studio",".lmstudio/server-logs"); Add("reasonix","Reasonix",".reasonix/stats");
        foreach(var (id,name) in new[]{("cursor","Cursor"),("antigravity","Antigravity (export)"),("trae","Trae"),("warp","Warp"),("hindsight","Hindsight"),("mcode","MiniMax Code")})
            Add(id,name,$".config/tokscale/{id}-cache|AppData/Roaming/Pulse/UsageImports/{id}"+(id=="mcode"?"|.minimax/v2/sessions|.config/tokscale/headless/mcode":id=="hindsight"?"|.hindsight/usage":""),tokens:id!="warp");
        Add("copilot","GitHub Copilot",".copilot/otel|.copilot/data.db|.copilot/session-state|AppData/Roaming/Code/User/workspaceStorage");
        return rows.AsReadOnly();
    }
    static string Editor(string extension)=>string.Join('|',new[]{"Code","Code - Insiders","VSCodium"}.Select(x=>$"AppData/Roaming/{x}/User/globalStorage/{extension}/tasks").Concat([$".vscode-server/data/User/globalStorage/{extension}/tasks"]));
    public static IReadOnlyList<string> ResolvePaths(SpendSource source,string home,IReadOnlyDictionary<string,string>? environment=null)
    {
        environment??=new Dictionary<string,string>();
        string? Env(string key)=>environment.TryGetValue(key,out var text)&&!string.IsNullOrWhiteSpace(text)?text.Trim():null;
        string Resolve(string raw)
        {
            raw=raw.Trim();if(raw=="~")return Path.GetFullPath(home);if(raw.StartsWith("~/",StringComparison.Ordinal)||raw.StartsWith("~\\",StringComparison.Ordinal))raw=Path.Combine(home,raw[2..]);
            raw=System.Text.RegularExpressions.Regex.Replace(raw,"%([^%]+)%",m=>Env(m.Groups[1].Value)??m.Value);return Path.GetFullPath(raw,home);
        }
        string Default(string relative)
        {
            foreach(var (prefix,key) in new[]{("AppData/Local/","LOCALAPPDATA"),("AppData/Roaming/","APPDATA"),(".local/share/","XDG_DATA_HOME"),(".config/tokscale/","TOKSCALE_CONFIG_DIR")})if(relative.StartsWith(prefix,StringComparison.Ordinal)&&Env(key) is {} custom)return Path.Combine(Resolve(custom),relative[prefix.Length..].Replace('/',Path.DirectorySeparatorChar));
            return Path.GetFullPath(Path.Combine(home,relative.Replace('/',Path.DirectorySeparatorChar)));
        }
        var paths=source.Paths.Select(Default).ToList();
        var overrideKey=source.Id switch {"hermes"=>"HERMES_HOME","unsloth"=>"UNSLOTH_STUDIO_HOME","jcode"=>"JCODE_HOME","dsh"=>"DSH_HOME","lmstudio"=>"LM_STUDIO_HOME","reasonix"=>"REASONIX_HOME","gemini" or "antigravity-cli" or "antigravity-ide"=>"GEMINI_CLI_HOME",_=>null};
        if(overrideKey is not null&&Env(overrideKey) is {} value)
        {
            var suffix=source.Id switch {"hermes"=>"state.db","unsloth"=>"studio.db","lmstudio"=>"server-logs","reasonix"=>"stats","gemini"=>"tmp","antigravity-cli"=>"antigravity-cli/conversations","antigravity-ide"=>"antigravity/conversations",_=>"sessions"};
            paths=[Path.Combine(Resolve(value),suffix.Replace('/',Path.DirectorySeparatorChar))];
            if(source.Id=="hermes")paths.Add(Path.Combine(Resolve(value),"profiles"));
            if(source.Id=="antigravity-ide")paths.Add(Path.Combine(Resolve(value),"antigravity-ide","conversations"));
        }
        if(source.Id=="senpi"){if(Env("SENPI_CODING_AGENT_DIR") is {} agent)paths=[Path.Combine(Resolve(agent),"sessions")];if(Env("SENPI_CODING_AGENT_SESSION_DIR") is {} session)paths.Add(Resolve(session));}
        if(source.Id=="kimchi"&&Env("KIMCHI_CODING_AGENT_DIR") is {} kimchi)paths=[Path.Combine(Resolve(kimchi),"sessions")];
        if(source.Id=="prime-agent")
        {
            if((Env("PRIME_AGENT_SESSION_DIR")??Env("PRIME_AGENT_CODING_AGENT_SESSION_DIR")) is {} sessions){var resolved=Resolve(sessions);paths=[resolved,Path.Combine(Path.GetDirectoryName(resolved)!,"session-artifacts")];}
            else if(Env("PRIME_AGENT_CODING_AGENT_DIR") is {} agent)paths=[Path.Combine(Resolve(agent),"sessions"),Path.Combine(Resolve(agent),"session-artifacts")];
        }
        if(source.Id=="cline")
        {
            var cli=Env("CLINE_SESSION_DATA_DIR") is {} session?Resolve(session):Env("CLINE_DATA_DIR") is {} data?Path.Combine(Resolve(data),"sessions"):Env("CLINE_DIR") is {} root?Path.Combine(Resolve(root),"data","sessions"):null;
            if(cli is not null){paths.Remove(Default(".cline/data/sessions"));paths.Add(cli);}
        }
        if(source.Id is "codebuff" or "freebuff"&&Env(source.Id=="codebuff"?"CODEBUFF_DATA_DIR":"FREEBUFF_DATA_DIR") is {} codebuff)paths=[Resolve(codebuff)];
        if(source.Id=="hindsight"&&Env("HINDSIGHT_HOME") is {} hindsight){paths.Remove(Default(".hindsight/usage"));paths.Add(Path.Combine(Resolve(hindsight),"usage"));}
        if(source.Id=="goose"&&Env("GOOSE_PATH_ROOT") is {} goose)paths.Insert(0,Path.Combine(Resolve(goose),"data","sessions","sessions.db"));
        if(source.Id=="reasonix"&&Env("REASONIX_STATE_HOME") is {} state)paths=[Resolve(state)];
        if(source.Id=="gjc")
        {
            if(Env("GJC_CODING_AGENT_DIR") is {} agent)paths=[Path.Combine(Resolve(agent),"sessions")];
            foreach(var key in new[]{"GJC_CONFIG_DIR","PI_CONFIG_DIR"})if(Env(key) is {} root)paths.Add(Path.Combine(Resolve(root),"agent","sessions"));
            paths.Add(Default(".local/share/gjc/sessions"));
        }
        return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}

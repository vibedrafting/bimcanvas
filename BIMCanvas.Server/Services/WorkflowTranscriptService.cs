using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace BIMCanvas.Server.Services
{
    /// <summary>
    /// Workflow transcript 读取服务（Task 页 CLI 风 phase 树 + per-agent 详情）。
    ///
    /// 两种数据态：
    ///  - 完成态(权威)：<c>{session}/workflows/wf_{runId}.json</c>——orchestrator 跑完写出的运行态，
    ///    含 phases[] + workflowProgress[]（每 agent 的 label/phaseIndex/model/tokens/toolCalls/durationMs）。
    ///    据此组装精确分组的 phase 树。
    ///  - 运行态(实时)：上面那个文件只在完成瞬间才写，运行中读不到；但
    ///    <c>{session}/subagents/workflows/{runId}/</c> 在启动时就建好、随 agent 执行**增量写**
    ///    （agent-*.jsonl + journal.jsonl，已用 mtime 实测坐实）。运行中据此读出 per-agent 实时态；
    ///    phase 列表从启动即写好的脚本 <c>{session}/workflows/scripts/*{runId}.js</c> 的 meta.phases 取。
    ///    运行中拿不到权威的 per-agent→phase 归属（只在完成 wf_*.json 里），故返回 live + 扁平 liveAgents，
    ///    前端用「阶段步进条 + 扁平 agent 列表」呈现；完成后切换到精确分组树。
    ///
    /// 全程 Newtonsoft（禁 STJ）。仅 Web 按需/心跳请求时调用。
    /// </summary>
    public class WorkflowTranscriptService
    {
        private readonly ILogger<WorkflowTranscriptService> _logger;

        public WorkflowTranscriptService(ILogger<WorkflowTranscriptService> logger)
        {
            _logger = logger;
        }

        private static string ClaudeProjectsRoot =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".claude", "projects");

        public WorkflowTranscriptResult GetTranscript(string sdkSessionId, string? taskId)
        {
            var result = new WorkflowTranscriptResult { SdkSessionId = sdkSessionId };

            var sessionDir = ResolveSessionDir(sdkSessionId);
            if (sessionDir == null)
            {
                _logger.LogInformation("Workflow transcript: 未找到会话目录 sdkSessionId={SessionId}", sdkSessionId);
                return result;
            }

            // 完成态优先：找到本次 run 的权威 wf_*.json
            var runJsonPath = PickRunJson(Path.Combine(sessionDir, "workflows"), taskId, sessionDir);
            if (runJsonPath != null)
            {
                try
                {
                    BuildFromRunJson(JObject.Parse(File.ReadAllText(runJsonPath)), sessionDir, result);
                    return result;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Workflow transcript: 解析 {Path} 失败，转实时态", runJsonPath);
                }
            }

            // 运行态：读增量子 agent transcript
            try { BuildLive(sessionDir, result); }
            catch (Exception ex) { _logger.LogWarning(ex, "Workflow transcript: 实时态读取失败"); }
            return result;
        }

        // ============ 完成态（权威） ============
        private void BuildFromRunJson(JObject root, string sessionDir, WorkflowTranscriptResult result)
        {
            result.RunId = (string?)root["runId"];
            result.WorkflowName = (string?)root["workflowName"];
            result.Summary = (string?)root["summary"];
            result.Status = (string?)root["status"];
            result.DurationMs = (long?)root["durationMs"];
            result.TotalTokens = (int?)root["totalTokens"];
            result.AgentCount = (int?)root["agentCount"];

            var phases = new List<WorkflowPhase>();
            if (root["phases"] is JArray phaseArr)
            {
                int idx = 1;
                foreach (var p in phaseArr.OfType<JObject>())
                {
                    phases.Add(new WorkflowPhase { Index = idx++, Title = (string?)p["title"] ?? "", Detail = (string?)p["detail"] });
                }
            }

            var subDir = result.RunId != null ? Path.Combine(sessionDir, "subagents", "workflows", result.RunId) : null;
            var outcomes = (subDir != null && Directory.Exists(subDir)) ? ReadJournalResults(subDir) : new Dictionary<string, string>();

            var byPhase = new Dictionary<int, List<WorkflowTranscriptAgent>>();
            var orphans = new List<WorkflowTranscriptAgent>();
            if (root["workflowProgress"] is JArray prog)
            {
                foreach (var e in prog.OfType<JObject>())
                {
                    if ((string?)e["type"] != "workflow_agent") continue;
                    var agent = new WorkflowTranscriptAgent
                    {
                        AgentId = (string?)e["agentId"] ?? "",
                        Label = (string?)e["label"],
                        Model = (string?)e["model"],
                        State = (string?)e["state"],
                        Tokens = (int?)e["tokens"],
                        ToolCalls = (int?)e["toolCalls"],
                        DurationMs = (long?)e["durationMs"]
                    };
                    if (subDir != null && !string.IsNullOrEmpty(agent.AgentId))
                    {
                        try { EnrichAgentDetail(agent, subDir); } catch (Exception ex) { _logger.LogWarning(ex, "enrich {Id}", agent.AgentId); }
                        if (string.IsNullOrEmpty(agent.Outcome) && outcomes.TryGetValue(agent.AgentId, out var oc)) agent.Outcome = oc;
                    }
                    var phaseIdx = (int?)e["phaseIndex"] ?? 0;
                    if (phaseIdx >= 1) { if (!byPhase.TryGetValue(phaseIdx, out var l)) { l = new(); byPhase[phaseIdx] = l; } l.Add(agent); }
                    else orphans.Add(agent);
                }
            }

            if (phases.Count == 0)
            {
                var all = byPhase.Values.SelectMany(x => x).Concat(orphans).ToList();
                if (all.Count > 0) phases.Add(new WorkflowPhase { Index = 1, Title = "", Agents = all });
            }
            else
            {
                foreach (var ph in phases) if (byPhase.TryGetValue(ph.Index, out var l)) ph.Agents = l;
                if (orphans.Count > 0) phases[0].Agents.AddRange(orphans);
            }
            result.Phases = phases;
        }

        // ============ 运行态（实时·增量 transcript） ============
        private void BuildLive(string sessionDir, WorkflowTranscriptResult result)
        {
            var subRoot = Path.Combine(sessionDir, "subagents", "workflows");
            if (!Directory.Exists(subRoot)) return;
            var runDir = Directory.EnumerateDirectories(subRoot, "wf_*")
                .OrderByDescending(d => Directory.GetLastWriteTimeUtc(d))
                .FirstOrDefault();
            if (runDir == null) return;

            result.Live = true;
            result.Status = "running";
            result.RunId = Path.GetFileName(runDir);

            // 脚本(启动即写)取 meta.name/description/phases
            var scriptsDir = Path.Combine(sessionDir, "workflows", "scripts");
            var scriptPath = Directory.Exists(scriptsDir)
                ? Directory.EnumerateFiles(scriptsDir, $"*{result.RunId}.js").FirstOrDefault()
                : null;
            if (scriptPath != null)
            {
                try
                {
                    var script = File.ReadAllText(scriptPath);
                    result.WorkflowName ??= MatchString(script, @"name:\s*['""]([^'""]+)['""]");
                    result.Summary ??= MatchString(script, @"description:\s*['""]([^'""]+)['""]");
                    result.Phases = ParseScriptPhases(script);
                }
                catch (Exception ex) { _logger.LogWarning(ex, "解析脚本失败 {Path}", scriptPath); }
            }

            // journal：started=已启动，result=已完成(含 outcome)
            var started = ReadJournalStarted(runDir);
            var results = ReadJournalResults(runDir);

            var liveAgents = new List<WorkflowTranscriptAgent>();
            foreach (var file in Directory.EnumerateFiles(runDir, "agent-*.jsonl"))
            {
                var agentId = ExtractAgentId(file);
                var agent = new WorkflowTranscriptAgent { AgentId = agentId };
                try { EnrichAgentDetail(agent, runDir); } catch (Exception ex) { _logger.LogWarning(ex, "live enrich {Id}", agentId); }
                if (string.IsNullOrEmpty(agent.Outcome) && results.TryGetValue(agentId, out var oc)) agent.Outcome = oc;
                agent.State = results.ContainsKey(agentId) ? "done" : (started.Contains(agentId) ? "running" : "running");
                agent.Label = LabelFromOutcomeOrPrompt(agent.Outcome, agent.Prompt) ?? agentId;
                liveAgents.Add(agent);
            }
            // 让 done 的排前面、稳定可读
            liveAgents = liveAgents.OrderBy(a => a.State == "done" ? 0 : 1).ToList();

            result.LiveAgents = liveAgents;
            result.AgentCount = liveAgents.Count;
            result.TotalTokens = liveAgents.Sum(a => a.Tokens ?? 0);
        }

        // ============ 公共辅助 ============
        private static string? ResolveSessionDir(string sdkSessionId)
        {
            var root = ClaudeProjectsRoot;
            if (string.IsNullOrWhiteSpace(sdkSessionId) || !Directory.Exists(root)) return null;
            foreach (var projectDir in Directory.EnumerateDirectories(root))
            {
                var candidate = Path.Combine(projectDir, sdkSessionId);
                if (Directory.Exists(candidate)) return candidate;
            }
            return null;
        }

        /// <summary>
        /// 选完成态 wf_*.json。
        /// - 给了 taskId(常规路径)：只认 taskId 匹配的；没匹配(本次 run 完成文件还没写出)→ null → 走实时态。
        /// - 没给 taskId(兜底)：取最新完成文件，但**仅当**其 runId 同时是最新活动的 run(subagents/workflows
        ///   下最新 run 目录)——即没有更新的 run 在跑。否则同会话新 run 启动期会错读上一个已完成 run 的
        ///   wf_*.json，把仍在跑的新 run 误报 completed(4b2f4edc 防的 latent bug)，此时返回 null 走实时态。
        ///   这条兜底是为修复"实时进度 SSE 漏传 taskId → 前端完成态查询无 taskId → 永久卡实时态"而加，
        ///   前端已优先从 Workflow 工具结果绑 taskId(bindWorkflowIdentity)，本兜底只在 taskId 仍缺失时生效。
        /// </summary>
        private static string? PickRunJson(string workflowsDir, string? taskId, string sessionDir)
        {
            if (!Directory.Exists(workflowsDir)) return null;
            var files = Directory.EnumerateFiles(workflowsDir, "wf_*.json", SearchOption.TopDirectoryOnly).ToList();
            if (files.Count == 0) return null;

            if (!string.IsNullOrEmpty(taskId))
            {
                foreach (var f in files)
                {
                    try { if (string.Equals((string?)JObject.Parse(File.ReadAllText(f))["taskId"], taskId, StringComparison.Ordinal)) return f; }
                    catch { }
                }
                return null; // taskId 指定但本次 run 还没写出 → 走实时态
            }

            // 无 taskId 安全兜底：最新完成文件，且其 runId == 最新活动 run（无更新的 run 在跑）
            var newestJson = files.OrderByDescending(File.GetLastWriteTimeUtc).First();
            var subRoot = Path.Combine(sessionDir, "subagents", "workflows");
            if (Directory.Exists(subRoot))
            {
                var newestRunDir = Directory.EnumerateDirectories(subRoot, "wf_*")
                    .OrderByDescending(Directory.GetLastWriteTimeUtc).FirstOrDefault();
                if (newestRunDir != null)
                {
                    var newestRunId = Path.GetFileName(newestRunDir);
                    string? jsonRunId = null;
                    try { jsonRunId = (string?)JObject.Parse(File.ReadAllText(newestJson))["runId"]; } catch { }
                    // 有更新的 run 在跑（最新完成文件 ≠ 最新活动 run）→ 不认完成态，交 BuildLive
                    if (jsonRunId == null || !string.Equals(jsonRunId, newestRunId, StringComparison.Ordinal))
                        return null;
                }
            }
            return newestJson;
        }

        private static HashSet<string> ReadJournalStarted(string subDir)
        {
            var set = new HashSet<string>();
            var journal = Path.Combine(subDir, "journal.jsonl");
            if (!File.Exists(journal)) return set;
            foreach (var line in SafeReadLines(journal))
            {
                JObject obj; try { obj = JObject.Parse(line); } catch { continue; }
                if ((string?)obj["type"] == "started")
                {
                    var id = (string?)obj["agentId"];
                    if (!string.IsNullOrEmpty(id)) set.Add(id!);
                }
            }
            return set;
        }

        private static Dictionary<string, string> ReadJournalResults(string subDir)
        {
            var map = new Dictionary<string, string>();
            var journal = Path.Combine(subDir, "journal.jsonl");
            if (!File.Exists(journal)) return map;
            foreach (var line in SafeReadLines(journal))
            {
                JObject obj; try { obj = JObject.Parse(line); } catch { continue; }
                if ((string?)obj["type"] != "result") continue;
                var id = (string?)obj["agentId"];
                if (string.IsNullOrEmpty(id)) continue;
                var token = obj["result"];
                if (token != null && token.Type != JTokenType.Null)
                {
                    map[id!] = token.Type == JTokenType.String ? token.ToString() : token.ToString(Newtonsoft.Json.Formatting.Indented);
                }
            }
            return map;
        }

        /// <summary>从 agent-{agentId}.jsonl 补 model/tokens/toolCalls/prompt/activity/outcome（增量文件，运行中可读）。</summary>
        private static void EnrichAgentDetail(WorkflowTranscriptAgent agent, string subDir)
        {
            var file = Path.Combine(subDir, $"agent-{agent.AgentId}.jsonl");
            if (!File.Exists(file)) return;

            int input = 0, output = 0; bool sawTokens = false; int toolCount = 0;
            string? lastStructured = null, lastText = null, model = null;

            foreach (var line in SafeReadLines(file))
            {
                JObject obj; try { obj = JObject.Parse(line); } catch { continue; }
                var type = (string?)obj["type"];
                var message = obj["message"] as JObject;
                if (message == null) continue;

                if (type == "user")
                {
                    if (string.IsNullOrEmpty(agent.Prompt)) agent.Prompt = ExtractText(message["content"]);
                }
                else if (type == "assistant")
                {
                    var m = (string?)message["model"];
                    if (!string.IsNullOrEmpty(m)) model = m;
                    if (message["usage"] is JObject usage)
                    {
                        input += (int?)usage["input_tokens"] ?? 0;
                        output += (int?)usage["output_tokens"] ?? 0;
                        sawTokens = true;
                    }
                    if (message["content"] is JArray blocks)
                    {
                        foreach (var b in blocks.OfType<JObject>())
                        {
                            var bt = (string?)b["type"];
                            if (bt == "tool_use")
                            {
                                var name = (string?)b["name"] ?? "tool";
                                toolCount++;
                                agent.Tools.Add(new WorkflowTranscriptTool { Name = name, Input = SummarizeInput(b["input"]) });
                                if (name == "StructuredOutput") lastStructured = b["input"]?.ToString(Newtonsoft.Json.Formatting.Indented);
                            }
                            else if (bt == "text")
                            {
                                var t = (string?)b["text"];
                                if (!string.IsNullOrWhiteSpace(t)) lastText = t;
                            }
                        }
                    }
                }
            }

            agent.Model ??= model;                       // 完成态已从 wf_json 设过 → 不覆盖
            if (sawTokens) agent.Tokens ??= input + output;
            agent.ToolCalls ??= toolCount;
            if (string.IsNullOrEmpty(agent.Outcome)) agent.Outcome = lastStructured ?? lastText;
        }

        private static List<WorkflowPhase> ParseScriptPhases(string script)
        {
            var phases = new List<WorkflowPhase>();
            var pm = Regex.Match(script, @"phases:\s*\[(.*?)\]", RegexOptions.Singleline);
            if (!pm.Success) return phases;
            int idx = 1;
            foreach (Match m in Regex.Matches(pm.Groups[1].Value, @"\{[^}]*\}"))
            {
                var title = MatchString(m.Value, @"title:\s*['""]([^'""]+)['""]");
                var detail = MatchString(m.Value, @"detail:\s*['""]([^'""]+)['""]");
                if (!string.IsNullOrEmpty(title)) phases.Add(new WorkflowPhase { Index = idx++, Title = title!, Detail = detail });
            }
            return phases;
        }

        private static string? LabelFromOutcomeOrPrompt(string? outcome, string? prompt)
        {
            // outcome JSON 的业务标识键（含 name 优先，再 slug/id/zoneId…）
            if (!string.IsNullOrWhiteSpace(outcome))
            {
                try
                {
                    var o = JObject.Parse(outcome);
                    var nameProp = o.Properties().FirstOrDefault(p => p.Value.Type == JTokenType.String && p.Name.IndexOf("name", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (nameProp != null) { var v = nameProp.Value.ToString().Trim(); if (!string.IsNullOrEmpty(v)) return Trunc(v); }
                    foreach (var k in new[] { "slug", "id", "zoneId", "targetId", "variant", "title", "key" })
                    {
                        var p = o.Properties().FirstOrDefault(x => x.Value.Type == JTokenType.String && string.Equals(x.Name, k, StringComparison.OrdinalIgnoreCase));
                        if (p != null) { var v = p.Value.ToString().Trim(); if (!string.IsNullOrEmpty(v)) return Trunc(v); }
                    }
                }
                catch { }
            }
            // prompt 里标识符样式引号 token（排除空格/逗号/等号，避免误配跨值片段）
            if (!string.IsNullOrWhiteSpace(prompt))
            {
                var m = Regex.Match(prompt, "[\"'「“]([^\"'」”\n=,\\s]{1,40})[\"'」”]");
                if (m.Success && !string.IsNullOrWhiteSpace(m.Groups[1].Value)) return m.Groups[1].Value.Trim();
                // 无引号标识符时退到 prompt 首行截断（强于裸 agentId 哈希；完成后会被 wf_*.json 的 label 校正）
                var firstLine = prompt.Split('\n').FirstOrDefault()?.Trim();
                if (!string.IsNullOrEmpty(firstLine)) return firstLine!.Length > 24 ? firstLine.Substring(0, 24) + "…" : firstLine;
            }
            return null;
        }

        private static string Trunc(string v) => v.Length > 48 ? v.Substring(0, 48) : v;

        private static string? MatchString(string text, string pattern)
        {
            var m = Regex.Match(text, pattern);
            return m.Success ? m.Groups[1].Value : null;
        }

        private static string ExtractAgentId(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            const string prefix = "agent-";
            return name.StartsWith(prefix, StringComparison.Ordinal) ? name.Substring(prefix.Length) : name;
        }

        private static IEnumerable<string> SafeReadLines(string path)
        {
            // 增量文件可能正被写入；逐行读、容忍尾部半行（调用方 per-line try/catch）
            string[] lines;
            try { lines = File.ReadAllLines(path); }
            catch { yield break; }
            foreach (var l in lines) if (!string.IsNullOrWhiteSpace(l)) yield return l;
        }

        private static string? ExtractText(JToken? content)
        {
            if (content == null) return null;
            if (content.Type == JTokenType.String) return content.ToString();
            if (content is JArray arr)
            {
                var parts = arr.OfType<JObject>().Where(b => (string?)b["type"] == "text").Select(b => (string?)b["text"]).Where(t => !string.IsNullOrWhiteSpace(t));
                var joined = string.Join("\n", parts);
                return string.IsNullOrWhiteSpace(joined) ? null : joined;
            }
            return null;
        }

        private static string? SummarizeInput(JToken? input)
        {
            if (input == null || input.Type == JTokenType.Null) return null;
            var s = input.ToString(Newtonsoft.Json.Formatting.None);
            return s.Length > 160 ? s.Substring(0, 160) + "…" : s;
        }
    }

    public class WorkflowTranscriptResult
    {
        public string SdkSessionId { get; set; } = "";
        public string? RunId { get; set; }
        public string? WorkflowName { get; set; }
        public string? Summary { get; set; }
        public string? Status { get; set; }
        public long? DurationMs { get; set; }
        public int? TotalTokens { get; set; }
        public int? AgentCount { get; set; }
        public bool Live { get; set; }
        public List<WorkflowPhase> Phases { get; set; } = new();
        public List<WorkflowTranscriptAgent> LiveAgents { get; set; } = new();
    }

    public class WorkflowPhase
    {
        public int Index { get; set; }
        public string Title { get; set; } = "";
        public string? Detail { get; set; }
        public List<WorkflowTranscriptAgent> Agents { get; set; } = new();
    }

    public class WorkflowTranscriptAgent
    {
        public string AgentId { get; set; } = "";
        public string? Label { get; set; }
        public string? Model { get; set; }
        public string? State { get; set; }
        public int? Tokens { get; set; }
        public int? ToolCalls { get; set; }
        public long? DurationMs { get; set; }
        public string? Prompt { get; set; }
        public string? Outcome { get; set; }
        public List<WorkflowTranscriptTool> Tools { get; set; } = new();
    }

    public class WorkflowTranscriptTool
    {
        public string Name { get; set; } = "";
        public string? Input { get; set; }
    }
}

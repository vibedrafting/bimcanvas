using Newtonsoft.Json.Linq;

namespace BIMCanvas.Server.Services
{
    /// <summary>
    /// Workflow transcript 读取服务（Task 页 CLI 风 phase 树 + per-agent 详情）。
    ///
    /// 权威数据源 = orchestrator 运行态文件
    ///   <c>~/.claude/projects/{projectId}/{sdkSessionId}/workflows/wf_{runId}.json</c>
    /// 它含 CLI 渲染所需全部信息：phases[]（阶段声明）+ workflowProgress[]（每 agent 的
    /// label / phaseIndex / phaseTitle / agentId / model / state / tokens / toolCalls / durationMs）
    /// + 汇总（workflowName / summary / status / durationMs / totalTokens / agentCount）。
    ///
    /// per-agent 的 prompt / activity / outcome 再从子 agent transcript 补：
    ///   <c>{sessionDir}/subagents/workflows/{runId}/agent-{agentId}.jsonl</c>（prompt + tool_use）
    ///   <c>.../journal.jsonl</c>（result = outcome）。
    ///
    /// 全程 Newtonsoft（禁 STJ）。仅 Web 按需请求时调用，绝不轮询。
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

        /// <summary>
        /// 读取指定 sdkSessionId 下的 workflow 运行态，组装成 phase 树。
        /// taskId 用于在一个 session 有多次 workflow 时精确定位（缺省取最新一次）。
        /// 未找到时返回空 phases 列表（不抛）。
        /// </summary>
        public WorkflowTranscriptResult GetTranscript(string sdkSessionId, string? taskId)
        {
            var result = new WorkflowTranscriptResult { SdkSessionId = sdkSessionId };

            var sessionDir = ResolveSessionDir(sdkSessionId);
            if (sessionDir == null)
            {
                _logger.LogInformation("Workflow transcript: 未找到会话目录 sdkSessionId={SessionId}", sdkSessionId);
                return result;
            }

            var workflowsDir = Path.Combine(sessionDir, "workflows");
            var runJsonPath = PickRunJson(workflowsDir, taskId);
            if (runJsonPath == null)
            {
                return result;
            }

            JObject root;
            try { root = JObject.Parse(File.ReadAllText(runJsonPath)); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Workflow transcript: 解析 {Path} 失败", runJsonPath);
                return result;
            }

            result.RunId = (string?)root["runId"];
            result.WorkflowName = (string?)root["workflowName"];
            result.Summary = (string?)root["summary"];
            result.Status = (string?)root["status"];
            result.DurationMs = (long?)root["durationMs"];
            result.TotalTokens = (int?)root["totalTokens"];
            result.AgentCount = (int?)root["agentCount"];

            // 声明的 phase 列表（保序）
            var phases = new List<WorkflowPhase>();
            if (root["phases"] is JArray phaseArr)
            {
                int idx = 1;
                foreach (var p in phaseArr.OfType<JObject>())
                {
                    phases.Add(new WorkflowPhase
                    {
                        Index = idx++,
                        Title = (string?)p["title"] ?? "",
                        Detail = (string?)p["detail"]
                    });
                }
            }

            // 子 agent transcript 目录（同 runId）
            var subDir = result.RunId != null
                ? Path.Combine(sessionDir, "subagents", "workflows", result.RunId)
                : null;
            var outcomes = (subDir != null && Directory.Exists(subDir))
                ? ReadJournalOutcomes(subDir)
                : new Dictionary<string, string>();

            // workflowProgress 里的 workflow_agent 条目 → 按 phaseIndex 归组
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
                        try { EnrichAgentDetail(agent, subDir); }
                        catch (Exception ex) { _logger.LogWarning(ex, "enrich agent {Id} 失败", agent.AgentId); }
                        if (string.IsNullOrEmpty(agent.Outcome) && outcomes.TryGetValue(agent.AgentId, out var oc))
                        {
                            agent.Outcome = oc;
                        }
                    }

                    var phaseIdx = (int?)e["phaseIndex"] ?? 0;
                    if (phaseIdx >= 1)
                    {
                        if (!byPhase.TryGetValue(phaseIdx, out var list)) { list = new(); byPhase[phaseIdx] = list; }
                        list.Add(agent);
                    }
                    else
                    {
                        orphans.Add(agent);
                    }
                }
            }

            if (phases.Count == 0)
            {
                // 未声明 phase：所有 agent 归一个匿名阶段（CLI 单阶段形态）
                var all = byPhase.Values.SelectMany(x => x).Concat(orphans).ToList();
                if (all.Count > 0)
                {
                    phases.Add(new WorkflowPhase { Index = 1, Title = "", Agents = all });
                }
            }
            else
            {
                foreach (var ph in phases)
                {
                    if (byPhase.TryGetValue(ph.Index, out var list)) ph.Agents = list;
                }
                if (orphans.Count > 0) phases[0].Agents.AddRange(orphans);
            }

            result.Phases = phases;
            return result;
        }

        /// <summary>在 projects/* 下找名为 sdkSessionId 的会话目录。</summary>
        private static string? ResolveSessionDir(string sdkSessionId)
        {
            var root = ClaudeProjectsRoot;
            if (string.IsNullOrWhiteSpace(sdkSessionId) || !Directory.Exists(root))
            {
                return null;
            }
            foreach (var projectDir in Directory.EnumerateDirectories(root))
            {
                var candidate = Path.Combine(projectDir, sdkSessionId);
                if (Directory.Exists(candidate)) return candidate;
            }
            return null;
        }

        /// <summary>选 wf_*.json：优先 taskId 匹配，否则取最新修改的一个（排除 scripts 子目录）。</summary>
        private static string? PickRunJson(string workflowsDir, string? taskId)
        {
            if (!Directory.Exists(workflowsDir)) return null;
            var files = Directory.EnumerateFiles(workflowsDir, "wf_*.json", SearchOption.TopDirectoryOnly).ToList();
            if (files.Count == 0) return null;

            if (!string.IsNullOrEmpty(taskId))
            {
                foreach (var f in files)
                {
                    try
                    {
                        var obj = JObject.Parse(File.ReadAllText(f));
                        if (string.Equals((string?)obj["taskId"], taskId, StringComparison.Ordinal))
                        {
                            return f;
                        }
                    }
                    catch { /* skip malformed */ }
                }
            }
            return files.OrderByDescending(f => File.GetLastWriteTimeUtc(f)).First();
        }

        /// <summary>读 journal.jsonl，建立 agentId -> outcome（result 文本）映射。</summary>
        private static Dictionary<string, string> ReadJournalOutcomes(string subDir)
        {
            var map = new Dictionary<string, string>();
            var journal = Path.Combine(subDir, "journal.jsonl");
            if (!File.Exists(journal)) return map;
            foreach (var line in File.ReadLines(journal))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                JObject obj;
                try { obj = JObject.Parse(line); } catch { continue; }
                if ((string?)obj["type"] != "result") continue;
                var agentId = (string?)obj["agentId"];
                if (string.IsNullOrEmpty(agentId)) continue;
                var token = obj["result"];
                if (token != null && token.Type != JTokenType.Null)
                {
                    map[agentId!] = token.Type == JTokenType.String
                        ? token.ToString()
                        : token.ToString(Newtonsoft.Json.Formatting.Indented);
                }
            }
            return map;
        }

        /// <summary>从 agent-{agentId}.jsonl 补 prompt + activity(tool 名列表) + outcome 兜底。</summary>
        private static void EnrichAgentDetail(WorkflowTranscriptAgent agent, string subDir)
        {
            var file = Path.Combine(subDir, $"agent-{agent.AgentId}.jsonl");
            if (!File.Exists(file)) return;

            string? lastStructured = null;
            string? lastText = null;

            foreach (var line in File.ReadLines(file))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                JObject obj;
                try { obj = JObject.Parse(line); } catch { continue; }

                var type = (string?)obj["type"];
                var message = obj["message"] as JObject;
                if (message == null) continue;

                if (type == "user")
                {
                    if (string.IsNullOrEmpty(agent.Prompt))
                    {
                        agent.Prompt = ExtractText(message["content"]);
                    }
                }
                else if (type == "assistant" && message["content"] is JArray blocks)
                {
                    foreach (var block in blocks.OfType<JObject>())
                    {
                        var btype = (string?)block["type"];
                        if (btype == "tool_use")
                        {
                            var name = (string?)block["name"] ?? "tool";
                            agent.Tools.Add(new WorkflowTranscriptTool
                            {
                                Name = name,
                                Input = SummarizeInput(block["input"])
                            });
                            if (name == "StructuredOutput")
                            {
                                lastStructured = block["input"]?.ToString(Newtonsoft.Json.Formatting.Indented);
                            }
                        }
                        else if (btype == "text")
                        {
                            var t = (string?)block["text"];
                            if (!string.IsNullOrWhiteSpace(t)) lastText = t;
                        }
                    }
                }
            }

            agent.Outcome = lastStructured ?? lastText;
        }

        private static string? ExtractText(JToken? content)
        {
            if (content == null) return null;
            if (content.Type == JTokenType.String) return content.ToString();
            if (content is JArray arr)
            {
                var parts = arr.OfType<JObject>()
                    .Where(b => (string?)b["type"] == "text")
                    .Select(b => (string?)b["text"])
                    .Where(t => !string.IsNullOrWhiteSpace(t));
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
        public List<WorkflowPhase> Phases { get; set; } = new();
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

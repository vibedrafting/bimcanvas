using Newtonsoft.Json.Linq;

namespace BIMCanvas.Server.Services
{
    /// <summary>
    /// Workflow transcript 读取服务（Task 页 tier C 完成详情）。
    ///
    /// 数据源：Claude Agent SDK / bundled CLI 把 workflow 子 agent 的完整执行流落盘到
    /// <c>~/.claude/projects/{projectId}/{sdkSessionId}/subagents/workflows/wf_*/</c> 下：
    ///   - <c>agent-*.jsonl</c>：每行一个 turn（user/assistant），含 model / usage / content[tool_use,thinking,text]
    ///   - <c>journal.jsonl</c>：started/result 事件，result 是 StructuredOutput 结果
    ///
    /// 本服务按 sdkSessionId 定位会话目录（不复刻 Claude 的 projectId 编码方案，直接在
    /// projects/* 下找名为 sdkSessionId 的子目录），逐行读 jsonl（Newtonsoft，禁 STJ），
    /// 按 agentId 聚合 per-agent 详情。仅在 Web 按需请求时调用，绝不轮询。
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
        /// 读取并聚合指定 sdkSessionId 下所有 workflow 子 agent 的 transcript。
        /// 未找到会话目录 / 无 workflow 时返回空 agents 列表（不抛）。
        /// </summary>
        public WorkflowTranscriptResult GetTranscript(string sdkSessionId)
        {
            var result = new WorkflowTranscriptResult { SdkSessionId = sdkSessionId };

            var sessionDir = ResolveSessionDir(sdkSessionId);
            if (sessionDir == null)
            {
                _logger.LogInformation("Workflow transcript: 未找到会话目录 sdkSessionId={SessionId}", sdkSessionId);
                return result;
            }

            var workflowsRoot = Path.Combine(sessionDir, "subagents", "workflows");
            if (!Directory.Exists(workflowsRoot))
            {
                return result;
            }

            foreach (var wfDir in Directory.EnumerateDirectories(workflowsRoot, "wf_*"))
            {
                // 先收 journal 的 outcome（agentId -> result 文本）
                var outcomes = ReadJournalOutcomes(wfDir);

                foreach (var agentFile in Directory.EnumerateFiles(wfDir, "agent-*.jsonl"))
                {
                    try
                    {
                        var agent = ParseAgentFile(agentFile);
                        if (agent == null)
                        {
                            continue;
                        }
                        if (string.IsNullOrEmpty(agent.Outcome) && outcomes.TryGetValue(agent.AgentId, out var outcome))
                        {
                            agent.Outcome = outcome;
                        }
                        result.Agents.Add(agent);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Workflow transcript: 解析 agent 文件失败 {File}", agentFile);
                    }
                }
            }

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
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }
            return null;
        }

        /// <summary>读 journal.jsonl，建立 agentId -> outcome（result 序列化文本）映射。</summary>
        private static Dictionary<string, string> ReadJournalOutcomes(string wfDir)
        {
            var map = new Dictionary<string, string>();
            var journal = Path.Combine(wfDir, "journal.jsonl");
            if (!File.Exists(journal))
            {
                return map;
            }
            foreach (var line in File.ReadLines(journal))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }
                JObject obj;
                try { obj = JObject.Parse(line); }
                catch { continue; }

                if ((string?)obj["type"] != "result")
                {
                    continue;
                }
                var agentId = (string?)obj["agentId"];
                if (string.IsNullOrEmpty(agentId))
                {
                    continue;
                }
                var resultToken = obj["result"];
                if (resultToken != null && resultToken.Type != JTokenType.Null)
                {
                    map[agentId] = resultToken.Type == JTokenType.String
                        ? resultToken.ToString()
                        : resultToken.ToString(Newtonsoft.Json.Formatting.Indented);
                }
            }
            return map;
        }

        private static WorkflowTranscriptAgent? ParseAgentFile(string agentFile)
        {
            var agent = new WorkflowTranscriptAgent
            {
                AgentId = ExtractAgentIdFromFileName(agentFile)
            };
            int inputTokens = 0;
            int outputTokens = 0;
            bool sawTokens = false;
            string? lastStructuredOutput = null;
            JObject? lastStructuredInput = null;
            string? lastAssistantText = null;

            foreach (var line in File.ReadLines(agentFile))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }
                JObject obj;
                try { obj = JObject.Parse(line); }
                catch { continue; }

                var idFromLine = (string?)obj["agentId"];
                if (!string.IsNullOrEmpty(idFromLine))
                {
                    agent.AgentId = idFromLine!;
                }

                var type = (string?)obj["type"];
                var message = obj["message"] as JObject;
                if (message == null)
                {
                    continue;
                }

                if (type == "user")
                {
                    if (string.IsNullOrEmpty(agent.Prompt))
                    {
                        agent.Prompt = ExtractText(message["content"]);
                    }
                }
                else if (type == "assistant")
                {
                    var model = (string?)message["model"];
                    if (!string.IsNullOrEmpty(model))
                    {
                        agent.Model = model;
                    }

                    var usage = message["usage"] as JObject;
                    if (usage != null)
                    {
                        inputTokens += (int?)usage["input_tokens"] ?? 0;
                        outputTokens += (int?)usage["output_tokens"] ?? 0;
                        sawTokens = true;
                    }

                    if (message["content"] is JArray blocks)
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
                                    lastStructuredInput = block["input"] as JObject;
                                    lastStructuredOutput = block["input"]?.ToString(Newtonsoft.Json.Formatting.Indented);
                                }
                            }
                            else if (btype == "text")
                            {
                                var text = (string?)block["text"];
                                if (!string.IsNullOrWhiteSpace(text))
                                {
                                    lastAssistantText = text;
                                }
                            }
                        }
                    }
                }
            }

            agent.ToolUses = agent.Tools.Count;
            if (sawTokens)
            {
                agent.InputTokens = inputTokens;
                agent.OutputTokens = outputTokens;
                agent.TotalTokens = inputTokens + outputTokens;
            }
            agent.Status = "completed";
            agent.Outcome = lastStructuredOutput ?? lastAssistantText;
            // 标签优先级：outcome 的标识字段(slug/id/name…) > prompt 区分性 token(引号内) > 短 agentId。
            // 不能用 prompt 首行——workflow 各 agent 常共享同一角色前导句，首行全部相同、无法区分。
            agent.Label = LabelFromStructured(lastStructuredInput)
                ?? LabelFromPrompt(agent.Prompt)
                ?? agent.AgentId;

            return agent;
        }

        private static string ExtractAgentIdFromFileName(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path); // agent-xxxx
            const string prefix = "agent-";
            return name.StartsWith(prefix, StringComparison.Ordinal) ? name.Substring(prefix.Length) : name;
        }

        /// <summary>content 可能是 string 或 block 数组；提取纯文本。</summary>
        private static string? ExtractText(JToken? content)
        {
            if (content == null)
            {
                return null;
            }
            if (content.Type == JTokenType.String)
            {
                return content.ToString();
            }
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
            if (input == null || input.Type == JTokenType.Null)
            {
                return null;
            }
            var s = input.ToString(Newtonsoft.Json.Formatting.None);
            return s.Length > 160 ? s.Substring(0, 160) + "…" : s;
        }

        // outcome StructuredOutput 里的标识字段（slug/id/name…）——区分各 agent 的最佳来源。
        private static readonly string[] LabelKeys =
            { "slug", "id", "name", "title", "key", "zoneId", "variant", "label", "target" };

        private static string? LabelFromStructured(JObject? obj)
        {
            if (obj == null)
            {
                return null;
            }
            foreach (var key in LabelKeys)
            {
                var prop = obj.Properties()
                    .FirstOrDefault(p => string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase));
                if (prop != null && prop.Value.Type == JTokenType.String)
                {
                    var v = prop.Value.ToString().Trim();
                    if (!string.IsNullOrEmpty(v))
                    {
                        return v.Length > 48 ? v.Substring(0, 48) : v;
                    }
                }
            }
            return null;
        }

        private static string? LabelFromPrompt(string? prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return null;
            }
            // 取首个引号内 token（"" / '' / 「」/ “”）作区分标签——通常是变体名/目标名。
            var m = System.Text.RegularExpressions.Regex.Match(prompt, "[\"'「“]([^\"'」”\n]{1,40})[\"'」”]");
            if (m.Success && !string.IsNullOrWhiteSpace(m.Groups[1].Value))
            {
                return m.Groups[1].Value.Trim();
            }
            var firstLine = prompt.Split('\n').FirstOrDefault()?.Trim();
            if (string.IsNullOrEmpty(firstLine))
            {
                return null;
            }
            return firstLine.Length > 48 ? firstLine.Substring(0, 48) + "…" : firstLine;
        }
    }

    public class WorkflowTranscriptResult
    {
        public string SdkSessionId { get; set; } = "";
        public List<WorkflowTranscriptAgent> Agents { get; set; } = new();
    }

    public class WorkflowTranscriptAgent
    {
        public string AgentId { get; set; } = "";
        public string? Label { get; set; }
        public string? Model { get; set; }
        public string? Status { get; set; }
        public int? TotalTokens { get; set; }
        public int? InputTokens { get; set; }
        public int? OutputTokens { get; set; }
        public int? ToolUses { get; set; }
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

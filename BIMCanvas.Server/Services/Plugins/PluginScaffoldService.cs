using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BIMCanvas.Server.Services.Plugins;

/// <summary>
/// 生成 plugin 骨架 (模板 §4.6)。
/// M0 阶段仅 <c>blank</c> baseTemplate 可工作;<c>from-core-base</c> / <c>from-indoor-layout</c>
/// 留 stub 抛 <see cref="NotImplementedException"/>,等 Templates 物理重组 (§4.10) 完成后接入。
/// <para>
/// Phase 1 Web 不接入 [新建本地] 按钮 (主真理源 §2.3 / §5.3),但 REST 端点已暴露便于
/// 命令行 / 测试调用。
/// </para>
/// </summary>
public sealed class PluginScaffoldService
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private readonly ILogger<PluginScaffoldService> _logger;

    public PluginScaffoldService(ILogger<PluginScaffoldService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 生成 plugin 骨架到 <c>BIMCANVAS_HOME/plugin-scaffolds/{pluginId}/</c>。
    /// 返回生成目录的绝对路径。
    /// </summary>
    public async Task<string> ScaffoldAsync(
        string pluginId,
        string displayName,
        string baseTemplate,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pluginId))
            throw new ArgumentException("pluginId 必须非空", nameof(pluginId));

        var target = Path.Combine(PluginPaths.ScaffoldsRoot, pluginId);
        if (Directory.Exists(target))
            throw new InvalidOperationException($"scaffold 目标已存在: {target}");

        Directory.CreateDirectory(target);

        switch ((baseTemplate ?? "blank").ToLowerInvariant())
        {
            case "blank":
                await GenerateBlankAsync(target, pluginId, displayName, ct);
                break;
            case "from-core-base":
            case "from-indoor-layout":
                throw new NotImplementedException(
                    $"baseTemplate '{baseTemplate}' 在 M0 阶段未实现,等 Templates 物理重组完成后接入");
            default:
                throw new ArgumentException($"未知 baseTemplate: {baseTemplate}", nameof(baseTemplate));
        }

        _logger.LogInformation("plugin scaffold 已生成: {Path}", target);
        return target;
    }

    private static async Task GenerateBlankAsync(string target, string pluginId, string displayName, CancellationToken ct)
    {
        var display = string.IsNullOrWhiteSpace(displayName) ? pluginId : displayName;

        // bimcanvas-plugin.json (最小合法 manifest,passes StaticPluginValidator)
        var manifest = $@"{{
  ""name"": ""{pluginId}"",
  ""displayName"": ""{display}"",
  ""version"": ""0.1.0"",
  ""type"": ""bimcanvas-plugin"",
  ""schemaVersion"": 1,
  ""compatibility"": {{
    ""bimcanvas"": ""^1.0.0""
  }},
  ""systemPrompt"": ""BIMCANVAS.md"",
  ""agents"": ""agents/"",
  ""skills"": ""skills/"",
  ""mcpTools"": ""mcp_tools/example.py"",
  ""mcpNamespace"": ""{pluginId}"",
  ""maturity"": ""experimental""
}}";
        await File.WriteAllTextAsync(Path.Combine(target, "bimcanvas-plugin.json"), manifest, Utf8NoBom, ct);

        // .claude-plugin/plugin.json (派生,SDK plugin 触发器)
        var sdkPluginDir = Path.Combine(target, ".claude-plugin");
        Directory.CreateDirectory(sdkPluginDir);
        var sdkPlugin = $@"{{
  ""name"": ""{pluginId}"",
  ""version"": ""0.1.0"",
  ""description"": ""{display}""
}}";
        await File.WriteAllTextAsync(Path.Combine(sdkPluginDir, "plugin.json"), sdkPlugin, Utf8NoBom, ct);

        // BIMCANVAS.md
        var prompt = $@"# {display} — Plugin System Prompt

这是 plugin `{pluginId}` 的系统提示词。
平台启动时,本文件会拼接到 core-base BIMCANVAS.md 之后,作 ""## Active Domain Contract"" 段落注入 Agent。

## TODO
- 替换本段为 plugin 域定位 (1-3 句话)
- 列出本 plugin 提供的能力 / 限制
";
        await File.WriteAllTextAsync(Path.Combine(target, "BIMCANVAS.md"), prompt, Utf8NoBom, ct);

        // mcp_tools/example.py (示范 register pattern,Probe 可成功通过)
        var toolsDir = Path.Combine(target, "mcp_tools");
        Directory.CreateDirectory(toolsDir);
        var examplePy = @"""""""Example MCP tool for BIMCanvas plugin scaffold.

ExecutablePluginProbe will import this file + call register(builder) at trust-time.
""""""


def register(builder):
    @builder.tool(name='echo', description='Echo input back to caller')
    async def echo(args, ctx):
        return {'echo': args.get('message', '')}
";
        await File.WriteAllTextAsync(Path.Combine(toolsDir, "example.py"), examplePy, Utf8NoBom, ct);

        // 空 agents/ skills/ projectMount/
        Directory.CreateDirectory(Path.Combine(target, "agents"));
        Directory.CreateDirectory(Path.Combine(target, "skills"));
        var projectMountDir = Path.Combine(target, "projectMount");
        Directory.CreateDirectory(projectMountDir);
        await File.WriteAllTextAsync(
            Path.Combine(projectMountDir, "manifest.json"),
            "{\n  \"files\": []\n}",
            Utf8NoBom, ct);

        // .gitignore (R5 / §6.2:预禁 plugin 目录污染)
        var gitignore = @"# BIMCanvas plugin 目录纯净纪律 (主真理源 v1.1 §6.2)
CLAUDE.md
settings.local.json
.claude/
.bimcanvas/
";
        await File.WriteAllTextAsync(Path.Combine(target, ".gitignore"), gitignore, Utf8NoBom, ct);

        // README.md (三步流程指引)
        var readme = $@"# {display}

BIMCanvas plugin scaffold (blank template)。

## 三步流程

1. 编辑 `BIMCANVAS.md` 与 `mcp_tools/example.py`,实现 plugin 能力
2. 运行 `bimcanvas-plugin-validate ./` 校验 (本地 dev 用 CLI,会跑 Static + Executable 两道防线)
3. `git init && git add . && git commit -m 'initial' && git push` 发布到 GitHub

## 在 BIMCanvas 中安装

- 打开设置页 → 插件管理 → [+ 安装新插件]
- 粘贴本仓库 GitHub URL → 确认
- 安装完成后点 [信任并激活] → 重启程序

## 目录约束 (主真理源 v1.1 §6.2)

绝不能放 `CLAUDE.md` / `settings.local.json` / `.claude/` / `.bimcanvas/` —— StaticPluginValidator 会拒绝。
";
        await File.WriteAllTextAsync(Path.Combine(target, "README.md"), readme, Utf8NoBom, ct);
    }
}

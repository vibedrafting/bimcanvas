using System;
using System.Text.Json.Serialization;

namespace BIMCanvas.Server.Models.Plugins;

/// <summary>
/// project.json.scenes[] 单项 (主真理源 v1.1 §3.9)。
/// 由组2 端点 <c>POST /api/project/{id}/scenes</c> 通过 JObject patch 写入;
/// PluginLaunchContext 通过 <see cref="ProjectScenesSummary"/> 把当前所有 scenes
/// 一次性携带给 Agent 子进程。
///
/// 字段权威性以本组 Step 3b 起草的 <c>docs/bcp-scenes-schema.json</c> JSONSchema 为准。
/// 任何字段扩展必须同步更新该 schema 并升 schemaVersion。
/// </summary>
public sealed record ProjectScene(
    [property: JsonPropertyName("sceneId")] string SceneId,
    [property: JsonPropertyName("scene")] string Scene,
    [property: JsonPropertyName("plugin")] ScenePluginRef Plugin,
    [property: JsonPropertyName("status")] SceneStatus Status,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt
);

/// <summary>
/// scenes[].plugin 子对象 (主真理源 v1.1 §3.9 / §2.2 场景 2 步骤 6 POST body)。
/// <para>
/// <c>Id</c> 是 plugin 的 manifest <c>name</c> 字段值;
/// <c>VersionRange</c> 是 semver range,绑定时由 Web / API 写入,后续 PluginLifecycleService
/// 启动 Agent 前会基于 plugins-state.json 中已安装版本与该 range 做兼容性校验 (M1+)。
/// </para>
/// </summary>
public sealed record ScenePluginRef(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("versionRange")] string VersionRange
);

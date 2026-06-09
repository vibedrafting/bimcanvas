using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace BIMCanvas.Server.Models.Plugins;

/// <summary>
/// OpenProject 返回状态 (项目去插件态后只余 Bound 单态)。
/// Server 把 ProjectContext 内部状态投影为该 enum 返回给 Web。
/// 序列化为 camelCase 字符串(由 enum 类型上的 <c>[JsonConverter]</c> 控制)。
///
/// <para>原 SceneSelectRequired / RequiresSceneBinding 两态随「项目不持插件运行态」退役:
/// 打开项目不再读 project.json.scenes[] 做匹配,activePlugin 非空即直接 Bound、
/// 无 active plugin 走 legacy 放行,均不产生待选/待绑定中间态。</para>
/// </summary>
[JsonConverter(typeof(StringEnumConverter), typeof(CamelCaseNamingStrategy))]
public enum OpenStatus
{
    /// <summary>
    /// 项目打开成功 → ProjectContext.State = Bound,生成 LaunchContext。
    /// Web 直接渲染 active plugin 视图。
    /// </summary>
    Bound,
}

/// <summary>
/// ProjectContext 状态机 (主真理源 v1.1 §3.2 四态生命周期 + §4.7)。
/// </summary>
public enum ProjectState
{
    /// <summary>无项目打开。</summary>
    None,

    /// <summary>项目已解析,但 scene 未绑定 / 多个候选未选 → 所有写入 API 403 (V12a)。</summary>
    Pending,

    /// <summary>scene 已绑定,LaunchContext 已生成,可写入,但 Agent 子进程尚未 launch。</summary>
    Bound,

    /// <summary>Agent 子进程已 launch + 健康,写入与 Agent 通信均就绪 (M1 范围)。</summary>
    Launched,
}

/// <summary>
/// ProjectContext.CheckWriteAllowed 返回结果。
/// 不允许时 <see cref="Code"/> 用于映射 HTTP 403 + JSON code 字段 (V12a / R3)。
/// </summary>
public sealed record WriteGateResult(bool Allowed, string? Code, string? Message)
{
    public static readonly WriteGateResult Ok = new(true, null, null);
}

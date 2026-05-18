namespace BIMCanvas.Server.Models.Plugins;

/// <summary>
/// OpenProject 三态返回 (主真理源 v1.1 §2.2 + §4.7 / 模板 §4.7)。
/// Server 把 ProjectContext 内部状态投影为该 enum 返回给 Web,Web 据此决定弹哪个对话框。
/// 序列化为 camelCase 字符串(由 Program.cs 全局 <c>StringEnumConverter</c> 接管)。
/// </summary>
public enum OpenStatus
{
    /// <summary>
    /// 命中唯一 scene → ProjectContext.State = Bound,生成 LaunchContext,启动 Agent。
    /// Web 直接渲染 active plugin 视图。
    /// </summary>
    Bound,

    /// <summary>
    /// 命中多个 scene → ProjectContext.State = Pending,候选 scenes 返回 Web。
    /// Web 弹 SceneSelectorDialog 让用户选。
    /// </summary>
    SceneSelectRequired,

    /// <summary>
    /// 未命中任何 scene (legacy 项目 / 新 scene 类型) → ProjectContext.State = Pending,
    /// Web 弹 SceneBindingDialog 让用户 [新增此场景] 或 [切回 active plugin]。
    /// </summary>
    RequiresSceneBinding,
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

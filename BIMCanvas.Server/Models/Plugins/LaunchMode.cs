namespace BIMCanvas.Server.Models.Plugins;

/// <summary>
/// Plugin Agent 子进程启动模式 (主真理源 v1.1 §3.3 / V14 T10-T12)。
/// 决定 Server 写入 gate 范围与 Agent 能力边界。
/// 序列化为 camelCase 字符串(由 Program.cs 全局 <c>StringEnumConverter</c> 接管)。
/// </summary>
public enum LaunchMode
{
    /// <summary>
    /// 平台已加载、无项目打开 (settings 页 / 启动时)。
    /// Agent 仅支持 chat / 读取 plugin 元数据;所有写入 API 一律 403 (V12a)。
    /// </summary>
    Projectless,

    /// <summary>
    /// 项目已打开 + scene 已绑定 + writable。
    /// Agent 拥有完整能力;Server 按 ActiveSceneId 强制写入隔离 (V12b)。
    /// </summary>
    ProjectBound,
}

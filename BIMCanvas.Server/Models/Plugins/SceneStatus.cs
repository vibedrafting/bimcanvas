using System.Text.Json.Serialization;

namespace BIMCanvas.Server.Models.Plugins;

/// <summary>
/// project.json.scenes[].status (主真理源 v1.1 §3.9)。
/// Phase 1 仅 <see cref="Active"/> 合法;未来可能引入 Archived / Frozen 用于 scene 生命周期。
/// JSONSchema (组1 Step 3) 当前 enum 也只声明 "active",新增值需走 schemaVersion 升级流程。
/// </summary>
[JsonConverter(typeof(CamelCaseEnumConverter))]
public enum SceneStatus
{
    /// <summary>scene 处于活动状态,Agent / Web 可写可读。</summary>
    Active,
}

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace BIMCanvas.Server.Models.Plugins;

/// <summary>
/// project.json.scenes[].status (主真理源 v1.1 §3.9)。
/// Phase 1 仅 <see cref="Active"/> 合法;未来可能引入 Archived / Frozen 用于 scene 生命周期。
/// JSONSchema (组1 Step 3) 当前 enum 也只声明 "active",新增值需走 schemaVersion 升级流程。
/// 序列化为 camelCase 字符串(由 enum 类型上的 <c>[JsonConverter]</c> 控制)。
/// </summary>
[JsonConverter(typeof(StringEnumConverter), typeof(CamelCaseNamingStrategy))]
public enum SceneStatus
{
    /// <summary>scene 处于活动状态,Agent / Web 可写可读。</summary>
    Active,
}

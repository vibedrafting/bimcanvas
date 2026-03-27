namespace BIMCanvas.Core.Validation
{
    /// <summary>
    /// 方案级诊断项（类比编译器 Diagnostic）
    /// </summary>
    public class Diagnostic
    {
        /// <summary>
        /// 错误代码（如 "E001_OUT_OF_BOUNDS"）
        /// </summary>
        public string Code { get; }

        /// <summary>
        /// 严重级别："error"（MVP 只有 error，未来扩展 "warning"）
        /// </summary>
        public string Severity { get; }

        /// <summary>
        /// 人类可读的错误消息
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// 产生错误的模块 ID
        /// </summary>
        public string ModuleId { get; }

        /// <summary>
        /// 模块可读名称（可为 null）
        /// </summary>
        public string? ModuleName { get; }

        /// <summary>
        /// 冲突对象 ID（墙体/柱子/禁区/另一模块的 ID，可为 null）
        /// </summary>
        public string? ConflictId { get; }

        /// <summary>
        /// 冲突对象类型：wall / column / exclusion / module（可为 null）
        /// </summary>
        public string? ConflictType { get; }

        /// <summary>重叠面积 mm²（E002-E005 有效，E001/E006 为 null）</summary>
        public double? OverlapAreaMm2 { get; }

        /// <summary>穿透深度 mm，模块应朝反方向移动此距离</summary>
        public double? PenetrationDepthMm { get; }

        /// <summary>穿透方向 north/south/east/west（障碍物相对于模块的方位）</summary>
        public string? PenetrationDirection { get; }

        public Diagnostic(
            string code,
            string severity,
            string message,
            string moduleId,
            string? moduleName = null,
            string? conflictId = null,
            string? conflictType = null,
            double? overlapAreaMm2 = null,
            double? penetrationDepthMm = null,
            string? penetrationDirection = null)
        {
            Code = code;
            Severity = severity;
            Message = message;
            ModuleId = moduleId;
            ModuleName = moduleName;
            ConflictId = conflictId;
            ConflictType = conflictType;
            OverlapAreaMm2 = overlapAreaMm2;
            PenetrationDepthMm = penetrationDepthMm;
            PenetrationDirection = penetrationDirection;
        }

        public override string ToString()
        {
            var conflict = ConflictId != null ? $" <-> {ConflictType}:{ConflictId}" : "";
            return $"[{Code}] {ModuleId}{conflict}: {Message}";
        }
    }

    /// <summary>
    /// 诊断错误代码常量
    /// </summary>
    public static class DiagnosticCodes
    {
        /// <summary>模块不在任何设计区/房间区域内</summary>
        public const string OutOfBounds = "E001_OUT_OF_BOUNDS";

        /// <summary>模块与墙体重叠</summary>
        public const string WallOverlap = "E002_WALL_OVERLAP";

        /// <summary>模块与柱子重叠</summary>
        public const string ColumnOverlap = "E003_COLUMN_OVERLAP";

        /// <summary>模块与禁区重叠（门扇扫过区域等）</summary>
        public const string ExclusionOverlap = "E004_EXCLUSION_OVERLAP";

        /// <summary>模块之间重叠</summary>
        public const string ModuleOverlap = "E005_MODULE_OVERLAP";

        /// <summary>模块缺少 Bounds 定义</summary>
        public const string MissingBounds = "E006_MISSING_BOUNDS";

        /// <summary>模块 facing.semantic 非法</summary>
        public const string InvalidFacingSemantic = "E007_INVALID_FACING_SEMANTIC";

        /// <summary>模块缺少有效 facing.value</summary>
        public const string MissingFacingValue = "E008_MISSING_FACING_VALUE";

        /// <summary>模块 facing.value 为零向量或非法向量</summary>
        public const string InvalidFacingValue = "E009_INVALID_FACING_VALUE";
    }
}

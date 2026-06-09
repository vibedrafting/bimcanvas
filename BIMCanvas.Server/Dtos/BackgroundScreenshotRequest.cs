using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BIMCanvas.Server.Dtos
{
    public class BackgroundScreenshotRequest
    {
        [Required]
        public string ProjectPath { get; set; } = string.Empty;

        public string StrategyId { get; set; } = "default";

        // 可选。指针模型下截指定候选/变体方案 slug（如 "_cand-a"）。
        // 非空时须配 viewport.zoneId（mode="zone"）作 zone 作用域，Server 据此解析
        // schemes/{dz}/{variantId}/[{leaf}/]modules.json；留空 → 截 adopted 当前生效方案。
        // 语义独立于 StrategyId（后者是策略/方案集 id，非候选 slug）。
        public string? VariantId { get; set; }

        // Legacy: viewMode + layers (number). Prefer LayerPreset + LayerEnable/Disable.
        public string? ViewMode { get; set; }

        public int[]? Layers { get; set; }

        // New layer control
        public string? LayerPreset { get; set; }

        public string[]? LayerEnable { get; set; }

        public string[]? LayerDisable { get; set; }

        public ViewportConfig? Viewport { get; set; }

        [Range(1, 4)]
        public int Scale { get; set; } = 2;

        // When true, auto-calculate viewport ratio based on target bounds.
        public bool? AutoFitViewport { get; set; }

        public string? Theme { get; set; }
    }

    public class ViewportConfig
    {
        /// <summary>
        /// 新格式（推荐）：传入任意有效 ID（如 rz_1/r_1/dz_1），
        /// 前端依次在 baseline.rooms → computed.roomZones → activeScheme.zones 中查找。
        /// 留空且 Mode=full 时全屏截图。
        /// </summary>
        public string? Id { get; set; }

        // 旧格式（向后兼容）
        public string Mode { get; set; } = "full";

        public string? RoomId { get; set; }

        public string? ZoneId { get; set; }

        public Bounds2D? Bounds { get; set; }
    }

    public class Bounds2D
    {
        public double MinX { get; set; }

        public double MinY { get; set; }

        public double MaxX { get; set; }

        public double MaxY { get; set; }
    }

    public class BackgroundScreenshotResponse
    {
        public string ImageData { get; set; } = string.Empty;
    }

    public class BackgroundScreenshotBatchRequest
    {
        [Required]
        public string ProjectPath { get; set; } = string.Empty;

        public string StrategyId { get; set; } = "default";

        // 可选。批量共享一个候选/变体方案 slug（请求级）；非空时各 shot 的 viewport.zoneId 去重作 zone 作用域。
        // 留空 → 截 adopted 当前生效方案。语义同 BackgroundScreenshotRequest.VariantId。
        public string? VariantId { get; set; }

        [Range(1, 4)]
        public int Scale { get; set; } = 2;

        public bool? AutoFitViewport { get; set; }

        public string? Theme { get; set; }

        public List<BackgroundScreenshotBatchItem> Items { get; set; } = new List<BackgroundScreenshotBatchItem>();
    }

    public class BackgroundScreenshotBatchItem
    {
        public string? Name { get; set; }

        public string? ViewMode { get; set; }

        public int[]? Layers { get; set; }

        public string? LayerPreset { get; set; }

        public string[]? LayerEnable { get; set; }

        public string[]? LayerDisable { get; set; }

        public ViewportConfig? Viewport { get; set; }

        public bool? AutoFitViewport { get; set; }
    }

    public class BackgroundScreenshotBatchItemResult
    {
        public string? Name { get; set; }

        public string? ImageData { get; set; }

        public string? Error { get; set; }

        public int? ElapsedMs { get; set; }
    }

    public class BackgroundScreenshotBatchResponse
    {
        public List<BackgroundScreenshotBatchItemResult> Items { get; set; } = new List<BackgroundScreenshotBatchItemResult>();
    }
}

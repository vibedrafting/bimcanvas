using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BIMCanvas.Server.Dtos
{
    public class BackgroundScreenshotRequest
    {
        [Required]
        public string ProjectPath { get; set; } = string.Empty;

        public string StrategyId { get; set; } = "default";

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

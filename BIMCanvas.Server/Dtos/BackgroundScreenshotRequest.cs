using System.ComponentModel.DataAnnotations;

namespace BIMCanvas.Server.Dtos
{
    public class BackgroundScreenshotRequest
    {
        [Required]
        public string ProjectPath { get; set; } = string.Empty;

        public string StrategyId { get; set; } = "default";

        public string ViewMode { get; set; } = "human";

        public int[]? Layers { get; set; }

        public ViewportConfig? Viewport { get; set; }

        [Range(1, 4)]
        public int Scale { get; set; } = 2;

        public string? Theme { get; set; }
    }

    public class ViewportConfig
    {
        public string Mode { get; set; } = "full";

        public string? RoomId { get; set; }

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
}

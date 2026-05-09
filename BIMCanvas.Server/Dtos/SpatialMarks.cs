using System.Collections.Generic;
using BIMCanvas.Core.Models.Geometry;

namespace BIMCanvas.Server.Dtos
{
    public class MergeGridSelectionRequest
    {
        public string ZoneId { get; set; } = string.Empty;
        public double CellSize { get; set; }
        public double? GridOriginX { get; set; }
        public double? GridOriginY { get; set; }
        public List<GridSelectionCell> Cells { get; set; } = new List<GridSelectionCell>();
    }

    public class GridSelectionCell
    {
        public int Col { get; set; }
        public int Row { get; set; }
    }

    public class MergeGridSelectionResponse
    {
        public string ZoneId { get; set; } = string.Empty;
        public List<SpatialGeometry> Geometry { get; set; } = new List<SpatialGeometry>();
    }
}

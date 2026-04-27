using Newtonsoft.Json;

namespace BIMCanvas.Core.Models.Geometry
{
    /// <summary>
    /// AI 空间标记的合并后几何片段。
    /// 每个片段只输出 aabb 或 polygon 之一。
    /// </summary>
    public class SpatialGeometry
    {
        [JsonProperty("aabb", NullValueHandling = NullValueHandling.Ignore)]
        public AABB? Aabb { get; set; }

        [JsonProperty("polygon", NullValueHandling = NullValueHandling.Ignore)]
        public Polygon2D? Polygon { get; set; }
    }
}

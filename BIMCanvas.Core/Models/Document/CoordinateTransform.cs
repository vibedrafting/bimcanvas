namespace BIMCanvas.Core.Models.Document
{
    /// <summary>
    /// 坐标转换配置（用于回写）
    /// </summary>
    public class CoordinateTransform
    {
        /// <summary>
        /// 坐标原点在 Revit 项目坐标系中的位置（毫米）
        /// </summary>
        public double[] Origin { get; set; } = new double[3]; // [x, y, z] in mm

        /// <summary>
        /// 视图旋转角度（弧度）
        /// </summary>
        public double Rotation { get; set; }

        /// <summary>
        /// 原点计算方法："boundingBox" 或 "cropBox"
        /// </summary>
        public string Method { get; set; } = "boundingBox";
    }
}

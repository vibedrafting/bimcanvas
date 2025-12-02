using System;
using System.Collections.Generic;

namespace BIMCanvas.Core.Algorithms
{
    /// <summary>
    /// 朝向转换辅助类
    /// </summary>
    public class FacingHelper
    {
        private static readonly Dictionary<string, double> FacingToAngle = new Dictionary<string, double>
        {
            { "north", 0 },
            { "northeast", 45 },
            { "east", 90 },
            { "southeast", 135 },
            { "south", 180 },
            { "southwest", 225 },
            { "west", 270 },
            { "northwest", 315 }
        };

        private static readonly Dictionary<double, string> AngleToFacing = new Dictionary<double, string>
        {
            { 0, "north" },
            { 45, "northeast" },
            { 90, "east" },
            { 135, "southeast" },
            { 180, "south" },
            { 225, "southwest" },
            { 270, "west" },
            { 315, "northwest" }
        };

        /// <summary>
        /// 语义方向 -> 旋转角度
        /// </summary>
        /// <param name="facing">语义方向 (north, south, east, west, ...)</param>
        /// <returns>旋转角度 (度)</returns>
        public double ToRotation(string facing)
        {
            if (string.IsNullOrEmpty(facing))
                return 0;

            var key = facing.ToLowerInvariant();
            return FacingToAngle.TryGetValue(key, out var angle) ? angle : 0;
        }

        /// <summary>
        /// 旋转角度 -> 语义方向
        /// </summary>
        /// <param name="angle">旋转角度 (度)</param>
        /// <returns>最接近的语义方向</returns>
        public string FromRotation(double angle)
        {
            // 标准化角度到 0-360
            angle = ((angle % 360) + 360) % 360;

            // 找到最接近的标准角度
            double closestAngle = 0;
            double minDiff = double.MaxValue;

            foreach (var entry in AngleToFacing)
            {
                double diff = Math.Abs(entry.Key - angle);
                // 考虑环绕
                diff = Math.Min(diff, 360 - diff);

                if (diff < minDiff)
                {
                    minDiff = diff;
                    closestAngle = entry.Key;
                }
            }

            return AngleToFacing[closestAngle];
        }

        /// <summary>
        /// 获取朝向的方向向量
        /// </summary>
        /// <param name="facing">语义方向</param>
        /// <returns>方向向量 [dx, dy]</returns>
        public double[] ToVector(string facing)
        {
            var angle = ToRotation(facing);
            var radians = angle * Math.PI / 180;

            // Y-up 坐标系中：north = (0, 1)
            return new double[]
            {
                Math.Sin(radians),
                Math.Cos(radians)
            };
        }
    }
}

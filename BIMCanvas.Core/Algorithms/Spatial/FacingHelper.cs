using System;
using BIMCanvas.Core.Models.Primitives;

namespace BIMCanvas.Core.Algorithms.Spatial
{
    /// <summary>
    /// 朝向辅助类
    /// </summary>
    public static class FacingHelper
    {
        /// <summary>
        /// 语义方向 → 单位向量
        /// </summary>
        public static Vec2D SemanticToVector(string semantic)
        {
            return semantic.ToLowerInvariant() switch
            {
                "north" => new Vec2D(0, 1),
                "south" => new Vec2D(0, -1),
                "east" => new Vec2D(1, 0),
                "west" => new Vec2D(-1, 0),
                "northeast" => new Vec2D(1, 1).Normalize(),
                "northwest" => new Vec2D(-1, 1).Normalize(),
                "southeast" => new Vec2D(1, -1).Normalize(),
                "southwest" => new Vec2D(-1, -1).Normalize(),
                _ => throw new ArgumentException($"Unknown semantic facing: {semantic}")
            };
        }

        /// <summary>
        /// 角度（度）→ 单位向量
        /// X轴正方向=0°，逆时针增加
        /// </summary>
        public static Vec2D AngleToVector(double degrees)
        {
            var radians = degrees * Math.PI / 180.0;
            return new Vec2D(Math.Cos(radians), Math.Sin(radians));
        }

        /// <summary>
        /// 向量 → 角度（度）
        /// X轴正方向=0°，逆时针增加
        /// </summary>
        public static double VectorToAngle(Vec2D vector)
        {
            var radians = Math.Atan2(vector.Y, vector.X);
            var degrees = radians * 180.0 / Math.PI;
            // 归一化到 [0, 360)
            if (degrees < 0)
                degrees += 360;
            return degrees;
        }

        /// <summary>
        /// 向量 → 最接近的语义方向（如果在容差范围内）
        /// </summary>
        /// <param name="vector">输入向量</param>
        /// <param name="toleranceDegrees">角度容差（默认 5°）</param>
        /// <returns>语义字符串，如果不匹配则返回 null</returns>
        public static string? VectorToSemantic(Vec2D vector, double toleranceDegrees = 5.0)
        {
            var angle = VectorToAngle(vector);

            // 语义方向及其角度
            var semantics = new (string name, double angle)[]
            {
                ("east", 0),
                ("northeast", 45),
                ("north", 90),
                ("northwest", 135),
                ("west", 180),
                ("southwest", 225),
                ("south", 270),
                ("southeast", 315)
            };

            foreach (var (name, targetAngle) in semantics)
            {
                var diff = Math.Abs(angle - targetAngle);
                if (diff > 180)
                    diff = 360 - diff;

                if (diff <= toleranceDegrees)
                    return name;
            }

            return null;
        }
    }
}

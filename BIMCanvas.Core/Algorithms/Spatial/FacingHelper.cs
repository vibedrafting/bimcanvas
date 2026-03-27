using System;
using BIMCanvas.Core.Models.Geometry;

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
            if (!TrySemanticToVector(semantic, out var vector))
                throw new ArgumentException($"Unknown semantic facing: {semantic}");

            return vector;
        }

        /// <summary>
        /// 尝试将语义方向解析为单位向量
        /// </summary>
        public static bool TrySemanticToVector(string? semantic, out Vec2D vector)
        {
            switch (semantic?.ToLowerInvariant())
            {
                case "north":
                    vector = new Vec2D(0, 1);
                    return true;
                case "south":
                    vector = new Vec2D(0, -1);
                    return true;
                case "east":
                    vector = new Vec2D(1, 0);
                    return true;
                case "west":
                    vector = new Vec2D(-1, 0);
                    return true;
                case "northeast":
                    vector = new Vec2D(1, 1).Normalize();
                    return true;
                case "northwest":
                    vector = new Vec2D(-1, 1).Normalize();
                    return true;
                case "southeast":
                    vector = new Vec2D(1, -1).Normalize();
                    return true;
                case "southwest":
                    vector = new Vec2D(-1, -1).Normalize();
                    return true;
                default:
                    vector = default;
                    return false;
            }
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

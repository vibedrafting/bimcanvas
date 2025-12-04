using System;
using BIMCanvas.Core.Models.Primitives;

namespace BIMCanvas.Core.Models.Document
{
    /// <summary>
    /// 朝向 - 支持语义字符串("north")或 Vec2D 向量([0.866, 0.5])
    /// </summary>
    public readonly struct Facing
    {
        private readonly string? _semantic;
        private readonly Vec2D? _vector;

        /// <summary>
        /// 是否为语义类型
        /// </summary>
        public bool IsSemantic => _semantic != null;

        /// <summary>
        /// 语义值（如 "north", "east" 等）
        /// </summary>
        public string? Semantic => _semantic;

        /// <summary>
        /// 向量值（单位向量）
        /// </summary>
        public Vec2D? Vector => _vector;

        /// <summary>
        /// 语义构造
        /// </summary>
        public Facing(string semantic)
        {
            _semantic = ValidateSemantic(semantic);
            _vector = null;
        }

        /// <summary>
        /// 向量构造
        /// </summary>
        public Facing(Vec2D vector)
        {
            _semantic = null;
            // 归一化向量
            _vector = vector.Length > 0.5 ? vector.Normalize() : vector;
        }

        /// <summary>
        /// 隐式转换：string → Facing
        /// </summary>
        public static implicit operator Facing(string s) => new Facing(s);

        /// <summary>
        /// 隐式转换：Vec2D → Facing
        /// </summary>
        public static implicit operator Facing(Vec2D v) => new Facing(v);

        /// <summary>
        /// 转换为弧度（供几何计算）
        /// X轴正方向=0°，逆时针增加
        /// </summary>
        public double ToAngleRadians()
        {
            var v = GetVector();
            return Math.Atan2(v.Y, v.X);
        }

        /// <summary>
        /// 获取向量表示
        /// </summary>
        public Vec2D GetVector()
        {
            if (_vector.HasValue)
                return _vector.Value;

            return SemanticToVector(_semantic!);
        }

        /// <summary>
        /// 语义 → 向量映射
        /// </summary>
        private static Vec2D SemanticToVector(string semantic)
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
                _ => throw new ArgumentException($"Unknown facing: {semantic}")
            };
        }

        /// <summary>
        /// 验证语义字符串
        /// </summary>
        private static string ValidateSemantic(string semantic)
        {
            var lower = semantic.ToLowerInvariant();
            return lower switch
            {
                "north" => lower,
                "south" => lower,
                "east" => lower,
                "west" => lower,
                "northeast" => lower,
                "northwest" => lower,
                "southeast" => lower,
                "southwest" => lower,
                _ => throw new ArgumentException($"Invalid semantic facing: {semantic}")
            };
        }

        public override string ToString()
        {
            return IsSemantic ? $"\"{_semantic}\"" : _vector.ToString()!;
        }
    }
}

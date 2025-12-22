using System;
using Newtonsoft.Json;
using BIMCanvas.Core.Converters.Json;
using BIMCanvas.Core.Models.Geometry;

namespace BIMCanvas.Core.Models.Semantic
{
    /// <summary>
    /// 朝向 - 支持语义方向枚举或 Vec2D 向量
    /// </summary>
    [JsonConverter(typeof(FacingConverter))]
    public readonly struct Facing
    {
        private readonly FacingDirection? _semantic;
        private readonly Vec2D? _vector;

        /// <summary>
        /// 是否为语义类型
        /// </summary>
        public bool IsSemantic => _semantic.HasValue;

        /// <summary>
        /// 语义值（枚举类型）
        /// </summary>
        public FacingDirection? Semantic => _semantic;

        /// <summary>
        /// 向量值（单位向量）
        /// </summary>
        public Vec2D? Vector => _vector;

        /// <summary>
        /// 语义构造
        /// </summary>
        public Facing(FacingDirection semantic)
        {
            _semantic = semantic;
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
        /// 隐式转换：FacingDirection → Facing
        /// </summary>
        public static implicit operator Facing(FacingDirection d) => new Facing(d);

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

            return SemanticToVector(_semantic!.Value);
        }

        /// <summary>
        /// 语义 → 向量映射
        /// </summary>
        private static Vec2D SemanticToVector(FacingDirection semantic)
        {
            return semantic switch
            {
                FacingDirection.North => new Vec2D(0, 1),
                FacingDirection.South => new Vec2D(0, -1),
                FacingDirection.East => new Vec2D(1, 0),
                FacingDirection.West => new Vec2D(-1, 0),
                FacingDirection.Northeast => new Vec2D(1, 1).Normalize(),
                FacingDirection.Northwest => new Vec2D(-1, 1).Normalize(),
                FacingDirection.Southeast => new Vec2D(1, -1).Normalize(),
                FacingDirection.Southwest => new Vec2D(-1, -1).Normalize(),
                _ => throw new ArgumentException($"Unknown facing: {semantic}")
            };
        }

        /// <summary>
        /// 获取语义字符串表示（用于 JSON 序列化）
        /// </summary>
        public string? GetSemanticString()
        {
            if (!_semantic.HasValue)
                return null;

            return _semantic.Value switch
            {
                FacingDirection.North => "north",
                FacingDirection.South => "south",
                FacingDirection.East => "east",
                FacingDirection.West => "west",
                FacingDirection.Northeast => "northeast",
                FacingDirection.Northwest => "northwest",
                FacingDirection.Southeast => "southeast",
                FacingDirection.Southwest => "southwest",
                _ => null
            };
        }

        /// <summary>
        /// 从字符串解析为 FacingDirection
        /// </summary>
        public static FacingDirection ParseSemantic(string semantic)
        {
            return semantic.ToLowerInvariant() switch
            {
                "north" => FacingDirection.North,
                "south" => FacingDirection.South,
                "east" => FacingDirection.East,
                "west" => FacingDirection.West,
                "northeast" => FacingDirection.Northeast,
                "northwest" => FacingDirection.Northwest,
                "southeast" => FacingDirection.Southeast,
                "southwest" => FacingDirection.Southwest,
                _ => throw new ArgumentException($"Invalid semantic facing: {semantic}")
            };
        }

        public override string ToString()
        {
            return IsSemantic ? $"\"{GetSemanticString()}\"" : _vector.ToString()!;
        }
    }
}

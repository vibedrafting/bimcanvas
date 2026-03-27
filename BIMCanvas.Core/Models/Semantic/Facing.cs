using System;
using BIMCanvas.Core.Algorithms.Spatial;
using Newtonsoft.Json;
using BIMCanvas.Core.Converters.Json;
using BIMCanvas.Core.Models.Geometry;

namespace BIMCanvas.Core.Models.Semantic
{
    /// <summary>
    /// 朝向值对象。
    /// value 是唯一方向真理；semantic 是 AI 临时输入槽。
    /// </summary>
    [JsonConverter(typeof(FacingConverter))]
    public readonly struct Facing
    {
        private const double MinVectorLength = 1e-10;
        private readonly Vec2D? _value;
        private readonly string? _semantic;

        /// <summary>
        /// 方向真理值（单位向量）
        /// </summary>
        public Vec2D? Value => _value;

        /// <summary>
        /// AI 语义输入槽位
        /// </summary>
        public string? Semantic => _semantic;

        /// <summary>
        /// 是否携带语义输入
        /// </summary>
        public bool HasSemantic => !string.IsNullOrWhiteSpace(_semantic);

        /// <summary>
        /// 是否存在有效向量值
        /// </summary>
        public bool HasUsableValue => TryGetNormalizedValue(out _);

        /// <summary>
        /// 对象构造
        /// </summary>
        public Facing(Vec2D? value, string? semantic)
        {
            _value = value;
            _semantic = string.IsNullOrWhiteSpace(semantic)
                ? null
                : semantic.Trim().ToLowerInvariant();
        }

        /// <summary>
        /// 兼容向量构造
        /// </summary>
        public Facing(Vec2D vector)
            : this(vector, null)
        {
        }

        /// <summary>
        /// 兼容旧语义构造：立即解析为 value，semantic 置空
        /// </summary>
        public Facing(string semantic)
            : this(ParseLegacySemantic(semantic), null)
        {
        }

        /// <summary>
        /// 兼容旧枚举构造：立即解析为 value，semantic 置空
        /// </summary>
        public Facing(FacingDirection semantic)
            : this(ParseLegacySemantic(DirectionToSemantic(semantic)), null)
        {
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
        /// north=[0,1] 为 0°，逆时针增加
        /// </summary>
        public double ToAngleRadians()
        {
            var v = GetVector();
            return Math.Atan2(v.X, v.Y);
        }

        /// <summary>
        /// 获取归一化后的向量表示
        /// </summary>
        public Vec2D GetVector()
        {
            if (TryGetNormalizedValue(out var value))
                return value;

            throw new InvalidOperationException("Facing 不包含可用的 value");
        }

        /// <summary>
        /// 尝试获取归一化 value
        /// </summary>
        public bool TryGetNormalizedValue(out Vec2D value)
        {
            if (!_value.HasValue)
            {
                value = default;
                return false;
            }

            var candidate = _value.Value;
            if (!IsFinite(candidate) || candidate.Length < MinVectorLength)
            {
                value = default;
                return false;
            }

            value = candidate.Normalize();
            return true;
        }

        /// <summary>
        /// 判断原始 value 是否为零向量
        /// </summary>
        public bool HasZeroValue()
        {
            return _value.HasValue && IsFinite(_value.Value) && _value.Value.Length < MinVectorLength;
        }

        /// <summary>
        /// 判断原始 value 是否是有限数值
        /// </summary>
        public bool HasFiniteValue()
        {
            return _value.HasValue && IsFinite(_value.Value);
        }

        /// <summary>
        /// 尝试将语义解析为单位向量
        /// </summary>
        public bool TryResolveSemanticValue(out Vec2D value)
        {
            return FacingHelper.TrySemanticToVector(_semantic, out value);
        }

        /// <summary>
        /// 返回将 value 归一化后的新实例
        /// </summary>
        public Facing WithNormalizedValue()
        {
            if (!TryGetNormalizedValue(out var value))
                return this;

            return new Facing(value, _semantic);
        }

        /// <summary>
        /// 返回仅保留规范 value 的新实例
        /// </summary>
        public Facing WithResolvedValue(Vec2D value, bool clearSemantic = true)
        {
            return new Facing(value.Normalize(), clearSemantic ? null : _semantic);
        }

        public override string ToString()
        {
            var valueText = _value.HasValue ? _value.Value.ToString() : "null";
            var semanticText = _semantic != null ? $"\"{_semantic}\"" : "null";
            return $"{{ value: {valueText}, semantic: {semanticText} }}";
        }

        private static bool IsFinite(Vec2D vector)
        {
            return !double.IsNaN(vector.X) &&
                   !double.IsInfinity(vector.X) &&
                   !double.IsNaN(vector.Y) &&
                   !double.IsInfinity(vector.Y);
        }

        private static Vec2D ParseLegacySemantic(string semantic)
        {
            if (!FacingHelper.TrySemanticToVector(semantic, out var value))
                throw new ArgumentException($"Unknown facing: {semantic}");

            return value;
        }

        private static string DirectionToSemantic(FacingDirection semantic)
        {
            return semantic switch
            {
                FacingDirection.North => "north",
                FacingDirection.South => "south",
                FacingDirection.East => "east",
                FacingDirection.West => "west",
                FacingDirection.Northeast => "northeast",
                FacingDirection.Northwest => "northwest",
                FacingDirection.Southeast => "southeast",
                FacingDirection.Southwest => "southwest",
                _ => throw new ArgumentException($"Unknown facing: {semantic}")
            };
        }
    }
}

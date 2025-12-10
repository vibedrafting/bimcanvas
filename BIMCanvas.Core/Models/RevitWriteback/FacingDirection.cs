using System.Runtime.Serialization;

namespace BIMCanvas.Core.Models.RevitWriteback
{
    /// <summary>
    /// 朝向方向枚举（8 个标准方向）
    /// </summary>
    public enum FacingDirection
    {
        [EnumMember(Value = "north")]
        North,

        [EnumMember(Value = "south")]
        South,

        [EnumMember(Value = "east")]
        East,

        [EnumMember(Value = "west")]
        West,

        [EnumMember(Value = "northeast")]
        Northeast,

        [EnumMember(Value = "northwest")]
        Northwest,

        [EnumMember(Value = "southeast")]
        Southeast,

        [EnumMember(Value = "southwest")]
        Southwest
    }
}

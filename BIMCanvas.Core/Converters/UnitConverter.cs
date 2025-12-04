using System;

namespace BIMCanvas.Core.Converters
{
    /// <summary>
    /// 单位转换工具
    /// </summary>
    public static class UnitConverter
    {
        /// <summary>
        /// 英尺 → 毫米转换系数
        /// </summary>
        public const double FeetToMm = 304.8;

        /// <summary>
        /// 毫米 → 英尺转换系数
        /// </summary>
        public const double MmToFeet = 1.0 / 304.8;

        /// <summary>
        /// 弧度 → 角度转换系数
        /// </summary>
        public const double RadToDeg = 180.0 / Math.PI;

        /// <summary>
        /// 角度 → 弧度转换系数
        /// </summary>
        public const double DegToRad = Math.PI / 180.0;

        /// <summary>
        /// 英尺 → 毫米
        /// </summary>
        public static double ToMillimeters(double feet)
        {
            return feet * FeetToMm;
        }

        /// <summary>
        /// 毫米 → 英尺
        /// </summary>
        public static double ToFeet(double mm)
        {
            return mm * MmToFeet;
        }

        /// <summary>
        /// 弧度 → 角度
        /// </summary>
        public static double ToDegrees(double radians)
        {
            return radians * RadToDeg;
        }

        /// <summary>
        /// 角度 → 弧度
        /// </summary>
        public static double ToRadians(double degrees)
        {
            return degrees * DegToRad;
        }

        /// <summary>
        /// 英寸 → 毫米
        /// </summary>
        public static double InchesToMm(double inches)
        {
            return inches * 25.4;
        }

        /// <summary>
        /// 毫米 → 英寸
        /// </summary>
        public static double MmToInches(double mm)
        {
            return mm / 25.4;
        }
    }
}

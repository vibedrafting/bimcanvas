using System.Collections.Generic;
using BIMCanvas.Core.Models.Shared;

namespace BIMCanvas.Core.Algorithms.Spatial
{
    /// <summary>
    /// 特殊完成面规则表
    /// 根据工作区标签触发特殊厚度覆盖
    /// </summary>
    public static class FinishRules
    {
        /// <summary>
        /// 特殊完成面厚度映射表
        /// 某些工作区标签需要特殊厚度的完成面
        /// </summary>
        public static readonly Dictionary<ZoneTag, double> SpecialFinishThickness = new Dictionary<ZoneTag, double>
        {
            { ZoneTag.TvMedia, 80.0 },   // 电视区 → 80mm（电视背景墙）
            { ZoneTag.Sleep, 60.0 },     // 睡眠区 → 60mm（床头背景墙）
            { ZoneTag.Bar, 40.0 },       // 吧台区 → 40mm
        };

        /// <summary>
        /// 检查标签是否触发特殊完成面厚度
        /// </summary>
        /// <param name="tag">工作区标签</param>
        /// <returns>是否触发特殊厚度</returns>
        public static bool TriggersSpecialFinish(ZoneTag tag)
        {
            return SpecialFinishThickness.ContainsKey(tag);
        }

        /// <summary>
        /// 获取特殊完成面厚度
        /// </summary>
        /// <param name="tag">工作区标签</param>
        /// <returns>厚度（mm），如果标签不触发特殊厚度则返回 null</returns>
        public static double? GetSpecialThickness(ZoneTag tag)
        {
            if (SpecialFinishThickness.TryGetValue(tag, out var thickness))
            {
                return thickness;
            }
            return null;
        }

        /// <summary>
        /// 从标签列表中获取最大特殊厚度
        /// </summary>
        /// <param name="tags">工作区标签列表</param>
        /// <returns>最大厚度（mm），如果没有特殊厚度则返回 null</returns>
        public static double? GetMaxSpecialThickness(IEnumerable<ZoneTag> tags)
        {
            double? maxThickness = null;

            foreach (var tag in tags)
            {
                var thickness = GetSpecialThickness(tag);
                if (thickness.HasValue)
                {
                    if (!maxThickness.HasValue || thickness.Value > maxThickness.Value)
                    {
                        maxThickness = thickness.Value;
                    }
                }
            }

            return maxThickness;
        }
    }
}

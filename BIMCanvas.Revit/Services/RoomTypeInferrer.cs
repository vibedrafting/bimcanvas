using System;
using System.Collections.Generic;
using BIMCanvas.Core.Models.Shared;

namespace BIMCanvas.Revit.Services
{
    /// <summary>
    /// 房间类型推断器
    /// 基于房间名称关键词自动推断 RoomType
    /// </summary>
    public static class RoomTypeInferrer
    {
        /// <summary>
        /// 关键词到房间类型的映射表
        /// </summary>
        private static readonly Dictionary<string, RoomType> KeywordMap = new Dictionary<string, RoomType>(StringComparer.OrdinalIgnoreCase)
        {
            // 中文关键词
            { "客厅", RoomType.LivingRoom },
            { "起居", RoomType.LivingRoom },
            { "起居室", RoomType.LivingRoom },
            { "餐厅", RoomType.DiningRoom },
            { "饭厅", RoomType.DiningRoom },
            { "主卧", RoomType.MasterBedroom },
            { "主人房", RoomType.MasterBedroom },
            { "主卧室", RoomType.MasterBedroom },
            { "卧室", RoomType.Bedroom },
            { "次卧", RoomType.Bedroom },
            { "次卧室", RoomType.Bedroom },
            { "客房", RoomType.Bedroom },
            { "儿童房", RoomType.Bedroom },
            { "书房", RoomType.Study },
            { "办公室", RoomType.Study },
            { "工作室", RoomType.Study },
            { "厨房", RoomType.Kitchen },
            { "卫生间", RoomType.Bathroom },
            { "洗手间", RoomType.Bathroom },
            { "浴室", RoomType.Bathroom },
            { "卫浴", RoomType.Bathroom },
            { "厕所", RoomType.Bathroom },
            { "门厅", RoomType.Entrance },
            { "玄关", RoomType.Entrance },
            { "入户", RoomType.Entrance },
            { "阳台", RoomType.Balcony },
            { "露台", RoomType.Balcony },
            { "走廊", RoomType.Corridor },
            { "过道", RoomType.Corridor },
            { "通道", RoomType.Corridor },
            { "储藏", RoomType.Storage },
            { "储藏室", RoomType.Storage },
            { "储物", RoomType.Storage },
            { "杂物间", RoomType.Storage },

            // 英文关键词
            { "living", RoomType.LivingRoom },
            { "livingroom", RoomType.LivingRoom },
            { "dining", RoomType.DiningRoom },
            { "diningroom", RoomType.DiningRoom },
            { "master", RoomType.MasterBedroom },
            { "masterbedroom", RoomType.MasterBedroom },
            { "bedroom", RoomType.Bedroom },
            { "bed", RoomType.Bedroom },
            { "study", RoomType.Study },
            { "office", RoomType.Study },
            { "kitchen", RoomType.Kitchen },
            { "bathroom", RoomType.Bathroom },
            { "toilet", RoomType.Bathroom },
            { "wc", RoomType.Bathroom },
            { "restroom", RoomType.Bathroom },
            { "entrance", RoomType.Entrance },
            { "entry", RoomType.Entrance },
            { "foyer", RoomType.Entrance },
            { "balcony", RoomType.Balcony },
            { "terrace", RoomType.Balcony },
            { "corridor", RoomType.Corridor },
            { "hallway", RoomType.Corridor },
            { "hall", RoomType.Corridor },
            { "storage", RoomType.Storage },
            { "storeroom", RoomType.Storage },
            { "utility", RoomType.Storage },
        };

        /// <summary>
        /// 房间类型显示名称映射
        /// </summary>
        private static readonly Dictionary<RoomType, string> DisplayNameMap = new Dictionary<RoomType, string>
        {
            { RoomType.LivingRoom, "客厅" },
            { RoomType.DiningRoom, "餐厅" },
            { RoomType.MasterBedroom, "主卧" },
            { RoomType.Bedroom, "卧室" },
            { RoomType.Study, "书房" },
            { RoomType.Kitchen, "厨房" },
            { RoomType.Bathroom, "卫生间" },
            { RoomType.Entrance, "门厅" },
            { RoomType.Balcony, "阳台" },
            { RoomType.Corridor, "走廊" },
            { RoomType.Storage, "储藏室" },
        };

        /// <summary>
        /// 根据房间名称推断类型
        /// </summary>
        /// <param name="roomName">房间名称</param>
        /// <returns>推断的房间类型</returns>
        public static RoomType InferFromName(string? roomName)
        {
            if (string.IsNullOrWhiteSpace(roomName))
                return RoomType.LivingRoom; // 默认值

            var normalizedName = roomName.Trim().ToLowerInvariant();

            // 遍历关键词映射表
            foreach (var kvp in KeywordMap)
            {
                if (normalizedName.Contains(kvp.Key.ToLowerInvariant()))
                {
                    return kvp.Value;
                }
            }

            // 无匹配时返回默认值
            return RoomType.LivingRoom;
        }

        /// <summary>
        /// 获取房间类型的中文显示名称
        /// </summary>
        /// <param name="type">房间类型</param>
        /// <returns>中文显示名称</returns>
        public static string GetDisplayName(RoomType type)
        {
            return DisplayNameMap.TryGetValue(type, out var name) ? name : type.ToString();
        }

        /// <summary>
        /// 获取所有可用的房间类型
        /// </summary>
        /// <returns>房间类型列表</returns>
        public static IEnumerable<RoomType> GetAllTypes()
        {
            return (RoomType[])Enum.GetValues(typeof(RoomType));
        }
    }
}

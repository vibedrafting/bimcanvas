using System.Collections.Generic;
using BIMCanvas.Core.Models.Shared;

namespace BIMCanvas.Server.Services
{
    /// <summary>
    /// RoomType 到 ZoneTag 的映射服务
    /// 负责根据房间类型返回对应的功能标签列表
    /// </summary>
    public class RoomTypeTagMappingService
    {
        /// <summary>
        /// RoomType → ZoneTag[] 映射表
        /// </summary>
        private static readonly Dictionary<RoomType, List<ZoneTag>> RoomTypeTagMapping = new()
        {
            { RoomType.LivingRoom, new List<ZoneTag> { ZoneTag.TvMedia, ZoneTag.Rest, ZoneTag.Display } },
            { RoomType.DiningRoom, new List<ZoneTag> { ZoneTag.Dining } },
            { RoomType.MasterBedroom, new List<ZoneTag> { ZoneTag.Sleep, ZoneTag.WardrobeStorage, ZoneTag.Vanity } },
            { RoomType.Bedroom, new List<ZoneTag> { ZoneTag.Sleep, ZoneTag.WardrobeStorage } },
            { RoomType.Study, new List<ZoneTag> { ZoneTag.Work, ZoneTag.Study, ZoneTag.Reading } },
            { RoomType.Kitchen, new List<ZoneTag> { ZoneTag.Cooking, ZoneTag.FoodPrep, ZoneTag.Bar } },
            { RoomType.Bathroom, new List<ZoneTag> { ZoneTag.Shower, ZoneTag.Toilet, ZoneTag.Washing, ZoneTag.Vanity } },
            { RoomType.Entrance, new List<ZoneTag> { ZoneTag.Entry } },
            { RoomType.Balcony, new List<ZoneTag> { ZoneTag.Rest, ZoneTag.Plants, ZoneTag.Display } },
            { RoomType.Corridor, new List<ZoneTag> { ZoneTag.Passage } },
            { RoomType.Storage, new List<ZoneTag> { ZoneTag.GeneralStorage, ZoneTag.WardrobeStorage, ZoneTag.ShoeStorage } }
        };

        /// <summary>
        /// 根据房间类型获取功能标签列表
        /// </summary>
        /// <param name="roomType">房间类型</param>
        /// <returns>功能标签列表，如果没有映射则返回空列表</returns>
        public List<ZoneTag> GetTagsForRoomType(RoomType roomType)
        {
            if (RoomTypeTagMapping.TryGetValue(roomType, out var tags))
            {
                // 返回副本以防止外部修改
                return new List<ZoneTag>(tags);
            }
            return new List<ZoneTag>();
        }

        /// <summary>
        /// 获取所有已配置的房间类型
        /// </summary>
        /// <returns>已配置映射的房间类型列表</returns>
        public IEnumerable<RoomType> GetConfiguredRoomTypes()
        {
            return RoomTypeTagMapping.Keys;
        }
    }
}

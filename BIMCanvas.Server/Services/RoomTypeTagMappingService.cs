using System.Collections.Generic;
using System.Linq;
using BIMCanvas.Core.Models.Shared;

namespace BIMCanvas.Server.Services
{
    /// <summary>
    /// RoomType 到 ZoneTag 的映射服务
    /// 负责根据房间类型返回对应的必备标签和建议标签列表
    /// </summary>
    public class RoomTypeTagMappingService
    {
        /// <summary>
        /// 必备标签：该房间类型的核心功能，对应家具必须布置
        /// </summary>
        private static readonly Dictionary<RoomType, List<ZoneTag>> RequiredTagMapping = new()
        {
            { RoomType.LivingRoom, new List<ZoneTag> { ZoneTag.TvMedia, ZoneTag.Rest, ZoneTag.Display } },
            { RoomType.DiningRoom, new List<ZoneTag> { ZoneTag.Dining } },
            { RoomType.MasterBedroom, new List<ZoneTag> { ZoneTag.Sleep, ZoneTag.WardrobeStorage } },
            { RoomType.Bedroom, new List<ZoneTag> { ZoneTag.Sleep, ZoneTag.WardrobeStorage } },
            { RoomType.Study, new List<ZoneTag> { ZoneTag.Work, ZoneTag.Study, ZoneTag.Reading } },
            { RoomType.Kitchen, new List<ZoneTag> { ZoneTag.Cooking, ZoneTag.FoodPrep } },
            { RoomType.Bathroom, new List<ZoneTag> { ZoneTag.Shower, ZoneTag.Toilet, ZoneTag.Washing } },
            { RoomType.Entrance, new List<ZoneTag> { ZoneTag.Entry } },
            { RoomType.Balcony, new List<ZoneTag> { ZoneTag.Rest, ZoneTag.Plants, ZoneTag.Display } },
            { RoomType.Corridor, new List<ZoneTag> { ZoneTag.Passage } },
            { RoomType.Storage, new List<ZoneTag> { ZoneTag.GeneralStorage, ZoneTag.WardrobeStorage, ZoneTag.ShoeStorage } }
        };

        /// <summary>
        /// 建议标签：该房间类型可选的扩展功能，对应家具可放可不放
        /// </summary>
        private static readonly Dictionary<RoomType, List<ZoneTag>> OptionalTagMapping = new()
        {
            { RoomType.MasterBedroom, new List<ZoneTag> { ZoneTag.Vanity } },
            { RoomType.Kitchen, new List<ZoneTag> { ZoneTag.Bar } },
            { RoomType.Bathroom, new List<ZoneTag> { ZoneTag.Vanity } },
        };

        /// <summary>
        /// 根据房间类型获取必备功能标签列表
        /// </summary>
        public List<ZoneTag> GetTagsForRoomType(RoomType roomType)
        {
            if (RequiredTagMapping.TryGetValue(roomType, out var tags))
            {
                return new List<ZoneTag>(tags);
            }
            return new List<ZoneTag>();
        }

        /// <summary>
        /// 根据房间类型获取建议功能标签列表
        /// </summary>
        public List<ZoneTag> GetOptionalTagsForRoomType(RoomType roomType)
        {
            if (OptionalTagMapping.TryGetValue(roomType, out var tags))
            {
                return new List<ZoneTag>(tags);
            }
            return new List<ZoneTag>();
        }

        /// <summary>
        /// 获取所有已配置的房间类型
        /// </summary>
        public IEnumerable<RoomType> GetConfiguredRoomTypes()
        {
            return RequiredTagMapping.Keys;
        }
    }
}

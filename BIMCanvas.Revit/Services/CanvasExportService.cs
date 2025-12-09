using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Autodesk.Revit.DB;
using BIMCanvas.Core.Models.Document;
using BIMCanvas.Revit.Adapters;
using BIMCanvas.Revit.Models;
using BIMCanvas.Revit.Views;
using BIMCanvas.Revit.Views.ViewModels;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace BIMCanvas.Revit.Services
{
    /// <summary>
    /// 画布导出服务
    /// 负责组装 CanvasDocument 并保存文件
    /// </summary>
    public class CanvasExportService
    {
        /// <summary>
        /// 从视图导出 CanvasDocument
        /// </summary>
        /// <param name="view">Revit 平面视图</param>
        /// <param name="options">导出选项</param>
        /// <returns>精简版 CanvasDocument</returns>
        /// <exception cref="OperationCanceledException">用户取消导出时抛出</exception>
        public CanvasDocument ExportFromView(View view, ExportOptions options)
        {
            if (view == null)
                throw new ArgumentNullException(nameof(view));
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            // 1. 创建坐标转换器
            var coordAdapter = new CoordinateAdapter(view);

            // 2. 构建元数据（只包含布置高度）
            var metadata = new Metadata
            {
                PlacementElevation = options.PlacementElevation
            };

            // 3. 提取边界
            var revitBoundarys = new List<RevitBoundary>();
            if (options.ExportBoundarys)
            {
                var wallAdapter = new BoundaryAdapter(coordAdapter);
                revitBoundarys = wallAdapter.ExtractBoundarys(view, options.BoundaryCutHeightMm);
            }

            // TODO: 后期实现坐标转换逻辑，将 revitBoundarys 转换为 Core 层的 Boundary

            // 4. 提取门窗
            var revitOpenings = new List<RevitOpening>();
            if (options.ExportOpenings)
            {
                var openingAdapter = new OpeningAdapter(coordAdapter);
                revitOpenings = openingAdapter.ExtractOpenings(view);
            }

            // TODO: 后期实现坐标转换逻辑，将 revitOpenings 转换为 Core 层的 Opening

            // 5. 提取房间
            var rooms = new List<Room>();
            if (options.ExportRooms)
            {
                var roomAdapter = new RoomAdapter(coordAdapter);
                rooms = roomAdapter.ExtractRooms(view);
            }

            // 6. 推断房间类型
            foreach (var room in rooms)
            {
                room.Type = RoomTypeInferrer.InferFromName(room.Name);
            }

            // 7. 显示配置窗口（如果需要）
            if (options.ShowConfigWindow && rooms.Count > 0)
            {
                var viewModel = new ConfigViewModel(rooms);
                var window = new ConfigWindow();
                window.SetViewModel(viewModel);

                var result = window.ShowDialog();

                if (result == true)
                {
                    // 应用用户确认的类型
                    var confirmedTypes = viewModel.GetConfirmedTypes();
                    foreach (var room in rooms)
                    {
                        if (confirmedTypes.TryGetValue(room.Id, out var type))
                        {
                            room.Type = type;
                        }
                    }
                }
                else
                {
                    throw new OperationCanceledException("用户取消了导出操作");
                }
            }

            // 8. 组装 CanvasDocument
            return new CanvasDocument
            {
                Id = $"canvas_{Guid.NewGuid():N}",
                Version = 1,
                CoordinateSystem = "cartesian_mm_yUp",
                Metadata = metadata,
                Outline = new Core.Models.Document.Outline
                {
                    Boundarys = new List<Core.Models.Document.Boundary>(),  // TODO: 转换 revitBoundarys
                    Openings = new List<Core.Models.Document.Opening>()      // TODO: 转换 revitOpenings
                },
                Rooms = rooms,
                Zones = new List<Zone>(),              // 精简版：空
                WallFinishes = new List<WallFinish>(), // 精简版：空
                Modules = new List<Module>()           // 精简版：空
            };
        }

        /// <summary>
        /// 保存 CanvasDocument 到文件
        /// </summary>
        /// <param name="document">画布文档</param>
        /// <param name="filePath">保存路径</param>
        public void SaveToFile(CanvasDocument document, string filePath)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("文件路径不能为空", nameof(filePath));

            var settings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore,
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            };

            var json = JsonConvert.SerializeObject(document, settings);
            File.WriteAllText(filePath, json, Encoding.UTF8);
        }

        /// <summary>
        /// 将 CanvasDocument 序列化为 JSON 字符串
        /// </summary>
        /// <param name="document">画布文档</param>
        /// <returns>JSON 字符串</returns>
        public string ToJson(CanvasDocument document)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            var settings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore,
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            };

            return JsonConvert.SerializeObject(document, settings);
        }
    }
}

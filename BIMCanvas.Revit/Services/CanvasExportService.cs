using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using BIMCanvas.Core.Converters;
using BIMCanvas.Core.Models.Document;
using BIMCanvas.Revit.Adapters;
using BIMCanvas.Revit.Models;
using BIMCanvas.Revit.Views;
using BIMCanvas.Revit.Views.ViewModels;
using NetTopologySuite.Geometries;
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
        /// 从视图导出 CanvasDocument（6 阶段流程）
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

            // ===== Phase 1: 提取原始数据 =====
            var rawBoundaries = new List<RevitBoundary>();
            var rawOpenings = new List<RevitOpening>();
            var revitRooms = new List<RevitRoom>();

            if (options.ExportBoundarys)
            {
                var boundaryAdapter = new BoundaryAdapter(options);
                rawBoundaries = boundaryAdapter.ExtractBoundaries(view);
            }

            if (options.ExportOpenings)
            {
                var openingAdapter = new OpeningAdapter();
                rawOpenings = openingAdapter.ExtractOpenings(view);
            }

            if (options.ExportRooms)
            {
                var roomAdapter = new RoomAdapter();
                revitRooms = roomAdapter.ExtractRooms(view);
            }

            // ===== Phase 2: 计算包围盒原点 =====
            var boundaryPolygons = rawBoundaries
                .Where(b => b.Boundary != null)
                .Select(b => b.Boundary)
                .ToList();

            var roomPolygons = revitRooms
                .Where(r => r.Boundary != null)
                .Select(r => r.Boundary)
                .ToList();

            var allPolygons = boundaryPolygons.Concat(roomPolygons).ToList();

            Coordinate origin;
            string originMethod;

            if (allPolygons.Count > 0)
            {
                // 使用 NTS Envelope 计算所有多边形的包围盒
                var envelope = new Envelope();
                foreach (var polygon in allPolygons)
                {
                    envelope.ExpandToInclude(polygon.EnvelopeInternal);
                }
                origin = new Coordinate(envelope.MinX, envelope.MinY);
                originMethod = "boundingBox";
            }
            else
            {
                // 降级策略：使用视图裁剪框
                var cropMin = view.CropBoxActive ? view.CropBox.Min : XYZ.Zero;
                origin = new Coordinate(cropMin.X, cropMin.Y);
                originMethod = "cropBox";
            }

            // ===== Phase 3: 创建坐标转换器 =====
            double rotation = GetViewRotation(view);
            var transformer = new CoordinateTransformer(origin, rotation);

            // ===== Phase 4: 统一坐标转换 =====
            var boundaries = rawBoundaries.Select(rb => new Boundary
            {
                Id = rb.Id,
                Polygon = NtsConverter.FromNtsPolygon(transformer.TransformPolygon(rb.Boundary))
            }).ToList();

            var openings = rawOpenings.Select(ro => new Core.Models.Document.Opening
            {
                Id = ro.Id,
                Type = ro.Type,
                Line = NtsConverter.FromNtsLineSegment(transformer.TransformLineSegment(ro.LocationLine)),
                FacingDirection = NtsConverter.FromNtsVector2D(transformer.TransformVector2D(ro.FacingDirection)),
                HandDirections = ro.HandDirections?.Count > 0
                    ? ro.HandDirections.Select(hd => NtsConverter.FromNtsVector2D(transformer.TransformVector2D(hd))).ToList()
                    : null
            }).ToList();

            var rooms = revitRooms.Select(rr => new Core.Models.Document.Room
            {
                Id = rr.Id,
                Name = rr.Name,
                Type = RoomTypeInferrer.InferFromName(rr.Name),
                Boundary = NtsConverter.FromNtsPolygon(transformer.TransformPolygon(rr.Boundary))
            }).ToList();

            // ===== Phase 5: 用户确认房间类型 =====
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

            // ===== Phase 6: 保存转换配置到 Metadata + 组装 CanvasDocument =====
            var metadata = new Metadata
            {
                PlacementElevation = options.PlacementElevation,
                CoordinateTransform = new CoordinateTransform
                {
                    Origin = new[]
                    {
                        UnitConverter.ToMillimeters(origin.X),
                        UnitConverter.ToMillimeters(origin.Y),
                        0.0
                    },
                    Rotation = rotation,
                    Method = originMethod
                }
            };

            return new CanvasDocument
            {
                Id = $"canvas_{Guid.NewGuid():N}",
                Version = 1,
                CoordinateSystem = "cartesian_mm_yUp",
                Metadata = metadata,
                Outline = new Core.Models.Document.Outline
                {
                    Boundarys = boundaries,
                    Openings = openings
                },
                Rooms = rooms,
                Zones = new List<Zone>(),              // 精简版：空
                WallFinishes = new List<WallFinish>(), // 精简版：空
                Modules = new List<Module>()           // 精简版：空
            };
        }

        /// <summary>
        /// 获取视图旋转角度（弧度）
        /// </summary>
        private double GetViewRotation(View view)
        {
            var rightDir = view.RightDirection;
            return Math.Atan2(rightDir.Y, rightDir.X);
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

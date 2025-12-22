using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using BIMCanvas.Core.Converters;
using BIMCanvas.Core.Models.Document;
using BIMCanvas.Core.Models.Revit;
using BIMCanvas.Core.Models.Computed;
using BIMCanvas.Core.Models.Layout;
using BIMCanvas.Core.Models.Shared;
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
    /// 负责组装 DesignDocument 并保存文件
    /// </summary>
    public class CanvasExportService
    {
        /// <summary>
        /// 从视图导出 DesignDocument（6 阶段流程）
        /// </summary>
        /// <param name="view">Revit 平面视图</param>
        /// <param name="options">导出选项</param>
        /// <returns>精简版 DesignDocument</returns>
        /// <exception cref="OperationCanceledException">用户取消导出时抛出</exception>
        public DesignDocument ExportFromView(View view, ExportOptions options)
        {
            if (view == null)
                throw new ArgumentNullException(nameof(view));
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            // ===== Phase 1: 提取原始数据 =====
            var rawWallFinishes = new List<RevitWallFinish>();
            var rawOpenings = new List<RevitOpening>();
            var revitRooms = new List<RevitRoom>();
            var revitWalls = new List<RevitWall>();
            var revitColumns = new List<RevitColumn>();

            // 提取墙面完成面定位边界（墙柱组合轮廓）
            if (options.ExportBoundarys)
            {
                var wallFinishAdapter = new WallFinishAdapter(options);
                rawWallFinishes = wallFinishAdapter.ExtractWallFinishes(view);
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

            // 提取单独墙体和柱子轮廓
            if (options.ExportElementOutlines)
            {
                var boundaryAdapter = new BoundaryAdapter(options);
                (revitWalls, revitColumns) = boundaryAdapter.ExtractBoundaries(view);
            }

            // ===== Phase 2: 计算包围盒原点 =====
            var wallFinishPolygons = rawWallFinishes
                .Where(wf => wf.Boundary != null)
                .Select(wf => wf.Boundary)
                .ToList();

            var roomPolygons = revitRooms
                .Where(r => r.Boundary != null)
                .Select(r => r.Boundary)
                .ToList();

            var wallPolygons = revitWalls
                .Where(w => w.Boundary != null)
                .Select(w => w.Boundary)
                .ToList();

            var columnPolygons = revitColumns
                .Where(c => c.Boundary != null)
                .Select(c => c.Boundary)
                .ToList();

            var allPolygons = wallFinishPolygons
                .Concat(roomPolygons)
                .Concat(wallPolygons)
                .Concat(columnPolygons)
                .ToList();

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

            // 转换单独墙体轮廓
            var walls = revitWalls.Select(w => new Core.Models.Revit.Wall
            {
                Id = w.Id,
                ElementId = w.ElementId,
                Polygon = NtsConverter.FromNtsPolygon(transformer.TransformPolygon(w.Boundary))
            }).ToList();

            // 转换单独柱子轮廓
            var columns = revitColumns.Select(c => new Column
            {
                Id = c.Id,
                ElementId = c.ElementId,
                IsStructural = c.IsStructural,
                Polygon = NtsConverter.FromNtsPolygon(transformer.TransformPolygon(c.Boundary))
            }).ToList();

            // 门窗转换
            var openings = rawOpenings.Select(ro => new Core.Models.Revit.Opening
            {
                Id = ro.Id,
                Type = ro.Type,
                Line = NtsConverter.FromNtsLineSegment(transformer.TransformLineSegment(ro.LocationLine)),
                FacingDirection = NtsConverter.FromNtsVector2D(transformer.TransformVector2D(ro.FacingDirection)),
                HandDirections = ro.HandDirections?.Count > 0
                    ? ro.HandDirections.Select(hd => NtsConverter.FromNtsVector2D(transformer.TransformVector2D(hd))).ToList()
                    : null
            }).ToList();

            // 过滤外墙边，只保留内墙边
            var filteredWallFinishes = FilterExteriorEdges(rawWallFinishes, revitRooms);

            // 转换完成面定位边界
            var finishLocationBoundaries = filteredWallFinishes.Select(wf => new FinishLocationBoundary
            {
                Id = wf.Id,
                ElementIds = wf.ElementIds,
                Polygon = NtsConverter.FromNtsPolygon(transformer.TransformPolygon(wf.Boundary))
            }).ToList();

            // 房间转换
            var rooms = revitRooms.Select(rr => new Core.Models.Revit.Room
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

            // ===== Phase 6: 保存转换配置到 Metadata + 组装 DesignDocument =====
            var metadata = new Metadata
            {
                PlacementElevation = options.PlacementElevation,
                // 坐标变换参数（已合并到 Metadata）
                Origin = new[]
                {
                    UnitConverter.ToMillimeters(origin.X),
                    UnitConverter.ToMillimeters(origin.Y),
                    0.0
                },
                Rotation = rotation,
                Method = originMethod
            };

            return new DesignDocument
            {
                // 常规属性
                Id = $"canvas_{Guid.NewGuid():N}",
                ProjectName = view.Document.Title ?? "",
                ExportDate = DateTime.Now,
                Version = 1,
                CoordinateSystem = "cartesian_mm_yUp",

                // Revit 原始数据
                Revit = new RevitData
                {
                    Metadata = metadata,
                    Walls = walls,
                    Columns = columns,
                    Openings = openings,
                    FinishLocationBoundaries = finishLocationBoundaries,
                    Rooms = rooms
                },

                // 计算派生数据（精简版：空）
                Computed = new ComputedData
                {
                    Zones = new List<Zone>(),
                    WallFinishes = new List<WallFinish>()
                },

                // 方案布置（精简版：空）
                Layout = new LayoutData()
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
        /// 保存 DesignDocument 到文件
        /// </summary>
        /// <param name="document">设计文档</param>
        /// <param name="filePath">保存路径</param>
        public void SaveToFile(DesignDocument document, string filePath)
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
        /// 将 DesignDocument 序列化为 JSON 字符串
        /// </summary>
        /// <param name="document">设计文档</param>
        /// <returns>JSON 字符串</returns>
        public string ToJson(DesignDocument document)
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

        #region 外墙过滤逻辑

        /// <summary>
        /// 过滤外墙边，只保留内墙边构成的定位线
        /// </summary>
        /// <param name="wallFinishes">WallFinishAdapter 提取的原始轮廓</param>
        /// <param name="rooms">RoomAdapter 提取的房间列表</param>
        /// <returns>过滤后的轮廓（仅包含内墙边）</returns>
        private List<RevitWallFinish> FilterExteriorEdges(
            List<RevitWallFinish> wallFinishes,
            List<RevitRoom> rooms)
        {
            var result = new List<RevitWallFinish>();

            foreach (var wallFinish in wallFinishes)
            {
                if (wallFinish.Boundary == null) continue;

                var shell = wallFinish.Boundary.Shell;
                var interiorCoordinates = new List<Coordinate>();
                bool hasInteriorEdge = false;

                // 遍历轮廓的每条边
                for (int i = 0; i < shell.NumPoints - 1; i++)
                {
                    var p0 = shell.GetCoordinateN(i);
                    var p1 = shell.GetCoordinateN(i + 1);

                    // 判断是否为内墙边
                    if (IsInteriorEdge(p0, p1, rooms))
                    {
                        if (!hasInteriorEdge)
                        {
                            interiorCoordinates.Add(p0);
                        }
                        interiorCoordinates.Add(p1);
                        hasInteriorEdge = true;
                    }
                }

                // 如果有内墙边，创建过滤后的结果
                if (hasInteriorEdge && interiorCoordinates.Count >= 3)
                {
                    // 确保闭合
                    if (!interiorCoordinates[0].Equals2D(interiorCoordinates[interiorCoordinates.Count - 1]))
                    {
                        interiorCoordinates.Add(interiorCoordinates[0]);
                    }

                    try
                    {
                        var filteredPolygon = new Polygon(new LinearRing(interiorCoordinates.ToArray()));
                        result.Add(new RevitWallFinish
                        {
                            Id = wallFinish.Id,
                            ElementIds = wallFinish.ElementIds,
                            Boundary = filteredPolygon
                        });
                    }
                    catch
                    {
                        // 如果无法创建有效多边形，跳过
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 判断边是否为内墙边（内侧在任何 Room 内）
        /// </summary>
        private bool IsInteriorEdge(Coordinate p0, Coordinate p1, List<RevitRoom> rooms)
        {
            // 1. 计算边的中点
            var midX = (p0.X + p1.X) / 2;
            var midY = (p0.Y + p1.Y) / 2;

            // 2. 计算边的内侧法向（逆时针轮廓的右侧）
            var dx = p1.X - p0.X;
            var dy = p1.Y - p0.Y;
            var len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-6) return false;  // 忽略零长度边

            // 垂直于边的方向（右侧）
            var normalX = dy / len;
            var normalY = -dx / len;

            // 3. 在中点内侧方向偏移一小段距离（0.1 feet ≈ 30mm）
            var testPoint = new NetTopologySuite.Geometries.Point(new Coordinate(
                midX + normalX * 0.1,
                midY + normalY * 0.1
            ));

            // 4. 检查测试点是否在任何 Room 内
            foreach (var room in rooms)
            {
                if (room.Boundary != null && room.Boundary.Contains(testPoint))
                {
                    return true;  // 在房间内，是内墙边
                }
            }

            return false;  // 不在任何房间内，是外墙边
        }

        #endregion
    }
}

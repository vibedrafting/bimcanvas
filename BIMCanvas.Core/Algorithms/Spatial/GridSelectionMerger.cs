using System;
using System.Collections.Generic;
using System.Linq;
using BIMCanvas.Core.Converters;
using BIMCanvas.Core.Models.Geometry;
using NetTopologySuite.Geometries;

namespace BIMCanvas.Core.Algorithms.Spatial
{
    /// <summary>
    /// 将 Web 端临时网格选择合并、裁剪为 Agent 可消费的空间标记几何。
    /// </summary>
    public static class GridSelectionMerger
    {
        private const double AreaTolerance = 1e-3;
        private const double CoordinateTolerance = 1e-6;

        private static readonly GeometryFactory Factory = new GeometryFactory();

        public static List<SpatialGeometry> MergeGridCells(
            Polygon2D zoneBoundary,
            double originX,
            double originY,
            double cellSize,
            IEnumerable<(int Col, int Row)> cells)
        {
            if (zoneBoundary == null)
                throw new ArgumentNullException(nameof(zoneBoundary));
            if (zoneBoundary.Vertices.Length < 3)
                throw new ArgumentException("Zone boundary must contain at least 3 vertices.", nameof(zoneBoundary));
            if (cellSize <= 0 || double.IsNaN(cellSize) || double.IsInfinity(cellSize))
                throw new ArgumentOutOfRangeException(nameof(cellSize), "Cell size must be positive.");
            if (cells == null)
                throw new ArgumentNullException(nameof(cells));

            var uniqueCells = cells
                .Distinct()
                .ToList();

            if (uniqueCells.Count == 0)
                return new List<SpatialGeometry>();

            if (uniqueCells.Any(c => c.Col < 0 || c.Row < 0))
                throw new ArgumentException("Cell col/row must be non-negative.", nameof(cells));

            var cellPolygons = uniqueCells
                .Select(cell => CreateCellPolygon(originX, originY, cellSize, cell.Col, cell.Row))
                .Cast<Geometry>()
                .ToList();

            if (cellPolygons.Count == 0)
                return new List<SpatialGeometry>();

            var selectionUnion = Factory.BuildGeometry(cellPolygons).Union();
            if (selectionUnion.IsEmpty)
                return new List<SpatialGeometry>();

            var zonePolygon = NtsConverter.ToNtsPolygon(zoneBoundary);
            var clipped = selectionUnion.Intersection(zonePolygon);
            if (clipped.IsEmpty)
                return new List<SpatialGeometry>();

            var polygons = new List<Polygon>();
            ExtractPolygons(clipped, polygons);

            return polygons
                .Where(p => !p.IsEmpty && p.Area > AreaTolerance)
                .Select(p => ToSpatialGeometry(p, originX, originY))
                .Where(g => g != null)
                .Cast<SpatialGeometry>()
                .ToList();
        }

        private static Polygon CreateCellPolygon(double originX, double originY, double cellSize, int col, int row)
        {
            var minX = originX + col * cellSize;
            var minY = originY + row * cellSize;
            var maxX = minX + cellSize;
            var maxY = minY + cellSize;

            return Factory.CreatePolygon(new[]
            {
                new Coordinate(minX, minY),
                new Coordinate(maxX, minY),
                new Coordinate(maxX, maxY),
                new Coordinate(minX, maxY),
                new Coordinate(minX, minY)
            });
        }

        private static void ExtractPolygons(Geometry geometry, List<Polygon> output)
        {
            if (geometry == null || geometry.IsEmpty)
                return;

            if (geometry is Polygon polygon)
            {
                output.Add(polygon);
                return;
            }

            if (geometry is MultiPolygon multiPolygon)
            {
                for (var i = 0; i < multiPolygon.NumGeometries; i++)
                {
                    ExtractPolygons(multiPolygon.GetGeometryN(i), output);
                }
                return;
            }

            if (geometry is GeometryCollection collection)
            {
                for (var i = 0; i < collection.NumGeometries; i++)
                {
                    ExtractPolygons(collection.GetGeometryN(i), output);
                }
            }
        }

        private static SpatialGeometry? ToSpatialGeometry(Polygon polygon, double originX, double originY)
        {
            var shell = NormalizeRing(ToLocalPoints(polygon.ExteriorRing.Coordinates, originX, originY));
            if (shell.Length < 3)
                return null;

            var holes = new List<Point2D[]>();
            for (var i = 0; i < polygon.NumInteriorRings; i++)
            {
                var hole = NormalizeRing(ToLocalPoints(polygon.GetInteriorRingN(i).Coordinates, originX, originY));
                if (hole.Length >= 3)
                    holes.Add(hole);
            }

            if (holes.Count == 0 && TryGetAxisAlignedRectangleAabb(shell, out var aabb))
            {
                return new SpatialGeometry { Aabb = aabb };
            }

            return new SpatialGeometry
            {
                Polygon = new Polygon2D(shell, holes.Count > 0 ? holes.ToArray() : null)
            };
        }

        private static Point2D[] ToLocalPoints(Coordinate[] coordinates, double originX, double originY)
        {
            return coordinates
                .Select(c => new Point2D(
                    Math.Round(c.X - originX),
                    Math.Round(c.Y - originY)))
                .ToArray();
        }

        private static Point2D[] NormalizeRing(Point2D[] ring)
        {
            var withoutClosingPoint = RemoveClosingPoint(ring);
            var withoutDuplicatePoints = RemoveConsecutiveDuplicates(withoutClosingPoint);
            var simplified = RemoveCollinearPoints(withoutDuplicatePoints);

            return simplified.Length >= 3 ? simplified : Array.Empty<Point2D>();
        }

        private static Point2D[] RemoveClosingPoint(Point2D[] points)
        {
            if (points.Length > 1 && AreSamePoint(points[0], points[points.Length - 1]))
                return points.Take(points.Length - 1).ToArray();

            return points;
        }

        private static Point2D[] RemoveConsecutiveDuplicates(Point2D[] points)
        {
            if (points.Length == 0)
                return points;

            var result = new List<Point2D> { points[0] };
            for (var i = 1; i < points.Length; i++)
            {
                if (!AreSamePoint(points[i], result[result.Count - 1]))
                    result.Add(points[i]);
            }

            if (result.Count > 1 && AreSamePoint(result[0], result[result.Count - 1]))
                result.RemoveAt(result.Count - 1);

            return result.ToArray();
        }

        private static Point2D[] RemoveCollinearPoints(Point2D[] points)
        {
            if (points.Length <= 3)
                return points;

            var working = points.ToList();
            var changed = true;

            while (changed && working.Count > 3)
            {
                changed = false;
                for (var i = 0; i < working.Count; i++)
                {
                    var prev = working[(i - 1 + working.Count) % working.Count];
                    var current = working[i];
                    var next = working[(i + 1) % working.Count];

                    if (IsCollinear(prev, current, next))
                    {
                        working.RemoveAt(i);
                        changed = true;
                        break;
                    }
                }
            }

            return working.ToArray();
        }

        private static bool TryGetAxisAlignedRectangleAabb(Point2D[] points, out AABB aabb)
        {
            aabb = default;
            if (points.Length != 4)
                return false;

            var minX = points.Min(p => p.X);
            var minY = points.Min(p => p.Y);
            var maxX = points.Max(p => p.X);
            var maxY = points.Max(p => p.Y);

            if (Math.Abs(maxX - minX) <= CoordinateTolerance || Math.Abs(maxY - minY) <= CoordinateTolerance)
                return false;

            var corners = new[]
            {
                new Point2D(minX, minY),
                new Point2D(maxX, minY),
                new Point2D(maxX, maxY),
                new Point2D(minX, maxY)
            };

            if (points.All(p => corners.Any(c => AreSamePoint(p, c))))
            {
                aabb = new AABB(minX, minY, maxX, maxY);
                return true;
            }

            return false;
        }

        private static bool AreSamePoint(Point2D a, Point2D b)
        {
            return Math.Abs(a.X - b.X) <= CoordinateTolerance &&
                   Math.Abs(a.Y - b.Y) <= CoordinateTolerance;
        }

        private static bool IsCollinear(Point2D a, Point2D b, Point2D c)
        {
            var cross = (b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X);
            return Math.Abs(cross) <= CoordinateTolerance;
        }
    }
}

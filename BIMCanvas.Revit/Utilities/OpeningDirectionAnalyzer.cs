using Autodesk.Revit.DB;
using Autodesk.Revit.DB.IFC;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BIMCanvas.Revit.Utilities
{
    /// <summary>
    /// 门窗开启方向判断工具类
    /// </summary>
    public class OpeningDirectionAnalyzer
    {
        #region 门开启方向 API

        /// <summary>
        /// 根据弧线集合和门实例计算面向方向和开启方向
        /// </summary>
        /// <param name="arcs">门的开启弧线集合</param>
        /// <param name="doorInstance">门族实例</param>
        /// <returns>包含面向方向和开启方向列表的命名元组</returns>
        public static (XYZ FacingDirection, List<XYZ> OpeningDirections) CalculateOpeningDirections(FamilyInstance doorInstance)
        {
            List<XYZ> openingDirections = new List<XYZ>();

            // 获取门的面向方向（确保在水平面上）
            XYZ facingDirection = doorInstance.FacingOrientation;
            facingDirection = new XYZ(facingDirection.X, facingDirection.Y, 0).Normalize();

            // 调用方法获取项目中门的实际开启弧线
            List<Arc> arcs = GetDoorTransformedArcs(doorInstance);

            if (arcs == null || arcs.Count == 0)
            {
                return (FacingDirection: facingDirection, OpeningDirections: openingDirections); // 返回面向方向和空的开启方向列表
            }

            if (arcs.Count == 1)
            {
                // 单开门情况
                XYZ direction = CalculateSingleArcDirection(arcs[0], facingDirection);
                openingDirections.Add(direction);
            }
            else if (arcs.Count == 2)
            {
                // 双弧线情况：需要判断是双开门还是子母门
                Arc arc1 = arcs[0];
                Arc arc2 = arcs[1];

                double radius1 = arc1.Radius;
                double radius2 = arc2.Radius;

                // 设置半径相等的容差（考虑到浮点数精度问题）
                double tolerance = 1e-6;

                if (Math.Abs(radius1 - radius2) < tolerance)
                {
                    // 双开门情况：两个弧线半径相同
                    XYZ direction1 = CalculateSingleArcDirection(arc1, facingDirection);
                    XYZ direction2 = CalculateSingleArcDirection(arc2, facingDirection);

                    openingDirections.Add(direction1);
                    openingDirections.Add(direction2);
                }
                else
                {
                    // 子母门情况：使用较大半径的弧线
                    Arc mainArc = radius1 > radius2 ? arc1 : arc2;

                    XYZ direction = CalculateSingleArcDirection(mainArc, facingDirection);
                    openingDirections.Add(direction);
                }
            }
            else
            {
                // 超过2个弧线的异常情况
                XYZ direction = CalculateSingleArcDirection(arcs[0], facingDirection);
                openingDirections.Add(direction);
            }

            return (FacingDirection: facingDirection, OpeningDirections: openingDirections);
        }

        #endregion

        #region 窗开启方向 API

        /// <summary>
        /// 获取窗的内外开启方向（面向方向）
        /// </summary>
        /// <param name="windowInstance">窗族实例</param>
        /// <returns>窗的面向方向单位向量（水平投影）</returns>
        public static XYZ GetWindowFacingDirection(FamilyInstance windowInstance)
        {
            if (windowInstance == null)
            {
                throw new ArgumentNullException(nameof(windowInstance), "窗实例不能为空");
            }

            // 检查是否为窗类别
            if (windowInstance.Category?.Id.IntegerValue != (int)BuiltInCategory.OST_Windows)
            {
                throw new ArgumentException("传入的实例不是窗类别");
            }

            // 窗的 FacingOrientation 直接表示内外开启方向
            XYZ facing = windowInstance.FacingOrientation;
            return new XYZ(facing.X, facing.Y, 0).Normalize();
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 获取指定门的开启弧线在项目坐标系中的Arc对象
        /// </summary>
        /// <param name="doorInstance">门族实例</param>
        /// <returns>门在项目坐标系中的弧线Arc对象列表</returns>
        public static List<Arc> GetDoorTransformedArcs(FamilyInstance doorInstance)
        {
            List<Arc> result = new List<Arc>();

            try
            {
                if (doorInstance == null)
                {
                    throw new ArgumentNullException(nameof(doorInstance), "门实例不能为空");
                }

                // 检查是否为门类别
                if (doorInstance.Category?.Id.IntegerValue != (int)BuiltInCategory.OST_Doors)
                {
                    throw new ArgumentException("传入的实例不是门类别");
                }

                // 根据门实例获取门的Family类型
                Family doorFamily = doorInstance.Symbol.Family;

                // 调用GetDoor2DArcsFromFamily获取门的开启弧线
                IList<Arc> doorArcs = ExporterIFCUtils.GetDoor2DArcsFromFamily(doorFamily);

                if (doorArcs != null && doorArcs.Count > 0)
                {
                    // 获取门实例的坐标变换信息（考虑翻转）
                    Transform doorTransform = GetDoorInstanceTransformWithFlipping(doorInstance);

                    // 对每条弧线进行坐标变换，获取变换后的Arc对象
                    for (int i = 0; i < doorArcs.Count; i++)
                    {
                        Arc originalArc = doorArcs[i];

                        // 使用Transform.OfCurve进行变换
                        Curve transformedCurve = originalArc.CreateTransformed(doorTransform);

                        // 将变换后的Curve转换为Arc
                        if (transformedCurve is Arc transformedArc)
                        {
                            result.Add(transformedArc);
                        }
                        else
                        {
                            // 如果变换后不是Arc，尝试手动创建Arc
                            Arc manualArc = CreateArcFromTransformedCurve(originalArc, doorTransform);
                            if (manualArc != null)
                            {
                                result.Add(manualArc);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"获取门弧线时出错: {ex.Message}", ex);
            }

            return result;
        }

        /// <summary>
        /// 当变换后的曲线不是Arc时，手动创建Arc对象
        /// </summary>
        /// <param name="originalArc">原始弧线</param>
        /// <param name="transform">变换矩阵</param>
        /// <returns>手动创建的Arc对象</returns>
        public static Arc CreateArcFromTransformedCurve(Arc originalArc, Transform transform)
        {
            try
            {
                // 变换弧线的关键点
                XYZ transformedCenter = transform.OfPoint(originalArc.Center);
                XYZ transformedStartPoint = transform.OfPoint(originalArc.GetEndPoint(0));
                XYZ transformedEndPoint = transform.OfPoint(originalArc.GetEndPoint(1));

                // 变换弧线的方向向量
                XYZ transformedNormal = transform.OfVector(originalArc.Normal).Normalize();
                XYZ transformedXDirection = transform.OfVector(originalArc.XDirection).Normalize();

                // 计算变换后的半径（可能因为缩放而改变）
                double transformedRadius = transformedCenter.DistanceTo(transformedStartPoint);

                // 获取原始弧线的参数范围
                double startParam = originalArc.GetEndParameter(0);
                double endParam = originalArc.GetEndParameter(1);

                // 创建新的Arc
                Arc newArc = Arc.Create(transformedCenter, transformedRadius, startParam, endParam,
                                       transformedXDirection, transformedNormal);

                return newArc;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// 获取门实例的坐标变换矩阵，考虑门的翻转状态
        /// </summary>
        /// <param name="doorInstance">门族实例</param>
        /// <returns>考虑翻转的坐标变换矩阵</returns>
        public static Transform GetDoorInstanceTransformWithFlipping(FamilyInstance doorInstance)
        {
            // 获取门实例的位置信息
            LocationPoint locationPoint = doorInstance.Location as LocationPoint;

            if (locationPoint != null)
            {
                // 获取门的基本位置和旋转
                XYZ position = locationPoint.Point;

                // 使用朝向和开启方向直接构建变换矩阵
                XYZ facingDirection = doorInstance.FacingOrientation;
                XYZ handDirection = doorInstance.HandOrientation;
                XYZ upDirection = XYZ.BasisZ;

                // 构建变换矩阵
                Transform doorTransform = Transform.Identity;
                doorTransform.Origin = position;
                doorTransform.BasisX = handDirection.Normalize();
                doorTransform.BasisY = facingDirection.Normalize();
                doorTransform.BasisZ = upDirection;

                return doorTransform;
            }
            else
            {
                return doorInstance.GetTotalTransform();
            }
        }

        /// <summary>
        /// 计算单个弧线的开启方向
        /// </summary>
        /// <param name="arc">单个弧线</param>
        /// <param name="facingDirection">门的面向方向</param>
        /// <returns>开启方向单位向量</returns>
        public static XYZ CalculateSingleArcDirection(Arc arc, XYZ facingDirection)
        {
            // 计算垂直于面向方向的左右方向
            XYZ rightDirection = facingDirection.CrossProduct(XYZ.BasisZ).Normalize();
            XYZ leftDirection = -rightDirection;

            // 获取弧线的两个端点和中心
            XYZ center = arc.Center;
            center = new XYZ(center.X, center.Y, 0);

            XYZ point0 = arc.GetEndPoint(0);
            point0 = new XYZ(point0.X, point0.Y, 0);

            XYZ point1 = arc.GetEndPoint(1);
            point1 = new XYZ(point1.X, point1.Y, 0);

            // 计算两个端点到圆心的径向向量
            XYZ radial0 = (point0 - center).Normalize();
            XYZ radial1 = (point1 - center).Normalize();

            // 计算径向向量与面向方向的点积，找到与面向方向最平行的端点
            double dot0 = Math.Abs(radial0.DotProduct(facingDirection));
            double dot1 = Math.Abs(radial1.DotProduct(facingDirection));

            XYZ closedPosition, openPosition;

            // 点积值越大，越平行，该端点为关闭位置
            if (dot0 > dot1)
            {
                closedPosition = point0;
                openPosition = point1;
            }
            else
            {
                closedPosition = point1;
                openPosition = point0;
            }

            // 计算从开启位置到关闭位置的向量（开启方向）
            XYZ openingVector = (closedPosition - openPosition);
            openingVector = new XYZ(openingVector.X, openingVector.Y, 0).Normalize();

            // 判断开启向量更接近左方向还是右方向
            double dotWithRight = openingVector.DotProduct(rightDirection);
            double dotWithLeft = openingVector.DotProduct(leftDirection);

            XYZ openingDirection = Math.Abs(dotWithRight) > Math.Abs(dotWithLeft) ?
                (dotWithRight > 0 ? rightDirection : -rightDirection) :
                (dotWithLeft > 0 ? leftDirection : -leftDirection);

            // 验证开启方向与面向方向垂直
            double dotProduct = Math.Abs(openingDirection.DotProduct(facingDirection));
            if (dotProduct > 1e-6)
            {
            }

            return openingDirection;
        }

        /// <summary>
        /// 计算两个XYZ向量之间的有向夹角，返回量化为90度倍数的角度值（以逆时针为正）
        /// </summary>
        /// <param name="vector1">第一个向量</param>
        /// <param name="vector2">第二个向量</param>
        /// <returns>夹角（度数），只返回0, 90, 180, -90四个值之一</returns>
        public static double GetVectorsAngle(XYZ vector1, XYZ vector2)
        {
            try
            {
                // 检查输入向量是否有效
                if (vector1 == null || vector2 == null)
                    throw new ArgumentNullException("输入向量不能为空");

                if (vector1.IsZeroLength() || vector2.IsZeroLength())
                    throw new ArgumentException("输入向量长度不能为零");

                // 将向量标准化（单位化）
                XYZ normalizedVector1 = vector1.Normalize();
                XYZ normalizedVector2 = vector2.Normalize();

                // 计算点积，用于获取夹角的余弦值
                double dotProduct = normalizedVector1.DotProduct(normalizedVector2);

                // 限制点积值在有效范围内，避免浮点精度问题
                dotProduct = Math.Max(-1.0, Math.Min(1.0, dotProduct));

                // 计算叉积，用于判断旋转方向
                XYZ crossProduct = normalizedVector1.CrossProduct(normalizedVector2);

                // 使用atan2函数计算有向夹角（弧度）
                double signedAngleRadians = Math.Atan2(crossProduct.Z, dotProduct);

                // 将弧度转换为度数
                double signedAngleDegrees = signedAngleRadians * 180.0 / Math.PI;

                // 将角度量化为90度的倍数
                // 将角度除以90度，然后四舍五入到最近的整数
                int multiplier = (int)Math.Round(signedAngleDegrees / 90.0);

                // 将倍数限制在[-2, 2]范围内，对应[-180, 180]
                if (multiplier > 2) multiplier = 2;
                if (multiplier < -2) multiplier = -2;

                // 特殊处理：当multiplier为2时，返回-180而不是180（保持在[-180, 180)范围内）
                if (multiplier == 2) multiplier = -2;

                return multiplier * 90.0;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("计算向量夹角时发生错误", ex);
            }
        }

        #endregion
    }
}
